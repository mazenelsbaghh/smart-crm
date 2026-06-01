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
        public string Label { get; set; } = string.Empty;
        public string PipelineStage { get; set; } = "New";
    }

    public interface IAIMarketingBrain
    {
        Task<MarketingAnalysisResult> AnalyzeAndGenerateReplyAsync(
            string messageContent, 
            string apiKeyOverride = null, 
            string brainContext = null, 
            string chatHistory = null, 
            string customerMemory = null,
            string[] existingLabels = null,
            string customerProfile = null);
    }

    public class AIMarketingBrain : IAIMarketingBrain
    {
        private readonly IGeminiClient _geminiClient;

        public AIMarketingBrain(IGeminiClient geminiClient)
        {
            _geminiClient = geminiClient;
        }

        public async Task<MarketingAnalysisResult> AnalyzeAndGenerateReplyAsync(
            string messageContent, 
            string apiKeyOverride = null, 
            string brainContext = null, 
            string chatHistory = null, 
            string customerMemory = null,
            string[] existingLabels = null,
            string customerProfile = null)
        {
            var systemPrompt = @"You are a high-performing AI Marketing Brain and CRM assistant.
Analyze the customer's message and generate a response.
You MUST respond strictly in the following JSON format, and nothing else (no markdown blocks like ```json):
{
  ""intent"": ""inquiry | complaint | purchase | follow-up | greeting"",
  ""sentiment"": ""positive | neutral | negative | angry"",
  ""replyStyle"": ""Fast | Casual | Sales | Support | VIP | Complaint | Follow-up"",
  ""label"": ""a short Arabic label (max 3 words) classifying the customer's current state/need based on the message, e.g., 'استفسار عن السعر', 'طلب شراء', 'شكوى', 'ترحيب'"",
  ""pipelineStage"": ""New | Contacted | Qualified | Proposal | Negotiation | Won | Lost"",
  ""entities"": {
    ""city"": ""string | null"",
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

Guidelines for replyContent formatting and unity:
- CRITICAL: Write a SINGLE cohesive response. Do NOT paste multiple different scripts, greeting scripts, or welcome templates together.
- Do NOT repeat greetings (e.g. do not say 'أهلاً' or 'مرحباً' or 'نورتنا' more than once in the same response).
- Do NOT include multiple signature lines or repeat agent names (e.g. never output '- ساندي ✨' more than once).
- If the reference knowledge base contains multiple templates, scripts, or FAQs, synthesize their facts into a single natural message.
- Strictly avoid repeating the same request/question (e.g. do not ask for the same customer details multiple times or in different styles).
- Ensure there are no redundant paragraphs. Keep it professional, warm, and concise in Arabic.
- Use double newlines ('\n\n') ONLY to separate logical paragraphs. Keep the number of paragraphs to a minimum (typically 1 to 3 paragraphs max) to avoid sending too many small message bubbles.

Ensure the replyContent is written in the same language as the customer's message. Don't use placeholders.
Be concise, natural, and friendly. Do not repeat greetings or duplicate questions. Keep your replyContent focused on answering the customer's direct query without unnecessary fluff.";

            if (existingLabels != null && existingLabels.Length > 0)
            {
                var labelsList = string.Join(", ", System.Linq.Enumerable.Select(existingLabels, l => $"'{l}'"));
                systemPrompt += $"\n\nExisting Customer Labels currently in use in our database:\n[{labelsList}]\nChoose ONE of these exact existing labels for the \"label\" field. Do not invent a new label if existing labels are provided above.";
            }

            if (!string.IsNullOrEmpty(customerProfile))
            {
                systemPrompt += $"\n\nHere is the customer's current CRM profile details:\n{customerProfile}\n" +
                                 "Identify if any fields are marked as \"Missing\". If there are missing fields (such as Name or City), you MUST politely and naturally ask the customer for them during the conversation. Do not ask for everything at once; ask for them step-by-step in a friendly Arabic conversational style, only if it fits the flow of the conversation.";
            }

            if (!string.IsNullOrEmpty(brainContext))
            {
                systemPrompt += $"\n\nUse the following reference knowledge base information to accurately answer the customer's questions if applicable:\n{brainContext}\n\nDo not invent facts outside this reference information if it contains pricing, shipping, or policies.";
            }

            if (!string.IsNullOrEmpty(customerMemory))
            {
                systemPrompt += $"\n\nHere is what you remember about this customer (Customer Profile & Memory):\n{customerMemory}";
            }

            if (!string.IsNullOrEmpty(chatHistory))
            {
                systemPrompt += $"\n\nHere is the recent chat history between the customer and Agent/AI for context:\n{chatHistory}";
            }

            var fullPrompt = $"{systemPrompt}\n\nCustomer Message: \"{messageContent}\"";

            string rawResponse = await _geminiClient.GenerateReplyAsync(fullPrompt, apiKeyOverride);

            if (string.IsNullOrEmpty(rawResponse))
            {
                return new MarketingAnalysisResult
                {
                    Intent = "inquiry",
                    Sentiment = "neutral",
                    ReplyStyle = "Casual",
                    ReplyContent = "أهلاً بك! سنقوم بالرد عليك في أقرب وقت ممكن.",
                    Confidence = 0.5,
                    Label = "استفسار عام",
                    PipelineStage = "New"
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
                    if (string.IsNullOrEmpty(result.Label))
                    {
                        result.Label = "استفسار عام";
                    }
                    if (string.IsNullOrEmpty(result.PipelineStage))
                    {
                        result.PipelineStage = "New";
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIMarketingBrain] Failed to parse JSON response: {ex.Message}. Raw: {rawResponse}");
            }

            // Fallback if not valid JSON
            string fallbackReply = rawResponse;
            if (string.IsNullOrEmpty(rawResponse) || 
                rawResponse.StartsWith("[AI_ERROR]") || 
                rawResponse.StartsWith("[AI Error Recovery]") ||
                !rawResponse.Trim().StartsWith("{"))
            {
                if (rawResponse != null && rawResponse.StartsWith("[Mock Gemini Reply]"))
                {
                    fallbackReply = rawResponse;
                }
                else
                {
                    fallbackReply = "أهلاً بك! سنقوم بالرد عليك في أقرب وقت ممكن.";
                }
            }

            return new MarketingAnalysisResult
            {
                Intent = "inquiry",
                Sentiment = "neutral",
                ReplyStyle = "Casual",
                ReplyContent = fallbackReply,
                Confidence = 0.5,
                Label = "استفسار عام",
                PipelineStage = "New"
            };
        }
    }
}
