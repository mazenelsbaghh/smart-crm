using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Modules.Customers.Domain;
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
    }

    public class MemoryExtractionResult
    {
        public List<string>? Facts { get; set; }
        public List<string>? Triggers { get; set; }
        public List<string>? Objections { get; set; }
        public string? Summary { get; set; }
    }
}
