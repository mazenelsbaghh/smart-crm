using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shared.Infrastructure;
using Modules.Campaigns.Domain;
using Modules.Campaigns.Application.Services;
using Modules.Conversations.Domain;
using Modules.Advertising.Services;
using Modules.WhatsApp.Services;
using Hangfire;

namespace Modules.Campaigns.Jobs
{
    public class CampaignSenderJob
    {
        private readonly AppDbContext _dbContext;
        private readonly HttpClient _httpClient;
        private readonly string _gatewayUrl;
        private readonly CampaignDispatchLimits _dispatchLimits;
        private readonly ICampaignAIService _campaignAIService;
        private readonly WhatsAppGatewaySessionClient _gatewaySessionClient;
        private readonly IBackgroundJobClient _backgroundJobs;

        public CampaignSenderJob(
            AppDbContext dbContext,
            IConfiguration configuration,
            ICampaignAIService campaignAIService,
            WhatsAppGatewaySessionClient gatewaySessionClient,
            IHttpClientFactory httpClientFactory,
            IBackgroundJobClient backgroundJobs)
        {
            _dbContext = dbContext;
            _httpClient = httpClientFactory.CreateClient();
            _gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            _dispatchLimits = LoadDispatchLimits(configuration);
            _campaignAIService = campaignAIService;
            _gatewaySessionClient = gatewaySessionClient;
            _backgroundJobs = backgroundJobs;
        }

        public async Task StartCampaignAsync(Guid campaignId)
        {
            List<(Guid Id, RecipientStatus Status)> recipientsToSchedule;
            if (_dbContext.Database.IsRelational())
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                if (!await TryClaimCampaignPreparationAsync(campaignId))
                {
                    return;
                }

                recipientsToSchedule = await PrepareCampaignAsync(campaignId);
                await transaction.CommitAsync();
            }
            else
            {
                if (!await TryClaimCampaignPreparationAsync(campaignId))
                {
                    return;
                }

                recipientsToSchedule = await PrepareCampaignAsync(campaignId);
            }

            for (var index = 0; index < recipientsToSchedule.Count; index++)
            {
                var recipient = recipientsToSchedule[index];
                var sendDelay = CampaignDispatchSchedule.DelayFor(index, _dispatchLimits, Random.Shared.NextDouble());
                if (recipient.Status == RecipientStatus.Accelerated)
                {
                    _backgroundJobs.Schedule<CampaignSenderJob>(job => job.SendAcceleratedMessageAsync(recipient.Id), sendDelay);
                }
                else
                {
                    _backgroundJobs.Schedule<CampaignSenderJob>(job => job.SendSingleMessageAsync(recipient.Id), sendDelay);
                }
            }
        }

        private async Task<bool> TryClaimCampaignPreparationAsync(Guid campaignId)
        {
            if (_dbContext.Database.IsRelational())
            {
                var claimed = await _dbContext.Campaigns
                    .IgnoreQueryFilters()
                    .Where(campaign => campaign.Id == campaignId
                        && (campaign.Status == CampaignStatus.Draft
                            || campaign.Status == CampaignStatus.Scheduled
                            || (campaign.Status == CampaignStatus.Running
                                && (!_dbContext.CampaignRecipients
                                        .IgnoreQueryFilters()
                                        .Any(recipient => recipient.CampaignId == campaign.Id)
                                    || _dbContext.CampaignRecipients
                                        .IgnoreQueryFilters()
                                        .Any(recipient => recipient.CampaignId == campaign.Id
                                            && (recipient.Status == RecipientStatus.Pending
                                                || recipient.Status == RecipientStatus.Accelerated))))))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(campaign => campaign.Status, CampaignStatus.Preparing)
                        .SetProperty(campaign => campaign.UpdatedAt, DateTime.UtcNow));
                if (claimed == 1)
                {
                    _dbContext.ChangeTracker.Clear();
                }

                return claimed == 1;
            }

            var campaign = await _dbContext.Campaigns
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == campaignId);
            var hasRecipients = campaign != null && await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .AnyAsync(recipient => recipient.CampaignId == campaign.Id);
            var hasSchedulableRecipients = campaign != null && await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .AnyAsync(recipient => recipient.CampaignId == campaign.Id
                    && (recipient.Status == RecipientStatus.Pending
                        || recipient.Status == RecipientStatus.Accelerated));
            if (campaign == null
                || (campaign.Status != CampaignStatus.Draft
                    && campaign.Status != CampaignStatus.Scheduled
                    && (campaign.Status != CampaignStatus.Running
                        || (hasRecipients && !hasSchedulableRecipients))))
            {
                return false;
            }

            campaign.Status = CampaignStatus.Preparing;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        private async Task<List<(Guid Id, RecipientStatus Status)>> PrepareCampaignAsync(Guid campaignId)
        {
            var campaign = await _dbContext.Campaigns
                .IgnoreQueryFilters()
                .FirstAsync(candidate => candidate.Id == campaignId);

            var existingRecipients = await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .Where(recipient => recipient.CampaignId == campaign.Id)
                .OrderBy(recipient => recipient.CreatedAt)
                .ThenBy(recipient => recipient.Id)
                .Select(recipient => new { recipient.Id, recipient.Status })
                .ToListAsync();
            if (existingRecipients.Count > 0)
            {
                campaign.StartedAt ??= DateTime.UtcNow;
                var schedulableRecipients = existingRecipients
                    .Where(recipient => recipient.Status == RecipientStatus.Pending
                        || recipient.Status == RecipientStatus.Accelerated)
                    .ToList();
                campaign.Status = schedulableRecipients.Count > 0
                        || existingRecipients.Any(recipient => recipient.Status == RecipientStatus.Processing)
                    ? CampaignStatus.Running
                    : CampaignStatus.Completed;
                if (campaign.Status == CampaignStatus.Completed)
                {
                    campaign.CompletedAt ??= DateTime.UtcNow;
                }

                await _dbContext.SaveChangesAsync();
                return schedulableRecipients.Select(recipient => (recipient.Id, recipient.Status)).ToList();
            }

            var segment = await _dbContext.Segments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == campaign.SegmentId);

            if (segment == null)
            {
                campaign.Status = CampaignStatus.Completed;
                campaign.CompletedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return [];
            }

            // Parse filter criteria
            string filterCity = null;
            int? leadScoreMin = null;
            string[] filterTags = null;
            int? inactiveDays = null;

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
                    if (root.TryGetProperty("inactiveDays", out var inactiveDaysProp))
                    {
                        inactiveDays = inactiveDaysProp.GetInt32();
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
                .Where(c => c.ProjectId == campaign.ProjectId && !c.IsBlacklisted && c.PhoneNumber != null && c.PhoneNumber != "");

            if (inactiveDays.HasValue)
            {
                var inactiveBefore = DateTime.UtcNow.AddDays(-inactiveDays.Value);
                query = query.Where(customer => _dbContext.Conversations
                    .IgnoreQueryFilters()
                    .Where(conversation => conversation.ProjectId == campaign.ProjectId && conversation.CustomerId == customer.Id)
                    .Any(conversation => conversation.LastMessageTimestamp < inactiveBefore)
                    && !_dbContext.Conversations
                        .IgnoreQueryFilters()
                        .Any(conversation => conversation.ProjectId == campaign.ProjectId
                            && conversation.CustomerId == customer.Id
                            && conversation.LastMessageTimestamp >= inactiveBefore));
            }

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
                return [];
            }

            var createdRecipients = new List<CampaignRecipient>(matchingCustomers.Count);

            foreach (var customer in matchingCustomers)
            {
                // Deterministic split: hash customer ID + campaign ID
                var assignedVariant = (customer.Id.GetHashCode() + campaign.Id.GetHashCode()) % 2 == 0 ? "A" : "B";
                
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
                createdRecipients.Add(recipient);
            }

            campaign.Status = CampaignStatus.Running;
            campaign.StartedAt ??= DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return createdRecipients.Select(recipient => (recipient.Id, recipient.Status)).ToList();
        }

        public async Task SendSingleMessageAsync(Guid recipientId)
        {
            await SendMessageAsync(recipientId, RecipientStatus.Pending);
        }

        public async Task SendAcceleratedMessageAsync(Guid recipientId)
        {
            await SendMessageAsync(recipientId, RecipientStatus.Accelerated);
        }

        private async Task SendMessageAsync(Guid recipientId, RecipientStatus expectedStatus)
        {
            var recipient = await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == recipientId);
            if (recipient == null || recipient.Status != expectedStatus)
            {
                return;
            }

            var customer = await _dbContext.Customers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == recipient.CustomerId);

            var campaign = await _dbContext.Campaigns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == recipient.CampaignId);

            if (campaign?.Status != CampaignStatus.Running)
            {
                return;
            }

            if (customer == null || customer.IsBlacklisted)
            {
                await ExecuteClaimedWorkAsync(
                    recipientId,
                    expectedStatus,
                    requiredWhatsAppAccountId: null,
                    (claimedRecipient, claimedCampaign) => RecordTerminalOutcomeAsync(
                        claimedRecipient,
                        claimedCampaign.Id,
                        RecipientStatus.Failed,
                        customer == null ? "Customer not found." : "Customer opted out."));
                return;
            }

            var whatsAppAccountId = campaign.WhatsAppAccountId ?? recipient.ProjectId;
            var gatewaySession = await _gatewaySessionClient.GetAsync(
                recipient.ProjectId,
                whatsAppAccountId);
            if (!gatewaySession.Connected || gatewaySession.ConnectedAt == null)
            {
                await PauseCampaignAsync(campaign.Id);
                Console.WriteLine($"[CampaignSenderJob] WhatsApp is disconnected or has no connection epoch for project {recipient.ProjectId}. Pausing campaign {campaign.Id} without generating AI copy or sending.");
                return;
            }

            var message = await PersonalizedMessageAsync(campaign, customer, recipient.Variant);

            await ExecuteClaimedWorkAsync(
                recipientId,
                expectedStatus,
                whatsAppAccountId,
                async (claimedRecipient, claimedCampaign) =>
                {
                    var currentCustomer = await _dbContext.Customers
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(candidate => candidate.Id == claimedRecipient.CustomerId);
                    if (currentCustomer == null || currentCustomer.IsBlacklisted)
                    {
                        await RecordTerminalOutcomeAsync(
                            claimedRecipient,
                            claimedCampaign.Id,
                            RecipientStatus.Failed,
                            currentCustomer == null ? "Customer not found." : "Customer opted out.");
                        return;
                    }

                    await SendClaimedMessageAsync(
                        claimedRecipient,
                        claimedCampaign,
                        currentCustomer,
                        message,
                        whatsAppAccountId,
                        gatewaySession.ConnectedAt.Value,
                        expectedStatus);
                });
        }

        private async Task ExecuteClaimedWorkAsync(
            Guid recipientId,
            RecipientStatus expectedStatus,
            Guid? requiredWhatsAppAccountId,
            Func<CampaignRecipient, Campaign, Task> work)
        {
            await using var transaction = _dbContext.Database.IsRelational()
                ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
                : null;
            if (!await TryClaimRecipientAsync(recipientId, expectedStatus))
            {
                return;
            }

            var recipient = await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .FirstAsync(candidate => candidate.Id == recipientId);
            var campaignQuery = _dbContext.Campaigns
                .IgnoreQueryFilters()
                .Where(candidate => candidate.Id == recipient.CampaignId);
            var campaign = _dbContext.Database.IsRelational()
                ? await campaignQuery.AsNoTracking().FirstAsync()
                : await campaignQuery.FirstAsync();
            var currentWhatsAppAccountId = campaign.WhatsAppAccountId ?? recipient.ProjectId;
            if (campaign.Status != CampaignStatus.Running
                || (requiredWhatsAppAccountId.HasValue
                    && currentWhatsAppAccountId != requiredWhatsAppAccountId.Value))
            {
                if (transaction == null)
                {
                    await ReleaseRecipientAsync(recipient, expectedStatus);
                }

                return;
            }

            await work(recipient, campaign);
            if (transaction != null)
            {
                await transaction.CommitAsync();
            }
        }

        private async Task SendClaimedMessageAsync(
            CampaignRecipient recipient,
            Campaign campaign,
            Customer customer,
            string message,
            Guid whatsAppAccountId,
            DateTimeOffset connectedAt,
            RecipientStatus expectedStatus)
        {
            var payload = new
            {
                projectId = recipient.ProjectId,
                whatsappAccountId = whatsAppAccountId,
                to = customer.PhoneNumber,
                message,
                idempotencyKey = recipient.Id.ToString("N"),
                expectedConnectedAt = connectedAt
            };

            var jsonPayload = JsonSerializer.Serialize(payload);

            try
            {
                using var response = await Shared.Infrastructure.GatewayRetryHelper.PostOnceAsync(_httpClient, $"{_gatewayUrl}/api/whatsapp/send", jsonPayload);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var providerMessageId = ProviderMessageId(responseBody);
                    if (string.IsNullOrWhiteSpace(providerMessageId))
                    {
                        await MarkConversationDeliveryUnknownAsync(
                            recipient.ProjectId,
                            recipient.CustomerId,
                            whatsAppAccountId,
                            recipient.Id.ToString("N"));
                        await RecordTerminalOutcomeAsync(
                            recipient,
                            campaign.Id,
                            RecipientStatus.DeliveryUnknown,
                            "Gateway reported success without a provider message id.");
                        return;
                    }
                    await RecordSuccessfulDeliveryAsync(
                        recipient,
                        campaign,
                        customer,
                        message,
                        whatsAppAccountId,
                        providerMessageId);
                }
                else
                {
                    var statusCode = (int)response.StatusCode;
                    if (statusCode is 412 or 503)
                    {
                        recipient.ErrorMessage = $"Gateway deferred delivery {statusCode}: {responseBody}";
                        await ReleaseRecipientAsync(recipient, expectedStatus);
                        await PauseCampaignAsync(campaign.Id);
                        return;
                    }

                    var outcome = statusCode == 409 || statusCode >= 500
                        ? RecipientStatus.DeliveryUnknown
                        : RecipientStatus.Failed;
                    if (outcome == RecipientStatus.DeliveryUnknown)
                    {
                        await MarkConversationDeliveryUnknownAsync(
                            recipient.ProjectId,
                            recipient.CustomerId,
                            whatsAppAccountId,
                            recipient.Id.ToString("N"));
                    }
                    await RecordTerminalOutcomeAsync(
                        recipient,
                        campaign.Id,
                        outcome,
                        $"Gateway error {statusCode}: {responseBody}");
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.IO.IOException)
            {
                await MarkConversationDeliveryUnknownAsync(
                    recipient.ProjectId,
                    recipient.CustomerId,
                    whatsAppAccountId,
                    recipient.Id.ToString("N"));
                await RecordTerminalOutcomeAsync(
                    recipient,
                    campaign.Id,
                    RecipientStatus.DeliveryUnknown,
                    $"Gateway delivery outcome is unknown: {exception.Message}");
            }
        }

        private async Task<bool> TryClaimRecipientAsync(Guid recipientId, RecipientStatus expectedStatus)
        {
            if (_dbContext.Database.IsRelational())
            {
                var claimed = await _dbContext.CampaignRecipients
                    .IgnoreQueryFilters()
                    .Where(recipient => recipient.Id == recipientId && recipient.Status == expectedStatus)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(recipient => recipient.Status, RecipientStatus.Processing)
                        .SetProperty(recipient => recipient.ErrorMessage, string.Empty)
                        .SetProperty(recipient => recipient.UpdatedAt, DateTime.UtcNow));
                if (claimed == 1)
                {
                    _dbContext.ChangeTracker.Clear();
                }

                return claimed == 1;
            }

            var recipient = await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == recipientId);
            if (recipient == null || recipient.Status != expectedStatus)
            {
                return false;
            }

            recipient.Status = RecipientStatus.Processing;
            recipient.ErrorMessage = string.Empty;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        private async Task ReleaseRecipientAsync(CampaignRecipient recipient, RecipientStatus status)
        {
            recipient.Status = status;
            await _dbContext.SaveChangesAsync();
        }

        private async Task PauseCampaignAsync(Guid campaignId)
        {
            if (_dbContext.Database.IsRelational())
            {
                await _dbContext.Campaigns
                    .IgnoreQueryFilters()
                    .Where(campaign => campaign.Id == campaignId && campaign.Status == CampaignStatus.Running)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(campaign => campaign.Status, CampaignStatus.Paused)
                        .SetProperty(campaign => campaign.UpdatedAt, DateTime.UtcNow));
                return;
            }

            var campaign = await _dbContext.Campaigns
                .IgnoreQueryFilters()
                .FirstAsync(candidate => candidate.Id == campaignId);
            campaign.Status = CampaignStatus.Paused;
            await _dbContext.SaveChangesAsync();
        }

        private async Task RecordSuccessfulDeliveryAsync(
            CampaignRecipient recipient,
            Campaign campaign,
            Customer customer,
            string content,
            Guid whatsAppAccountId,
            string providerMessageId)
        {
            var deliveredAt = DateTime.UtcNow;
            var conversation = await new WhatsAppConversationService(_dbContext)
                .ResolveOrCreateAsync(
                    recipient.ProjectId,
                    customer.Id,
                    whatsAppAccountId,
                    deliveredAt);
            if (string.Equals(
                    conversation.WhatsAppDeliveryUnknownKey,
                    recipient.Id.ToString("N"),
                    StringComparison.Ordinal))
            {
                conversation.WhatsAppDeliveryUnknownAt = null;
                conversation.WhatsAppDeliveryUnknownKey = null;
            }
            var messageId = DeterministicMessageId(
                recipient.ProjectId,
                whatsAppAccountId,
                providerMessageId);
            if (!await _dbContext.Messages.IgnoreQueryFilters()
                .AnyAsync(existing => existing.Id == messageId))
            {
                _dbContext.Messages.Add(new Message
                {
                    Id = messageId,
                    ConversationId = conversation.Id,
                    ExternalMessageId = providerMessageId,
                    Direction = "Outgoing",
                    Content = content,
                    MessageType = "Text",
                    Timestamp = deliveredAt
                });
            }
            recipient.Status = RecipientStatus.Sent;
            recipient.SentAt = deliveredAt;
            recipient.DeliveredAt = deliveredAt;
            recipient.ErrorMessage = string.Empty;
            await _dbContext.SaveChangesAsync();

            if (_dbContext.Database.IsRelational())
            {
                await _dbContext.Campaigns
                    .IgnoreQueryFilters()
                    .Where(candidate => candidate.Id == campaign.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.SentCount, candidate => candidate.SentCount + 1)
                        .SetProperty(candidate => candidate.DeliveredCount, candidate => candidate.DeliveredCount + 1)
                        .SetProperty(candidate => candidate.UpdatedAt, DateTime.UtcNow));
            }
            else
            {
                campaign.SentCount++;
                campaign.DeliveredCount++;
                await _dbContext.SaveChangesAsync();
            }

            await CompleteCampaignIfTerminalAsync(campaign.Id);
        }

        private async Task MarkConversationDeliveryUnknownAsync(
            Guid projectId,
            Guid customerId,
            Guid whatsAppAccountId,
            string deliveryKey)
        {
            var conversation = await new WhatsAppConversationService(_dbContext)
                .ResolveOrCreateAsync(
                    projectId,
                    customerId,
                    whatsAppAccountId,
                    DateTime.UtcNow);
            conversation.WhatsAppDeliveryUnknownAt = DateTime.UtcNow;
            conversation.WhatsAppDeliveryUnknownKey = deliveryKey;
            await _dbContext.SaveChangesAsync();
        }

        private static string? ProviderMessageId(string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                return document.RootElement.TryGetProperty("messageId", out var messageId)
                    && messageId.ValueKind == JsonValueKind.String
                    ? messageId.GetString()?.Trim()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static Guid DeterministicMessageId(
            Guid projectId,
            Guid whatsAppAccountId,
            string providerMessageId)
        {
            var value = $"whatsapp-outgoing:{projectId:N}:{whatsAppAccountId:N}:{providerMessageId}";
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value));
            return new Guid(bytes.AsSpan(0, 16));
        }

        private async Task RecordTerminalOutcomeAsync(
            CampaignRecipient recipient,
            Guid campaignId,
            RecipientStatus status,
            string errorMessage)
        {
            recipient.Status = status;
            recipient.ErrorMessage = errorMessage;
            await _dbContext.SaveChangesAsync();
            await CompleteCampaignIfTerminalAsync(campaignId);
        }

        private async Task CompleteCampaignIfTerminalAsync(Guid campaignId)
        {
            var hasUnfinishedRecipients = await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .AnyAsync(recipient => recipient.CampaignId == campaignId
                    && (recipient.Status == RecipientStatus.Pending
                        || recipient.Status == RecipientStatus.Accelerated
                        || recipient.Status == RecipientStatus.Processing));
            if (hasUnfinishedRecipients)
            {
                return;
            }

            var completedAt = DateTime.UtcNow;
            if (_dbContext.Database.IsRelational())
            {
                await _dbContext.Campaigns
                    .IgnoreQueryFilters()
                    .Where(campaign => campaign.Id == campaignId && campaign.Status == CampaignStatus.Running)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(campaign => campaign.Status, CampaignStatus.Completed)
                        .SetProperty(campaign => campaign.CompletedAt, completedAt)
                        .SetProperty(campaign => campaign.UpdatedAt, completedAt));
                return;
            }

            var campaign = await _dbContext.Campaigns
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == campaignId);
            if (campaign?.Status == CampaignStatus.Running)
            {
                campaign.Status = CampaignStatus.Completed;
                campaign.CompletedAt = completedAt;
                await _dbContext.SaveChangesAsync();
            }
        }

        public async Task ResumeFirstBatchAsync(Guid campaignId)
        {
            if (_dbContext.Database.IsRelational())
            {
                var claimed = await _dbContext.Campaigns
                    .IgnoreQueryFilters()
                    .Where(campaign => campaign.Id == campaignId && campaign.Status == CampaignStatus.Paused)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(campaign => campaign.Status, CampaignStatus.Running)
                        .SetProperty(campaign => campaign.UpdatedAt, DateTime.UtcNow));
                if (claimed != 1)
                {
                    return;
                }

                _dbContext.ChangeTracker.Clear();
            }
            else
            {
                var campaign = await _dbContext.Campaigns
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(candidate => candidate.Id == campaignId);
                if (campaign == null || campaign.Status != CampaignStatus.Paused)
                {
                    return;
                }

                campaign.Status = CampaignStatus.Running;
                await _dbContext.SaveChangesAsync();
            }

            var resumableRecipients = await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .Where(recipient => recipient.CampaignId == campaignId
                    && (recipient.Status == RecipientStatus.Pending
                        || recipient.Status == RecipientStatus.Accelerated))
                .OrderBy(recipient => recipient.CreatedAt)
                .ThenBy(recipient => recipient.Id)
                .Select(recipient => new { recipient.Id, recipient.Status })
                .ToListAsync();

            var pendingIndex = 0;
            var acceleratedIndex = 0;
            foreach (var recipient in resumableRecipients)
            {
                if (recipient.Status == RecipientStatus.Accelerated)
                {
                    var sendDelay = TimeSpan.FromSeconds((acceleratedIndex * 8) + Random.Shared.Next(0, 5));
                    _backgroundJobs.Schedule<CampaignSenderJob>(job => job.SendAcceleratedMessageAsync(recipient.Id), sendDelay);
                    acceleratedIndex++;
                }
                else
                {
                    var sendDelay = CampaignDispatchSchedule.DelayFor(
                        pendingIndex,
                        _dispatchLimits,
                        Random.Shared.NextDouble());
                    _backgroundJobs.Schedule<CampaignSenderJob>(job => job.SendSingleMessageAsync(recipient.Id), sendDelay);
                    pendingIndex++;
                }
            }
        }

        public async Task AccelerateAfterFirstBatchAsync(Guid campaignId)
        {
            var campaignIsRunning = await _dbContext.Campaigns
                .IgnoreQueryFilters()
                .AnyAsync(campaign => campaign.Id == campaignId && campaign.Status == CampaignStatus.Running);
            if (!campaignIsRunning)
            {
                return;
            }

            var recipientIds = await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .Where(recipient => recipient.CampaignId == campaignId && recipient.Status == RecipientStatus.Pending)
                .OrderBy(recipient => recipient.CreatedAt)
                .ThenBy(recipient => recipient.Id)
                .Skip(_dispatchLimits.BatchSize)
                .Select(recipient => recipient.Id)
                .ToListAsync();

            for (var index = 0; index < recipientIds.Count; index++)
            {
                var recipientId = recipientIds[index];
                var sendDelay = TimeSpan.FromSeconds((index * 17) + Random.Shared.Next(0, 5));
                _backgroundJobs.Schedule<CampaignSenderJob>(job => job.SendSingleMessageAsync(recipientId), sendDelay);
            }
        }

        public async Task AccelerateAllPendingAsync(Guid campaignId)
        {
            var campaignIsRunning = await _dbContext.Campaigns
                .IgnoreQueryFilters()
                .AnyAsync(campaign => campaign.Id == campaignId && campaign.Status == CampaignStatus.Running);
            if (!campaignIsRunning)
            {
                return;
            }

            if (_dbContext.Database.IsRelational())
            {
                await _dbContext.CampaignRecipients
                    .IgnoreQueryFilters()
                    .Where(recipient => recipient.CampaignId == campaignId
                        && recipient.Status == RecipientStatus.Pending)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(recipient => recipient.Status, RecipientStatus.Accelerated)
                        .SetProperty(recipient => recipient.UpdatedAt, DateTime.UtcNow));
                _dbContext.ChangeTracker.Clear();
            }
            else
            {
                var pendingRecipients = await _dbContext.CampaignRecipients
                    .IgnoreQueryFilters()
                    .Where(recipient => recipient.CampaignId == campaignId
                        && recipient.Status == RecipientStatus.Pending)
                    .ToListAsync();
                foreach (var recipient in pendingRecipients)
                {
                    recipient.Status = RecipientStatus.Accelerated;
                }

                await _dbContext.SaveChangesAsync();
            }

            var recipientIds = await _dbContext.CampaignRecipients
                .IgnoreQueryFilters()
                .Where(recipient => recipient.CampaignId == campaignId
                    && recipient.Status == RecipientStatus.Accelerated)
                .OrderBy(recipient => recipient.CreatedAt)
                .ThenBy(recipient => recipient.Id)
                .Select(recipient => recipient.Id)
                .ToListAsync();

            for (var index = 0; index < recipientIds.Count; index++)
            {
                var recipientId = recipientIds[index];
                var sendDelay = TimeSpan.FromSeconds((index * 8) + Random.Shared.Next(0, 5));
                _backgroundJobs.Schedule<CampaignSenderJob>(job => job.SendAcceleratedMessageAsync(recipientId), sendDelay);
            }
        }

        private static CampaignDispatchLimits LoadDispatchLimits(IConfiguration configuration)
        {
            var defaults = CampaignDispatchLimits.SafeDefaults;
            return new CampaignDispatchLimits(
                configuration.GetValue("CampaignDispatch:BatchSize", defaults.BatchSize),
                configuration.GetValue("CampaignDispatch:BatchesPerDay", defaults.BatchesPerDay),
                TimeSpan.FromMinutes(configuration.GetValue("CampaignDispatch:BatchGapMinutes", (int)defaults.BatchGap.TotalMinutes)),
                TimeSpan.FromSeconds(configuration.GetValue("CampaignDispatch:MinimumMessageGapSeconds", (int)defaults.MinimumMessageGap.TotalSeconds)),
                TimeSpan.FromSeconds(configuration.GetValue("CampaignDispatch:MessageJitterSeconds", (int)defaults.MessageJitter.TotalSeconds)));
        }

        private async Task<string> PersonalizedMessageAsync(Campaign campaign, Customer customer, string variant)
        {
            var conversationIds = await _dbContext.Conversations
                .IgnoreQueryFilters()
                .Where(conversation => conversation.ProjectId == campaign.ProjectId && conversation.CustomerId == customer.Id)
                .Select(conversation => conversation.Id)
                .ToListAsync();

            var recentMessages = await _dbContext.Messages
                .IgnoreQueryFilters()
                .Where(message => conversationIds.Contains(message.ConversationId))
                .OrderByDescending(message => message.Timestamp)
                .Take(15)
                .OrderBy(message => message.Timestamp)
                .Select(message => $"{message.Direction}: {message.Content}")
                .ToListAsync();

            var chatContext = string.Join("\n", recentMessages);
            var prompt = "استخرج موضوع اهتمام العميل في كلمتين إلى ست كلمات بالمصري. "
                + "اكتب الموضوع فقط بدون اسم العميل أو علامات ترقيم. لو السياق غير واضح اكتب: تفاصيل الكورس";
            var baseTemplate = variant == "B" ? campaign.MessageTemplateB ?? campaign.MessageTemplateA : campaign.MessageTemplateA;
            var generatedTopic = await _campaignAIService.GenerateProjectCampaignCopyAsync(
                campaign.ProjectId,
                prompt,
                baseTemplate,
                chatContext);

            var topic = SafeInterestTopic(generatedTopic);
            return baseTemplate
                .Replace("{{CustomerName}}", customer.Name)
                .Replace("{{InterestTopic}}", topic);
        }

        private static string SafeInterestTopic(string generatedTopic)
        {
            var topic = generatedTopic.Trim().Trim('"', '\'', '[', ']', '{', '}');
            if (topic.StartsWith("[Mock", StringComparison.OrdinalIgnoreCase)
                || topic.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
                || topic.Length is 0 or > 80
                || topic.Contains('\n'))
            {
                return "تفاصيل الكورس";
            }

            return topic;
        }
    }
}
