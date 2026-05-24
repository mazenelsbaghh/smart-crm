using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.AI.Services
{
    public class CRMEntities
    {
        public string City { get; set; }
        public decimal? Budget { get; set; }
        public string[] Interests { get; set; } = Array.Empty<string>();
        public string Timeline { get; set; }
    }

    public class MarketingAnalysisResult
    {
        public string Intent { get; set; } = "inquiry"; // inquiry, complaint, purchase, follow-up, greeting
        public string Sentiment { get; set; } = "neutral"; // positive, neutral, negative, angry
        public string ReplyStyle { get; set; } = "Casual"; // Fast, Casual, Sales, Support, VIP, Complaint, Follow-up
        public CRMEntities Entities { get; set; } = new CRMEntities();
        public string ReplyContent { get; set; } = string.Empty;
        public double Confidence { get; set; } = 1.0;
    }

    public interface IAIMarketingBrain
    {
        Task<MarketingAnalysisResult> AnalyzeAndGenerateReplyAsync(string messageContent, string apiKeyOverride = null);
    }

    public class AIMarketingBrain : IAIMarketingBrain
    {
        private readonly IGeminiClient _geminiClient;

        public AIMarketingBrain(IGeminiClient geminiClient)
        {
            _geminiClient = geminiClient;
        }

        public async Task<MarketingAnalysisResult> AnalyzeAndGenerateReplyAsync(string messageContent, string apiKeyOverride = null)
        {
            var systemPrompt = @"You are a high-performing AI Marketing Brain and CRM assistant.
Analyze the customer's message and generate a response.
You MUST respond strictly in the following JSON format, and nothing else (no markdown blocks like ```json):
{
  ""intent"": ""inquiry | complaint | purchase | follow-up | greeting"",
  ""sentiment"": ""positive | neutral | negative | angry"",
  ""replyStyle"": ""Fast | Casual | Sales | Support | VIP | Complaint | Follow-up"",
  ""entities"": {
    ""city"": ""string | null"",
    ""budget"": number | null,
    ""interests"": [""string""],
    ""timeline"": ""string | null""
  },
  ""replyContent"": ""your human-like helpful reply text here"",
  ""confidence"": 0.95
}

Guidelines for replyStyle:
- Fast: Short, immediate answers.
- Casual: Friendly, informal tone.
- Sales: Persuasive, highlighting benefits, includes a clear CTA.
- Support: Empathetic, helpful.
- VIP: Exclusive, highly polite.
- Complaint: Apologetic, resolution-focused, highly empathetic.
- Follow-up: Re-engaging, curious.

Ensure the replyContent is written in the same language as the customer's message. Don't use placeholders.";

            var fullPrompt = $"{systemPrompt}\n\nCustomer Message: \"{messageContent}\"";

            string rawResponse = await _geminiClient.GenerateReplyAsync(fullPrompt, apiKeyOverride);

            if (string.IsNullOrEmpty(rawResponse))
            {
                return new MarketingAnalysisResult
                {
                    ReplyContent = "[AI Error Recovery] Empty response from AI engine."
                };
            }

            // Clean markdown tags if Gemini wraps it in ```json ... ```
            string cleanedResponse = rawResponse.Trim();
            if (cleanedResponse.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                cleanedResponse = cleanedResponse.Substring(7);
            }
            if (cleanedResponse.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                cleanedResponse = cleanedResponse.Substring(0, cleanedResponse.Length - 3);
            }
            cleanedResponse = cleanedResponse.Trim();

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var result = JsonSerializer.Deserialize<MarketingAnalysisResult>(cleanedResponse, options);
                if (result != null)
                {
                    // Ensure entities is initialized
                    result.Entities ??= new CRMEntities();
                    result.Entities.Interests ??= Array.Empty<string>();
                    return result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIMarketingBrain] Failed to parse JSON response: {ex.Message}. Raw: {rawResponse}");
            }

            // Fallback if not valid JSON
            return new MarketingAnalysisResult
            {
                Intent = "inquiry",
                Sentiment = "neutral",
                ReplyStyle = "Casual",
                ReplyContent = rawResponse, // Use the raw text as content
                Confidence = 0.5
            };
        }
    }
}
