using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Modules.Customers.Domain;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Shared.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.Customers.Services
{
    public interface ICustomerMemoryService
    {
        Task UpdateMemoryFromConversationAsync(Guid projectId, Guid customerId, Guid conversationId);
        Task<CustomerMemory> GenerateCompleteProfileAsync(Guid projectId, Guid customerId);
    }

    public class CustomerMemoryService : ICustomerMemoryService
    {
        private readonly AppDbContext _dbContext;
        private readonly IGeminiClient _geminiClient;

        public CustomerMemoryService(AppDbContext dbContext, IGeminiClient geminiClient)
        {
            _dbContext = dbContext;
            _geminiClient = geminiClient;
        }

        public async Task UpdateMemoryFromConversationAsync(Guid projectId, Guid customerId, Guid conversationId)
        {
            // Fetch messages in this conversation
            var messages = await _dbContext.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            if (!messages.Any()) return;

            var transcript = string.Join("\n", messages.Select(m => $"{m.Direction}: {m.Content}"));

            var prompt = $@"Analyze the following WhatsApp conversation between an Agent/AI and a Customer.
Extract any notable facts (e.g. preferences, family info, business size), buying triggers, and customer objections mentioned.
Also write a narrative summary of the relationship so far.
Return the output ONLY as a JSON object of this structure:
{{
  ""facts"": [""fact1"", ""fact2""],
  ""triggers"": [""trigger1""],
  ""objections"": [""objection1""],
  ""summary"": ""A brief narrative summary...""
}}

Conversation Transcript:
{transcript}";

            string response;
            try
            {
                response = await _geminiClient.GenerateReplyAsync(prompt);
            }
            catch (Exception ex)
            {
                response = $"Error: {ex.Message}";
            }

            List<string> facts = new();
            List<string> triggers = new();
            List<string> objections = new();
            string summary = string.Empty;

            bool parsed = false;
            if (!string.IsNullOrEmpty(response) && !response.StartsWith("[Mock") && !response.StartsWith("Error"))
            {
                try
                {
                    // Clean possible markdown backticks
                    var cleanResponse = response.Trim();
                    if (cleanResponse.StartsWith("```json"))
                    {
                        cleanResponse = cleanResponse.Substring(7).Trim();
                    }
                    if (cleanResponse.EndsWith("```"))
                    {
                        cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3).Trim();
                    }

                    var result = JsonSerializer.Deserialize<MemoryExtractionResult>(cleanResponse, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (result != null)
                    {
                        facts = result.Facts ?? facts;
                        triggers = result.Triggers ?? triggers;
                        objections = result.Objections ?? objections;
                        summary = result.Summary ?? summary;
                        parsed = true;
                    }
                }
                catch
                {
                    // Fail gracefully to fallback parsing
                }
            }

            if (!parsed)
            {
                // Fallback analysis by keyword search
                summary = $"Automated summary of conversation {conversationId}. Customer exchanged messages.";
                foreach (var msg in messages.Where(m => m.Direction == "Incoming"))
                {
                    var text = msg.Content.ToLower();
                    if (text.Contains("email"))
                    {
                        facts.Add("Prefers contact via email");
                    }
                    if (text.Contains("phone") || text.Contains("call"))
                    {
                        facts.Add("Prefers contact via phone call");
                    }
                    if (text.Contains("expensive") || text.Contains("price") || text.Contains("cost"))
                    {
                        objections.Add("Price sensitive / Objections about cost");
                    }
                    if (text.Contains("urgent") || text.Contains("need") || text.Contains("buy"))
                    {
                        triggers.Add("Urgent need / purchase intent");
                    }
                }

                // Default if empty
                if (!facts.Any()) facts.Add("Interacted via WhatsApp");
            }

            // Find or create CustomerMemory record
            var memory = await _dbContext.CustomerMemories
                .FirstOrDefaultAsync(m => m.CustomerId == customerId);

            if (memory == null)
            {
                memory = new CustomerMemory
                {
                    ProjectId = projectId,
                    CustomerId = customerId,
                    FactsJson = JsonSerializer.Serialize(facts),
                    TriggersJson = JsonSerializer.Serialize(triggers),
                    ObjectionsJson = JsonSerializer.Serialize(objections),
                    LongTermSummary = summary,
                    LastUpdatedAt = DateTime.UtcNow
                };
                _dbContext.CustomerMemories.Add(memory);
            }
            else
            {
                // Merge/Update
                var existingFacts = JsonSerializer.Deserialize<List<string>>(memory.FactsJson) ?? new();
                var existingTriggers = JsonSerializer.Deserialize<List<string>>(memory.TriggersJson) ?? new();
                var existingObjections = JsonSerializer.Deserialize<List<string>>(memory.ObjectionsJson) ?? new();

                foreach (var f in facts) if (!existingFacts.Contains(f)) existingFacts.Add(f);
                foreach (var t in triggers) if (!existingTriggers.Contains(t)) existingTriggers.Add(t);
                foreach (var o in objections) if (!existingObjections.Contains(o)) existingObjections.Add(o);

                memory.FactsJson = JsonSerializer.Serialize(existingFacts);
                memory.TriggersJson = JsonSerializer.Serialize(existingTriggers);
                memory.ObjectionsJson = JsonSerializer.Serialize(existingObjections);
                memory.LongTermSummary = string.IsNullOrEmpty(memory.LongTermSummary) 
                    ? summary 
                    : $"{memory.LongTermSummary}\n{summary}";
                memory.LastUpdatedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task<CustomerMemory> GenerateCompleteProfileAsync(Guid projectId, Guid customerId)
        {
            // 1. Fetch conversation IDs for the given customer and project
            var conversationIds = await _dbContext.Conversations
                .Where(c => c.CustomerId == customerId && c.ProjectId == projectId)
                .Select(c => c.Id)
                .ToListAsync();

            if (!conversationIds.Any())
            {
                throw new ArgumentException("لا توجد رسائل سابقة لهذا العميل لتوليد ملف التعريف.");
            }

            // 2. Fetch all messages in these conversations ordered by Timestamp
            var messages = await _dbContext.Messages
                .Where(m => conversationIds.Contains(m.ConversationId))
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            if (!messages.Any())
            {
                throw new ArgumentException("لا توجد رسائل سابقة لهذا العميل لتوليد ملف التعريف.");
            }

            // 3. Fetch existing customer labels to restrict options
            var existingLabels = await _dbContext.Customers
                .Where(c => c.ProjectId == projectId && c.Label != null && c.Label != "")
                .Select(c => c.Label)
                .Distinct()
                .ToListAsync();

            string labelsPrompt = "";

            var transcript = string.Join("\n", messages.Select(m => $"{m.Direction}: {m.Content}"));

            var prompt = $@"Analyze the following WhatsApp conversation between an Agent/AI and a Customer.
Extract any notable facts (e.g. preferences, family info, business size), buying triggers, and customer objections mentioned.
Also write a narrative summary of the relationship so far.
Additionally, try to extract the customer's real name (only if mentioned, do not guess), city/location, budget amount (as a number if mentioned), lead score (estimate 0-100 based on interest/intent), pipeline stage (one of: ""New"", ""Contacted"", ""Proposal"", ""Negotiation"", ""Won"", ""Lost""), and a short Arabic classification label (e.g. ""طلب حجز"", ""استفسار عن السعر"", ""متابعة"", ""غير مهتم"").{labelsPrompt}

Return the output ONLY as a JSON object of this structure:
{{
  ""facts"": [""fact1"", ""fact2""],
  ""triggers"": [""trigger1""],
  ""objections"": [""objection1""],
  ""summary"": ""A brief narrative summary..."",
  ""name"": ""extracted name or null"",
  ""city"": ""extracted city or null"",
  ""budget"": 1500,
  ""leadScore"": 80,
  ""pipelineStage"": ""Proposal"",
  ""label"": ""Arabic label""
}}

Conversation Transcript:
{transcript}";

            string response;
            try
            {
                response = await _geminiClient.GenerateReplyAsync(prompt);
            }
            catch (Exception ex)
            {
                response = $"Error: {ex.Message}";
            }

            List<string> facts = new();
            List<string> triggers = new();
            List<string> objections = new();
            string summary = string.Empty;
            MemoryExtractionResult result = null;

            bool parsed = false;
            if (!string.IsNullOrEmpty(response) && !response.StartsWith("[Mock") && !response.StartsWith("Error"))
            {
                try
                {
                    // Clean possible markdown backticks
                    var cleanResponse = response.Trim();
                    if (cleanResponse.StartsWith("```json"))
                    {
                        cleanResponse = cleanResponse.Substring(7).Trim();
                    }
                    if (cleanResponse.EndsWith("```"))
                    {
                        cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3).Trim();
                    }

                    result = JsonSerializer.Deserialize<MemoryExtractionResult>(cleanResponse, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (result != null)
                    {
                        facts = result.Facts ?? facts;
                        triggers = result.Triggers ?? triggers;
                        objections = result.Objections ?? objections;
                        summary = result.Summary ?? summary;
                        parsed = true;
                    }
                }
                catch
                {
                    // Fail gracefully to fallback parsing
                }
            }

            if (!parsed)
            {
                // Fallback analysis by keyword search
                summary = $"Automated summary of conversation history. Customer exchanged messages.";
                foreach (var msg in messages.Where(m => m.Direction == "Incoming"))
                {
                    var text = msg.Content.ToLower();
                    if (text.Contains("email"))
                    {
                        facts.Add("Prefers contact via email");
                    }
                    if (text.Contains("phone") || text.Contains("call"))
                    {
                        facts.Add("Prefers contact via phone call");
                    }
                    if (text.Contains("expensive") || text.Contains("price") || text.Contains("cost"))
                    {
                        objections.Add("Price sensitive / Objections about cost");
                    }
                    if (text.Contains("urgent") || text.Contains("need") || text.Contains("buy"))
                    {
                        triggers.Add("Urgent need / purchase intent");
                    }
                }

                // Default if empty
                if (!facts.Any()) facts.Add("Interacted via WhatsApp");
            }

            // Find or create CustomerMemory record
            var memory = await _dbContext.CustomerMemories
                .FirstOrDefaultAsync(m => m.CustomerId == customerId);

            if (memory == null)
            {
                memory = new CustomerMemory
                {
                    ProjectId = projectId,
                    CustomerId = customerId,
                    FactsJson = JsonSerializer.Serialize(facts),
                    TriggersJson = JsonSerializer.Serialize(triggers),
                    ObjectionsJson = JsonSerializer.Serialize(objections),
                    LongTermSummary = summary,
                    LastUpdatedAt = DateTime.UtcNow
                };
                _dbContext.CustomerMemories.Add(memory);
            }
            else
            {
                // Overwrite with the newly generated comprehensive profile
                memory.FactsJson = JsonSerializer.Serialize(facts);
                memory.TriggersJson = JsonSerializer.Serialize(triggers);
                memory.ObjectionsJson = JsonSerializer.Serialize(objections);
                memory.LongTermSummary = summary;
                memory.LastUpdatedAt = DateTime.UtcNow;
            }

            // Update Customer fields if successfully parsed
            if (parsed && result != null)
            {
                var customer = await _dbContext.Customers.FindAsync(customerId);
                if (customer != null)
                {
                    if (!string.IsNullOrEmpty(result.Name) && 
                        !result.Name.Equals(customer.PhoneNumber) && 
                        !result.Name.Contains("Customer", StringComparison.OrdinalIgnoreCase) && 
                        !result.Name.Contains("عميل", StringComparison.OrdinalIgnoreCase) &&
                        !result.Name.Contains("extracted name", StringComparison.OrdinalIgnoreCase))
                    {
                        customer.Name = result.Name;
                    }
                    if (!string.IsNullOrEmpty(result.City) && 
                        !result.City.Contains("extracted city", StringComparison.OrdinalIgnoreCase))
                    {
                        customer.City = result.City;
                    }
                    if (result.Budget.HasValue && result.Budget.Value > 0)
                    {
                        customer.Budget = result.Budget.Value;
                    }
                    if (result.LeadScore.HasValue)
                    {
                        customer.LeadScore = Math.Min(100, Math.Max(0, result.LeadScore.Value));
                    }
                    if (!string.IsNullOrEmpty(result.Label) && 
                        !result.Label.Contains("label", StringComparison.OrdinalIgnoreCase))
                    {
                        customer.Label = result.Label;
                    }

                    // Update or Create Deal if pipelineStage is returned
                    if (!string.IsNullOrEmpty(result.PipelineStage) && 
                        !result.PipelineStage.Contains("stage", StringComparison.OrdinalIgnoreCase))
                    {
                        var stage = await _dbContext.PipelineStages
                            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Name.ToLower() == result.PipelineStage.ToLower());

                        if (stage == null)
                        {
                            var orders = await _dbContext.PipelineStages
                                .Where(s => s.ProjectId == projectId)
                                .Select(s => s.Order)
                                .ToListAsync();
                            int maxOrder = orders.Any() ? orders.Max() : -1;

                            stage = new PipelineStage
                            {
                                Id = Guid.NewGuid(),
                                ProjectId = projectId,
                                Name = result.PipelineStage,
                                Order = maxOrder + 1
                            };
                            _dbContext.PipelineStages.Add(stage);
                            await _dbContext.SaveChangesAsync();
                        }

                        var activeDeal = await _dbContext.Deals
                            .FirstOrDefaultAsync(d => d.CustomerId == customerId && d.Status == DealStatus.Open);

                        if (activeDeal != null)
                        {
                            activeDeal.PipelineStageId = stage.Id;
                            if (result.Budget.HasValue)
                            {
                                activeDeal.Amount = result.Budget.Value;
                            }
                            if (stage.Name.Equals("Won", StringComparison.OrdinalIgnoreCase))
                            {
                                activeDeal.Status = DealStatus.Won;
                                activeDeal.ClosedAt = DateTime.UtcNow;
                            }
                            else if (stage.Name.Equals("Lost", StringComparison.OrdinalIgnoreCase))
                            {
                                activeDeal.Status = DealStatus.Lost;
                                activeDeal.ClosedAt = DateTime.UtcNow;
                            }
                            _dbContext.Entry(activeDeal).State = EntityState.Modified;
                        }
                        else
                        {
                            var status = DealStatus.Open;
                            DateTime? closedAt = null;
                            if (stage.Name.Equals("Won", StringComparison.OrdinalIgnoreCase))
                            {
                                status = DealStatus.Won;
                                closedAt = DateTime.UtcNow;
                            }
                            else if (stage.Name.Equals("Lost", StringComparison.OrdinalIgnoreCase))
                            {
                                status = DealStatus.Lost;
                                closedAt = DateTime.UtcNow;
                            }

                            var deal = new Deal
                            {
                                Id = Guid.NewGuid(),
                                ProjectId = projectId,
                                CustomerId = customerId,
                                Title = $"{customer.Name}'s Deal",
                                Amount = customer.Budget ?? 0,
                                PipelineStageId = stage.Id,
                                Status = status,
                                ClosedAt = closedAt
                            };
                            _dbContext.Deals.Add(deal);
                        }
                    }

                    _dbContext.Entry(customer).State = EntityState.Modified;
                }
            }

            await _dbContext.SaveChangesAsync();
            return memory;
        }
    }

    public class MemoryExtractionResult
    {
        public List<string>? Facts { get; set; }
        public List<string>? Triggers { get; set; }
        public List<string>? Objections { get; set; }
        public string? Summary { get; set; }
        public string? Name { get; set; }
        public string? City { get; set; }
        public decimal? Budget { get; set; }
        public int? LeadScore { get; set; }
        public string? PipelineStage { get; set; }
        public string? Label { get; set; }
    }
}
