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
        public string? Transcription { get; set; }
        public SuggestedFollowUpResult? SuggestedFollowUp { get; set; }
        public string[] SuggestedButtons { get; set; } = Array.Empty<string>();
        public string? SuggestedReaction { get; set; }
        public string? SuggestedGroupBookingId { get; set; }
    }

    public class SuggestedFollowUpResult
    {
        public bool Needed { get; set; }
        public string Type { get; set; } = "Nurturing"; // Nurturing, AppointmentReminder
        public string? AppointmentTime { get; set; }
        public string? DueDate { get; set; }
        public string? Notes { get; set; }
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
            string customerProfile = null,
            byte[] fileBytes = null,
            string mimeType = null,
            string aiTonePreference = null,
            string aiTargetAudience = null);
    }

    public class AIMarketingBrain : IAIMarketingBrain
    {
        private readonly IGeminiClient _geminiClient;

        public AIMarketingBrain(IGeminiClient geminiClient)
        {
            _geminiClient = geminiClient;
        }

        private string GetCurrentAgentName()
        {
            TimeZoneInfo cairoZone;
            try
            {
                cairoZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    cairoZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
                }
                catch
                {
                    cairoZone = TimeZoneInfo.Utc;
                }
            }

            var cairoTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairoZone);
            int hour = cairoTime.Hour;

            // Shifts (Cairo Local Time) using the 5 names:
            // 00:00 - 05:00 -> ساجي
            // 05:00 - 10:00 -> لارا
            // 10:00 - 15:00 -> مادلين
            // 15:00 - 19:00 -> شاهي
            // 19:00 - 24:00 -> ساندي
            if (hour >= 0 && hour < 5)
            {
                return "ساجي";
            }
            else if (hour >= 5 && hour < 10)
            {
                return "لارا";
            }
            else if (hour >= 10 && hour < 15)
            {
                return "مادلين";
            }
            else if (hour >= 15 && hour < 19)
            {
                return "شاهي";
            }
            else
            {
                return "ساندي";
            }
        }

        public async Task<MarketingAnalysisResult> AnalyzeAndGenerateReplyAsync(
            string messageContent, 
            string apiKeyOverride = null, 
            string brainContext = null, 
            string chatHistory = null, 
            string customerMemory = null,
            string[] existingLabels = null,
            string customerProfile = null,
            byte[] fileBytes = null,
            string mimeType = null,
            string aiTonePreference = null,
            string aiTargetAudience = null)
        {
            var agentName = GetCurrentAgentName();
            Console.WriteLine($"[AIMarketingBrain] Active shift agent resolved: {agentName} (UTC hour: {DateTime.UtcNow.Hour})");

            var systemPrompt = @"You are a high-performing AI Marketing Brain and CRM assistant.
Your name is [AGENT_NAME]. You MUST sign off your response with a signature as the very last line of your reply.
- Normally, sign off with '- [AGENT_NAME] ✨'.
- CRITICAL: If the customer's sentiment is 'angry' or 'negative', or if you classify the replyStyle as 'Complaint':
  1. Set replyStyle to 'Complaint'.
  2. Write an extremely apologetic, polite, and empathetic response.
  3. Do NOT use any sparkles (✨) or cheerful/playful emojis anywhere in the replyContent.
  4. Sign off with a plain signature '- [AGENT_NAME]' (without the '✨' sparkles) to maintain a respectful and serious tone.
  5. Set suggestedFollowUp.needed to false (because complaints/angry customers require immediate human resolution and manual follow-up, never send them automated messages).

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
  ""confidence"": 0.95,
  ""transcription"": ""string | null"",
  ""suggestedFollowUp"": {
    ""needed"": true | false,
    ""type"": ""Nurturing | AppointmentReminder"",
    ""appointmentTime"": ""ISO_DATETIME_STRING (UTC) | null"",
    ""dueDate"": ""ISO_DATETIME_STRING (UTC)"",
    ""notes"": ""Arabic message content customized to the customer's context and conversation state, to be sent to them automatically""
  },
  ""suggestedReaction"": ""👍 | ❤️ | 💖 | 😢 | 😂 | 😮 | null"",
  ""suggestedGroupBookingId"": ""GUID_OF_GROUP | null""
}

Guidelines for suggestedReaction:
- suggestedReaction: Set to a single emoji (👍, ❤️, 💖, 😢, 😂, 😮) or null. Suggest an emoji reaction to the customer's message only if it adds a warm, human-like touch (e.g. ❤️/💖 for gratitude, joy, or positive feedback; 😢 for sadness or complaints; 😂 for jokes; 👍 for agreement or simple acknowledgment). Otherwise, return null.

Guidelines for suggestedGroupBookingId (Auto-Booking):
- IMPORTANT: When the customer explicitly expresses intent to book or register in a group appointment (e.g. ""عايز أحجز"", ""سجلني"", ""أنا جاهز"", ""أيوه عايز"", ""احجزلي"", ""مواعيد المجموعات"", ""عندكم أماكن؟"", ""ينفع اشترك""), set suggestedGroupBookingId to the GUID of the appropriate group.
- If there is only ONE available group with remaining slots, auto-select it directly and confirm the booking in your reply (e.g. ""تمام يا فندم، سجلتك في مجموعة X"").
- If there are MULTIPLE available groups, first ask which group they prefer. Once they specify or confirm, set suggestedGroupBookingId to that group's GUID.
- If ALL groups are full (or no groups are listed), set to null and tell the customer there are no available slots currently.
- When you set suggestedGroupBookingId, write a warm confirmation in replyContent telling the customer they have been registered successfully. The system will handle the actual booking automatically.
- NEVER set suggestedGroupBookingId if the customer hasn't explicitly asked to book/register.
- NEVER mention any group that is marked as ""ممتلئة تماماً"" (full) to the customer.

Guidelines for suggestedFollowUp:
- needed: Set to true if the customer booked an appointment/course (requires AppointmentReminder) OR if they are hesitant, cold, or waiting for feedback (requires Nurturing). Otherwise false.
- type: Use 'AppointmentReminder' for booked appointments/courses. Use 'Nurturing' for re-engaging hesitant or cold leads.
- appointmentTime: Specify the target datetime of the appointment/course in ISO format (UTC), only if type is 'AppointmentReminder'. Otherwise null.
- dueDate:
  - For AppointmentReminder: Must be exactly 24 hours prior to appointmentTime. If the appointment is less than 24 hours away, set dueDate to the current time.
  - For Nurturing: Set to a reasonable re-engagement time (typically 1 to 3 days from the current time).
- notes: Provide a warm, personalized message in friendly Arabic tailored specifically to the customer's context (e.g. reminding them of their specific session time, or asking them if they had time to review the details, tailored to their exact hesitation). Do not use placeholders.

            Guidelines for replyStyle:
- Fast: Short, immediate answers.
- Casual: Friendly, informal tone.
- Sales: Persuasive, highlighting benefits, includes a clear CTA.
- Support: Empathetic, helpful.
- VIP: Exclusive, highly polite.
- Complaint: Apologetic, resolution-focused, highly empathetic.
- Follow-up: Re-engaging, curious.

Guidelines for replyContent tone, style, and vocabulary:
- TONE & DIALECT PREFERENCE: You must write in: [TONE_PREFERENCE]. Adjust your vocabulary, greetings, and syntax to perfectly match this dialect and tone.
- TARGET AUDIENCE: You are talking to: [TARGET_AUDIENCE]. Tailor your message style, concerns, and persuasive arguments specifically to this audience's level, interests, and needs.
- RESPECT & POLITENESS: Always remain polite, respectful, and professional. Avoid any offensive, overly casual, or inappropriate slang. The customer must feel respected and valued at all times.
- NO CORPORATE DRYNESS: Avoid dry, formal Standard Arabic (الفصحى الجافة) and avoid structured corporate-style headings (e.g. NEVER use headings like ""1. نظام الدراسة:"" or ""2. التوظيف:"").
- CONVERSATIONAL FLOW & TRANSITIONS: Present details as a single cohesive story or conversation, using natural, friendly connectors matching the chosen dialect (e.g. for Egyptian: ""بص يا سيدي..."", ""من الآخر...""; for Gulf: ""طال عمرك..."", ""تفضل يا طيب..."", etc.) instead of rigid academic lists.
- PERSUASIVE WRITING: Present the details as an exciting opportunity rather than a dry list of facts. Keep the energy high and engaging!

Guidelines for replyContent formatting and unity:
- CRITICAL: Write a SINGLE cohesive response. Do NOT paste multiple different scripts, greeting scripts, or welcome templates together.
- Do NOT repeat greetings (e.g. do not say 'أهلاً' or 'مرحباً' or 'نورتنا' more than once in the same response).
- Do NOT include multiple signature lines or repeat agent names (e.g. never output '- [AGENT_NAME] ✨' or '- [AGENT_NAME]' more than once).
- If the reference knowledge base contains multiple templates, scripts, or FAQs, synthesize their facts into a single natural message.
- Strictly avoid repeating the same request/question (e.g. do not ask for the same customer details multiple times or in different styles).
- Ensure there are no redundant paragraphs. Keep it professional, warm, and concise in Arabic.
- Use double newlines ('\n\n') ONLY to separate logical paragraphs. Keep the number of paragraphs to a minimum (typically 1 to 2 paragraphs max) to avoid sending too many small message bubbles.

Ensure the replyContent is written in the same language as the customer's message. Don't use placeholders.
Be concise, natural, and friendly. Do not repeat greetings or duplicate questions. Keep your replyContent focused on answering the customer's direct query without unnecessary fluff.";

            var tonePref = !string.IsNullOrEmpty(aiTonePreference) ? aiTonePreference : "العامية المصرية الروشة والصايعة";
            var targetAud = !string.IsNullOrEmpty(aiTargetAudience) ? aiTargetAudience : "طلاب كورس كول سنتر يبحثون عن عمل";

            systemPrompt = systemPrompt.Replace("[AGENT_NAME]", agentName);
            systemPrompt = systemPrompt.Replace("[TONE_PREFERENCE]", tonePref);
            systemPrompt = systemPrompt.Replace("[TARGET_AUDIENCE]", targetAud);

            var tonePrefLower = tonePref.ToLower();
            if (tonePrefLower.Contains("روشة") || tonePrefLower.Contains("صايعة") || (tonePrefLower.Contains("مصر") && tonePrefLower.Contains("روشة")))
            {
                systemPrompt += "\n\nSpecific guidelines for Egyptian Slang Tone (العامية المصرية الروشة والصايعة):\n" +
                                 "- Use natural, cool, and highly conversational transitions like: \"بص يا سيدي...\", \"بص بقى يا صاحبي...\", \"سكة ودغري كدة...\", \"من الآخر...\", \"أول حاجة لازم تعرفها...\", \"تاني حاجة بقى ودي الأهم...\", \"هتطلع من الكورس ده بتتكلم إنجليزي زي الأجانب وبثقة...\", \"هندلعك في المتابعة والتاسكات...\", \"الشغل مضمون وفي جيبك...\".\n" +
                                 "- Use warm, popular Egyptian words that build rapport and sound authentic, such as: \"يا غالي\", \"يا صديقي\", \"يا صاحبي\", \"باشا\", \"سكة ودغري\", \"في الرايق\", \"على الهادي\", \"تظبط الكلام\", \"في الجون\", \"حاجة عظمة\". Do not use offensive language.";
            }
            else if (tonePrefLower.Contains("مهذب") || tonePrefLower.Contains("محترم") || tonePrefLower.Contains("لذيذ") || tonePrefLower.Contains("عامي") || (tonePrefLower.Contains("مصر") && (tonePrefLower.Contains("مهذب") || tonePrefLower.Contains("محترم"))))
            {
                systemPrompt += "\n\nSpecific guidelines for Polite Egyptian Colloquial Tone (العامية المصرية المهذبة والمحترمة):\n" +
                                 "- Use polite, warm, and professional Egyptian colloquial Arabic (عامية مصرية راقية ومحترمة).\n" +
                                 "- Use polite greetings and transitions like: \"أهلاً بك يا فندم...\", \"تحياتي لحضرتك...\", \"بص يا فندم...\", \"خليني أوضح لحضرتك...\", \"تحت أمرك في أي وقت...\".\n" +
                                 "- Build trust and rapport without being overly casual or using street slang. Avoid words like \"روشة\", \"صايعة\", \"هندلعك\", \"في جيبك\". Use respectful terms like \"يا فندم\", \"حضرتك\", \"يسعدنا جداً\".";
            }
            else if (tonePrefLower.Contains("خليج") || tonePrefLower.Contains("سعودي"))
            {
                systemPrompt += "\n\nSpecific guidelines for Gulf Dialect (اللهجة الخليجية المهذبة):\n" +
                                 "- Write in polite, warm, and authentic Gulf Arabic (لهجة خليجية بيضاء مهذبة).\n" +
                                 "- Use common polite transitions and phrases like: \"طال عمرك...\", \"تفضل يا طيب...\", \"يا هلا ومرحبا فيك...\", \"حياك الله يا فندم...\", \"يسعدنا خدمتكم...\", \"أبشر بالخير...\".\n" +
                                 "- Always address the customer respectfully using \"عمرك\", \"حضرتك\", or \"طال عمرك\".";
            }
            else if (tonePrefLower.Contains("فصحى") || tonePrefLower.Contains("عربية فصحى"))
            {
                systemPrompt += "\n\nSpecific guidelines for Simplified Modern Standard Arabic (العربية الفصحى المبسطة):\n" +
                                 "- Write in clear, warm, and modern simplified standard Arabic (فصحى مبسطة وودودة).\n" +
                                 "- Avoid overly rigid, archaic, or complex classical words, but maintain correct grammar.\n" +
                                 "- Use warm expressions like: \"أهلاً بك عزيزي...\", \"يسعدنا جداً تواصلك معنا...\", \"يسرني أن أوضح لك...\", \"بكل تأكيد...\".";
            }

            systemPrompt += $"\n\nCRITICAL: The current UTC time is {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}. Use this as the reference time to calculate all relative dates/times for follow-ups (e.g. 'tomorrow at 7 PM' or 'in 2 days').";

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

            if (fileBytes != null && mimeType != null)
            {
                if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                {
                    systemPrompt += "\n\nCRITICAL: The attached file is a WhatsApp voice note. Transcribe it exactly in its original language, and include the transcription in your JSON response under the 'transcription' key.";
                }
                else if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    systemPrompt += "\n\nCRITICAL: The attached file is an image sent by the customer. Analyze its visual details to extract CRM facts, billing values, city, budget, tags, etc. and map them to the CRM entities JSON payload.";
                }
            }

            var fullPrompt = $"{systemPrompt}\n\nCustomer Message: \"{messageContent}\"";

            string rawResponse;
            if (fileBytes != null && mimeType != null)
            {
                rawResponse = await _geminiClient.GenerateReplyAsync(fullPrompt, fileBytes, mimeType, apiKeyOverride);
            }
            else
            {
                rawResponse = await _geminiClient.GenerateReplyAsync(fullPrompt, apiKeyOverride);
            }

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
                    result.SuggestedButtons ??= Array.Empty<string>();
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
                PipelineStage = "New",
                SuggestedButtons = Array.Empty<string>()
            };
        }
    }
}
