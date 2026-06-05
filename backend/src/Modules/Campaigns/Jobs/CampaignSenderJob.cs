using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shared.Infrastructure;
using Modules.Campaigns.Domain;
using Modules.Conversations.Domain;
using Hangfire;

namespace Modules.Campaigns.Jobs
{
    public class CampaignSenderJob
    {
        private readonly AppDbContext _dbContext;
        private readonly HttpClient _httpClient;
        private readonly string _gatewayUrl;

        public CampaignSenderJob(AppDbContext dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _httpClient = new HttpClient();
            _gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
        }

        public async Task StartCampaignAsync(Guid campaignId)
        {
            // We disable global tenant filter temporarily to access this campaign if running outside a tenant controller context
            var campaign = await _dbContext.Campaigns
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == campaignId);

            if (campaign == null || campaign.Status == CampaignStatus.Completed || campaign.Status == CampaignStatus.Cancelled)
            {
                return;
            }

            campaign.Status = CampaignStatus.Running;
            campaign.StartedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var segment = await _dbContext.Segments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == campaign.SegmentId);

            if (segment == null)
            {
                campaign.Status = CampaignStatus.Completed;
                campaign.CompletedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return;
            }

            // Parse filter criteria
            string filterCity = null;
            int? leadScoreMin = null;
            string[] filterTags = null;

            try
            {
                if (!string.IsNullOrEmpty(segment.FilterCriteriaJson))
                {
                    using var doc = JsonDocument.Parse(segment.FilterCriteriaJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("city", out var cityProp))
                    {
                        filterCity = cityProp.GetString();
                    }
                    if (root.TryGetProperty("leadScoreMin", out var scoreProp))
                    {
                        leadScoreMin = scoreProp.GetInt32();
                    }
                    if (root.TryGetProperty("tags", out var tagsProp))
                    {
                        filterTags = tagsProp.EnumerateArray().Select(x => x.GetString()).ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing filter JSON for segment {segment.Id}: {ex.Message}");
            }

            // Query matching customers for the project
            var query = _dbContext.Customers
                .IgnoreQueryFilters()
                .Where(c => c.ProjectId == campaign.ProjectId);

            if (!string.IsNullOrEmpty(filterCity))
            {
                query = query.Where(c => c.City == filterCity);
            }
            if (leadScoreMin.HasValue)
            {
                query = query.Where(c => c.LeadScore >= leadScoreMin.Value);
            }

            var matchingCustomers = await query.ToListAsync();

            // Client-side array filtering for tags if specified (EF Core might not fully translate array intersections depending on Postgres provider settings)
            if (filterTags != null && filterTags.Length > 0)
            {
                matchingCustomers = matchingCustomers
                    .Where(c => c.Tags != null && c.Tags.Intersect(filterTags).Any())
                    .ToList();
            }

            if (!matchingCustomers.Any())
            {
                campaign.Status = CampaignStatus.Completed;
                campaign.CompletedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return;
            }

            var random = new Random();
            int index = 0;

            foreach (var customer in matchingCustomers)
            {
                // Deterministic split: hash customer ID + campaign ID
                var assignedVariant = (customer.Id.GetHashCode() + campaign.Id.GetHashCode()) % 2 == 0 ? "A" : "B";
                
                // Select template text
                var template = assignedVariant == "A" 
                    ? campaign.MessageTemplateA 
                    : (campaign.MessageTemplateB ?? campaign.MessageTemplateA);

                // Replace placeholders
                var personalizedMessage = template.Replace("{{CustomerName}}", customer.Name);

                // Create recipient track record
                var recipient = new CampaignRecipient
                {
                    Id = Guid.NewGuid(),
                    ProjectId = campaign.ProjectId,
                    CampaignId = campaign.Id,
                    CustomerId = customer.Id,
                    Variant = assignedVariant,
                    Status = RecipientStatus.Pending
                };

                _dbContext.CampaignRecipients.Add(recipient);

                // Stagger message dispatch using Hangfire delayed execution (e.g. random interval between 5 to 15 seconds per contact)
                int delaySeconds = (index + 1) * random.Next(5, 15);
                
                // Schedule the Hangfire job
                BackgroundJob.Schedule<CampaignSenderJob>(job => job.SendSingleMessageAsync(recipient.Id, personalizedMessage), TimeSpan.FromSeconds(delaySeconds));

                index++;
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task SendSingleMessageAsync(Guid recipientId, string messageText)
        {
            var recipient = await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == recipientId);

            if (recipient == null || recipient.Status != RecipientStatus.Pending)
            {
                return;
            }

            var customer = await _dbContext.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == recipient.CustomerId);

            if (customer == null)
            {
                recipient.Status = RecipientStatus.Failed;
                recipient.ErrorMessage = "Customer not found.";
                await _dbContext.SaveChangesAsync();
                return;
            }

            var payload = new
            {
                projectId = recipient.ProjectId,
                to = customer.PhoneNumber,
                message = messageText
            };

            var jsonPayload = JsonSerializer.Serialize(payload);

            try
            {
                var response = await Shared.Infrastructure.GatewayRetryHelper.PostWithRetryAsync(_httpClient, $"{_gatewayUrl}/api/whatsapp/send", jsonPayload);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    recipient.Status = RecipientStatus.Sent;
                    recipient.SentAt = DateTime.UtcNow;
                    recipient.DeliveredAt = DateTime.UtcNow; // Assume delivered for simple tracking
                    recipient.DeliveredAt = DateTime.UtcNow;

                    // Update Campaign aggregate counts
                    var campaign = await _dbContext.Campaigns
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.Id == recipient.CampaignId);
                    if (campaign != null)
                    {
                        campaign.SentCount++;
                        campaign.DeliveredCount++;

                        // Check if this was the last recipient to complete the campaign status
                        var totalRecipients = await _dbContext.CampaignRecipients
                            .IgnoreQueryFilters()
                            .CountAsync(r => r.CampaignId == campaign.Id);
                        
                        var processedRecipients = await _dbContext.CampaignRecipients
                            .IgnoreQueryFilters()
                            .CountAsync(r => r.CampaignId == campaign.Id && r.Status != RecipientStatus.Pending);

                        if (processedRecipients >= totalRecipients)
                        {
                            campaign.Status = CampaignStatus.Completed;
                            campaign.CompletedAt = DateTime.UtcNow;
                        }
                    }
                }
                else
                {
                    recipient.Status = RecipientStatus.Failed;
                    recipient.ErrorMessage = $"Gateway error {response.StatusCode}: {responseBody}";
                }
            }
            catch (Exception ex)
            {
                recipient.Status = RecipientStatus.Failed;
                recipient.ErrorMessage = $"Exception calling gateway: {ex.Message}";
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}
