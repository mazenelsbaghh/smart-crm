using System;
using System.Text.RegularExpressions;
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
        public string? PublicCommentReply { get; set; }
        public double Confidence { get; set; } = 1.0;
        public string Label { get; set; } = string.Empty;
        public string PipelineStage { get; set; } = "New";
        public string? Transcription { get; set; }
        public SuggestedFollowUpResult? SuggestedFollowUp { get; set; }
        public string[] SuggestedButtons { get; set; } = Array.Empty<string>();
        public string? SuggestedReaction { get; set; }
        public string? SuggestedGroupBookingId { get; set; }
        public bool CancelGroupBooking { get; set; } = false;
        public string[] AIInsights { get; set; } = Array.Empty<string>();
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
            string aiTargetAudience = null,
            string geminiModel = null,
            string cachedContentId = null);

        string BuildStaticPrompt(
            string agentName,
            string tonePref,
            string targetAud,
            string approvedKnowledgeBaseText);

        string GetCurrentAgentName();
        Task<string> RewriteFollowUpNotesAsync(
            string customerName,
            string notes,
            bool hasAttended,
            string? tone = null,
            string? apiKeyOverride = null,
            string? modelOverride = null);
    }

    public class AIMarketingBrain : IAIMarketingBrain
    {
        private readonly IGeminiClient _geminiClient;

        private const string SystemPromptTemplate = @"You are a high-performing AI Marketing Brain and CRM assistant communicating with customers through WhatsApp messaging.
CRITICAL CONTEXT: You are chatting with customers on WhatsApp. This means:
- Write SHORT, conversational messages like a real person texting on WhatsApp. Not long paragraphs.
- Use WhatsApp-friendly formatting: emojis, short sentences, casual tone.
- IMPORTANT about links/URLs: Do NOT invent or generate any URLs on your own. If the customer asks for the location, address, map, or how to get to the center, you MUST provide the specific location/Google Maps link (e.g., the URL starting with maps.google or maps.app.goo.gl) from the reference knowledge base. Do NOT confuse the company's website or outsourcing URLs (like talktips-outsourcing.com or talktips-academy.com) with the map/location link.
- NEVER use markdown formatting (no headers, no bold with **, no bullet lists with -). Just plain text with emojis.
- Keep messages concise (2-4 short paragraphs MAX). Nobody reads long walls of text on WhatsApp.
- Sound like a real human customer service agent texting, not a robot or a website chatbot.
- Use line breaks between ideas for readability in chat bubbles.
- CRITICAL LANGUAGE RULE: Always write replyContent in Arabic, preferably polite Egyptian Arabic, even if the customer writes in English, Arabizi, or mixed Arabic/English. Do not switch the reply language to English unless the customer explicitly asks you to reply in English.

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
  ""replyContent"": ""your human-like helpful reply text here (used as the private DM when channel is FacebookComment, and as the main message for WhatsApp/Messenger)"",
  ""publicCommentReply"": ""brief public comment reply in Arabic here (ONLY when channel is FacebookComment, e.g. 'تم الرد في الخاص يا فندم! 🌸') or null otherwise"",
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
  ""suggestedGroupBookingId"": ""GUID_OF_GROUP | null"",
  ""cancelGroupBooking"": true | false,
  ""aiInsights"": [""2-3 brief insights/recommendations about the customer behavior/needs in Arabic based on the conversation history, e.g. 'العميل مهتم ببرنامج متقدم', 'يرغب في تغيير موعد حجز المجموعة' (max 10-15 words per insight)""]
}

Guidelines for publicCommentReply:
- Set this field ONLY when the communication channel is a Facebook comment (i.e. 'Facebook Comment').
- Write a short, friendly, and welcoming public comment in polite Arabic/Colloquial Egyptian dialect that refers the user to check their private message inbox (e.g. ""تم الرد في الرسائل الخاصة يا فندم! 🌸"", ""تواصلنا مع حضرتك في الرسائل للتفاصيل كاملة! ✨"", ""أهلاً بك يا فندم! أرسلنا لحضرتك التفاصيل كاملة في رسالة خاصة، يرجى مراجعة صندوق الرسائل."").
- Keep it to a single, polite public comment.
- If the communication channel is WhatsApp or Messenger, set publicCommentReply to null.
- CRITICAL RULE FOR APOLOGIES, GREETINGS AND SHORT ACKNOWLEDGMENTS: If the customer is apologizing (e.g., saying they cannot attend, canceling their appointment, apologizing for a delay), greeting, or just saying thank you without asking for details:
  1. Do NOT dump long course details, prices, or links in replyContent.
  2. Reply to them contextually and concisely within the limits of their message (e.g., ""حصل خير يا فندم تنورنا في أي وقت!"" or ""ولا يهمك يا غالي تتعوض إن شاء الله"").
  3. For Facebook Comments: Write this contextual response directly in the publicCommentReply field, and set replyContent (private DM) to null or a very brief greeting like ""تحت أمرك يا فندم في أي وقت!"" to avoid spamming their inbox with duplicate details.

Guidelines for suggestedReaction:
- suggestedReaction: Set to a single emoji (👍, ❤️, 💖, 😢, 😂, 😮) or null. Suggest an emoji reaction to the customer's message only if it adds a warm, human-like touch (e.g. ❤️/💖 for gratitude, joy, or positive feedback; 😢 for sadness or complaints; 😂 for jokes; 👍 for agreement or simple acknowledgment). Otherwise, return null.

Guidelines for suggestedGroupBookingId (Auto-Booking):
- IMPORTANT: When the customer explicitly expresses intent to book or register in a group appointment (e.g. ""عايز أحجز"", ""سجلني"", ""أنا جاهز"", ""أيوه عايز"", ""احجزلي"", ""مواعيد المجموعات"", ""عندكم أماكن؟"", ""ينفع اشترك""), set suggestedGroupBookingId to the GUID of the appropriate group.
- If there is only ONE available group with remaining slots, auto-select it directly and confirm the booking in your reply (e.g. ""تمام يا فندم، سجلتك in مجموعة X"").
- If there are MULTIPLE available groups, first ask which group they prefer. Once they specify or confirm, set suggestedGroupBookingId to that group's GUID.
- If ALL groups are full (or no groups are listed), set to null and tell the customer there are no available slots currently.
- When you set suggestedGroupBookingId, write a warm confirmation in replyContent telling the customer they have been registered successfully. The system will handle the actual booking automatically.
- NEVER set suggestedGroupBookingId if the customer hasn't explicitly asked to book/register.
- NEVER mention any group that is marked as ""ممتلئة تماماً"" (full) to the customer.
- STRICTOR VERIFICATION RULES (قوانين صارمة للتحقق من المواعيد وتغييرها):
  1. يُمنع منعاً باتاً الموافقة أو تأكيد أي حجز في موعد أو وقت غير متوفر في 'قائمة المجموعات المتاحة حالياً'. إذا طلب العميل موعداً غير متاح أو طلب تعديل الموعد لوقت آخر (مثل تغيير موعد من 4 إلى 5 ولا توجد مجموعة متاحة الساعة 5)، فلا تقل له 'تمام' أو 'ماشي' ولا تؤكد الحجز؛ بل وضح له بدقة ولطف المواعيد والتواريخ المتاحة فعلياً من القائمة واطلب منه الاختيار منها.
  2. عند عرض المجموعات المتاحة أو تأكيد الحجز، يجب كتابة الميعاد بالتفصيل شاملاً اليوم والتاريخ والساعة (مثال: 'يوم السبت 12/6 الساعة 4:00 مساءً') ولا تكتفِ بذكر الساعة فقط، حتى يكون العميل على علم كامل بالتفاصيل والتاريخ الفعلي للموعد.

Guidelines for cancelGroupBooking (Auto-Cancellation):
- Set cancelGroupBooking to true ONLY if the customer explicitly requests to cancel their booking, delete their reservation, says they are not coming, or asks to be removed from the group (e.g., ""عايز ألغي الحجز"", ""مش جاي خلاص"", ""احذف حتة الحجز"", ""إلغاء الميعاد""). Otherwise, set to false.
- When you set cancelGroupBooking to true, write a polite, empathetic, and comforting reply in replyContent confirming the cancellation, letting them know it is done, and friendly asking if they would like to reschedule/book a different time later, or how you can assist them further to adjust their schedule (""يظبط معاهم"").

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
- CONVERSATIONAL FLOW & TRANSITIONS: Present details as a single cohesive story or conversation, using natural, friendly connectors matching the chosen dialect and tone preference instead of rigid academic lists. Do not use generic dialect examples if they conflict with the specific tone guidelines below.
- PERSUASIVE WRITING: Present the details as an exciting opportunity rather than a dry list of facts. Keep the energy high and engaging!

Guidelines for replyContent formatting and unity:
- CRITICAL: Write a SINGLE cohesive response. Do NOT paste multiple different scripts, greeting scripts, or welcome templates together.
- CRITICAL PRICING RULE: You MUST strictly use the exact pricing numbers from the reference knowledge base (e.g. 1000 EGP monthly subscription, 3000 EGP cash for the full 4-month course). NEVER invent, hallucinate, or change these numbers (e.g. do not say the price is 1500 EGP). If the customer asks about price, cost, fees, payment, ""السعر"", ""الأسعار"", ""بكام"", or similar, you MUST answer with the exact pricing numbers immediately. NEVER say pricing is decided after the free session, after level assessment, or after a trial session.
- Do NOT repeat greetings (e.g. do not say 'أهلاً' or 'مرحباً' or 'نورتنا' more than once in the same response).
- Do NOT include multiple signature lines or repeat agent names (e.g. never output '- [AGENT_NAME] ✨' or '- [AGENT_NAME]' more than once).
- If the reference knowledge base contains multiple templates, scripts, or FAQs, synthesize their facts into a single natural message.
- Strictly avoid repeating the same request/question (e.g. do not ask for the same customer details multiple times or in different styles).
- Ensure there are no redundant paragraphs. Keep it professional, warm, and concise in Arabic.
- Use double newlines ('\n\n') ONLY to separate logical paragraphs. Keep the number of paragraphs to a minimum (typically 1 to 2 paragraphs max) to avoid sending too many small message bubbles.

Ensure the replyContent is always written in Arabic unless the customer explicitly asks for English. Don't use placeholders.
Be concise, natural, and friendly. Do not repeat greetings or duplicate questions. Keep your replyContent focused on answering the customer's direct query without unnecessary fluff.";

        public AIMarketingBrain(IGeminiClient geminiClient)
        {
            _geminiClient = geminiClient;
        }

        private static void NormalizeReaction(MarketingAnalysisResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.SuggestedReaction))
            {
                return;
            }

            var isComplaint = string.Equals(result.ReplyStyle, "Complaint", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.Intent, "complaint", StringComparison.OrdinalIgnoreCase);
            var isNegative = string.Equals(result.Sentiment, "negative", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.Sentiment, "angry", StringComparison.OrdinalIgnoreCase);

            if (isComplaint || isNegative)
            {
                result.SuggestedReaction = "😢";
                return;
            }

            var isPositive = string.Equals(result.Sentiment, "positive", StringComparison.OrdinalIgnoreCase);
            var isPurchase = string.Equals(result.Intent, "purchase", StringComparison.OrdinalIgnoreCase);
            var isGreeting = string.Equals(result.Intent, "greeting", StringComparison.OrdinalIgnoreCase);

            if (isPositive || isPurchase)
            {
                result.SuggestedReaction = "❤️";
                return;
            }

            if (isGreeting)
            {
                result.SuggestedReaction = "💖";
            }
        }

        public string GetCurrentAgentName()
        {
            var cairoZone = Shared.Infrastructure.TimezoneHelper.GetTimeZone("Africa/Cairo");
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
            string aiTargetAudience = null,
            string geminiModel = null,
            string cachedContentId = null)
        {
            var agentName = GetCurrentAgentName();
            Console.WriteLine($"[AIMarketingBrain] Active shift agent resolved: {agentName} (UTC hour: {DateTime.UtcNow.Hour})");

            string fullPrompt;
            if (!string.IsNullOrEmpty(cachedContentId))
            {
                var dynamicPrompt = $@"CRITICAL: The current UTC time is {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}. Use this as the reference time to calculate all relative dates/times for follow-ups (e.g. 'tomorrow at 7 PM' or 'in 2 days').";

                if (!string.IsNullOrEmpty(customerProfile))
                {
                    dynamicPrompt += $"\n\nHere is the customer's current CRM profile details:\n{customerProfile}\n" +
                                     "Identify if any fields are marked as \"Missing\". If there are missing fields (such as Name or City), you MUST politely and naturally ask the customer for them during the conversation. Do not ask for everything at once; ask for them step-by-step in a friendly Arabic conversational style, only if it fits the flow of the conversation.";
                }

                if (!string.IsNullOrEmpty(customerMemory))
                {
                    dynamicPrompt += $"\n\nHere is what you remember about this customer (Customer Profile & Memory):\n{customerMemory}";
                }

                if (!string.IsNullOrEmpty(chatHistory))
                {
                    dynamicPrompt += $"\n\nHere is the recent chat history between the customer and Agent/AI for context:\n{chatHistory}";
                }

                if (fileBytes != null && mimeType != null)
                {
                    if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                    {
                        dynamicPrompt += "\n\nCRITICAL: The attached file is a WhatsApp voice note. Transcribe it exactly in its original language, and include the transcription in your JSON response under the 'transcription' key.";
                    }
                    else if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        dynamicPrompt += "\n\nCRITICAL: The attached file is an image sent by the customer. Analyze its visual details to extract CRM facts, billing values, city, budget, tags, etc. and map them to the CRM entities JSON payload.";
                    }
                }

                if (!string.IsNullOrEmpty(brainContext))
                {
                    dynamicPrompt += $"\n\nUse the following reference information (such as active booking slots) if applicable:\n{brainContext}";
                }

                fullPrompt = $"{dynamicPrompt}\n\nCustomer Message: \"{messageContent}\"\n\nAnalyze the message, query the cached knowledge base and system instructions, and output the result strictly in the requested JSON format.";
            }
            else
            {
                var systemPrompt = SystemPromptTemplate;

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
                                 "- NEVER use street slang or overly casual connectors like \"بص يا سيدي\", \"من عيوني\", \"من الآخر\", \"سكة ودغري\", \"يا صاحبي\", \"هندلعك\", \"في جيبك\".\n" +
                                 "- NEVER address the customer directly using an informal name call combined with a polite title (e.g. do NOT say \"يا مارفن يا فندم\"). If addressing the customer by name, always use a polite prefix (e.g. \"أستاذ مارفن\") or simply address them as \"يا فندم\" or \"حضرتك\" without their name.\n" +
                                 "- Build trust and rapport without being overly casual. Use respectful terms like \"يا فندم\", \"حضرتك\", \"يسعدنا جداً\".";
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

                fullPrompt = $"{systemPrompt}\n\nCustomer Message: \"{messageContent}\"";
            }

            string rawResponse;
            if (fileBytes != null && mimeType != null)
            {
                rawResponse = await _geminiClient.GenerateReplyAsync(fullPrompt, fileBytes, mimeType, apiKeyOverride, geminiModel, cachedContentId);
            }
            else
            {
                rawResponse = await _geminiClient.GenerateReplyAsync(fullPrompt, apiKeyOverride, geminiModel, cachedContentId);
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
                    result.AIInsights ??= Array.Empty<string>();
                    if (string.IsNullOrEmpty(result.Label))
                    {
                        result.Label = "استفسار عام";
                    }
                    if (string.IsNullOrEmpty(result.PipelineStage))
                    {
                        result.PipelineStage = "New";
                    }
                    if (PricingGuard.IsPricingQuestion(messageContent))
                    {
                        var pricingReply = PricingGuard.BuildPricingReplyFromKnowledge(brainContext);
                        if (!string.IsNullOrWhiteSpace(pricingReply))
                        {
                            result.Intent = "inquiry";
                            result.Label = "استفسار عن السعر";
                            result.ReplyStyle = "Sales";
                            result.ReplyContent = pricingReply;
                            result.Confidence = Math.Max(result.Confidence, 0.99);
                        }
                    }
                    NormalizeReaction(result);
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

            var fallbackResult = new MarketingAnalysisResult
            {
                Intent = "inquiry",
                Sentiment = "neutral",
                ReplyStyle = "Casual",
                ReplyContent = fallbackReply,
                Confidence = 0.5,
                Label = "استفسار عام",
                PipelineStage = "New",
                SuggestedButtons = Array.Empty<string>(),
                AIInsights = Array.Empty<string>()
            };
            if (PricingGuard.IsPricingQuestion(messageContent))
            {
                var pricingReply = PricingGuard.BuildPricingReplyFromKnowledge(brainContext);
                if (!string.IsNullOrWhiteSpace(pricingReply))
                {
                    fallbackResult.Label = "استفسار عن السعر";
                    fallbackResult.ReplyStyle = "Sales";
                    fallbackResult.ReplyContent = pricingReply;
                    fallbackResult.Confidence = 0.99;
                }
            }
            NormalizeReaction(fallbackResult);
            return fallbackResult;
        }

        public string BuildStaticPrompt(
            string agentName,
            string tonePref,
            string targetAud,
            string approvedKnowledgeBaseText)
        {
            var systemPrompt = SystemPromptTemplate;

            var resolvedTonePref = !string.IsNullOrEmpty(tonePref) ? tonePref : "العامية المصرية الروشة والصايعة";
            var resolvedTargetAud = !string.IsNullOrEmpty(targetAud) ? targetAud : "طلاب كورس كول سنتر يبحثون عن عمل";

            systemPrompt = systemPrompt.Replace("[AGENT_NAME]", agentName);
            systemPrompt = systemPrompt.Replace("[TONE_PREFERENCE]", resolvedTonePref);
            systemPrompt = systemPrompt.Replace("[TARGET_AUDIENCE]", resolvedTargetAud);

            var tonePrefLower = resolvedTonePref.ToLower();
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
                                 "- NEVER use street slang or overly casual connectors like \"بص يا سيدي\", \"من عيوني\", \"من الآخر\", \"سكة ودغري\", \"يا صاحبي\", \"هندلعك\", \"في جيبك\".\n" +
                                 "- NEVER address the customer directly using an informal name call combined with a polite title (e.g. do NOT say \"يا مارفن يا فندم\"). If addressing the customer by name, always use a polite prefix (e.g. \"أستاذ مارفن\") or simply address them as \"يا فندم\" or \"حضرتك\" without their name.\n" +
                                 "- Build trust and rapport without being overly casual. Use respectful terms like \"يا فندم\", \"حضرتك\", \"يسعدنا جداً\".";
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

            if (!string.IsNullOrEmpty(approvedKnowledgeBaseText))
            {
                systemPrompt += $"\n\nUse the following reference knowledge base information to accurately answer the customer's questions if applicable:\n{approvedKnowledgeBaseText}\n\nDo not invent facts outside this reference information if it contains pricing, shipping, or policies.";
            }

            return systemPrompt;
        }

        public async Task<string> RewriteFollowUpNotesAsync(
            string customerName,
            string notes,
            bool hasAttended,
            string? tone = null,
            string? apiKeyOverride = null,
            string? modelOverride = null)
        {
            var toneInstructions = "";
            var resolvedTone = tone?.ToLower() ?? "default";
            if (resolvedTone == "creative")
            {
                toneInstructions = "- أسلوب الرسالة يجب أن يكون إبداعي ومبتكر وجذاب ولطيف للغاية، يترك انطباعاً رائعاً لدى العميل.\n";
            }
            else if (resolvedTone == "salesy")
            {
                toneInstructions = "- أسلوب الرسالة يجب أن يكون سلزجي صايع، ذكي ومقنع ومحفز جداً، يركز على إظهار الفوائد وحث العميل على اتخاذ القرار وإتمام الحجز والدفع بذكاء ودهاء.\n" +
                                   "- استخدم مصطلحات شعبية وودية جداً مثل \"يا غالي\"، \"يا صديقي\"، \"يا صاحبي\"، \"يا باشا\"، \"من الآخر\"، \"سكة ودغري\"، \"حاجة عظمة\".\n";
            }
            else
            {
                toneInstructions = "- لا تستخدم تعبيرات عامية غير رسمية أو شعبية مثل \"بص يا سيدي\" أو \"من عيوني\" أو \"يا غالي\" أو \"يا صاحبي\".\n";
            }

            var attendanceInstructions = "";
            if (hasAttended)
            {
                attendanceInstructions = "تنبيه هام جداً: العميل (الطالب) حضر بالفعل الحصة/المجموعة. يجب أن تعكس الرسالة ترحيباً بحضوره والمتابعة معه بناءً على حضور الجلسة وتتمنى له التوفيق.\n";
            }

            var prompt = $@"أنت مساعد ذكاء اصطناعي محترف.
لديك ملاحظة متابعة داخلية لعميل اسمه: ""{customerName}"".
ملاحظة المتابعة الداخلية هي: ""{notes}""

{attendanceInstructions}
قم بصياغة رسالة واتساب قصيرة وودية ومباشرة باللغة العربية (اللهجة المصرية الودية والمهنية) موجهة مباشرة للعميل بناءً على هذه الملاحظة.
- يجب أن تكون الرسالة موجهة مباشرة للعميل بصيغة المخاطب (مثال: ""يا فندم""، ""حضرتك"").
- لا تخلط أبداً بين النداء غير الرسمي والنداء الرسمي (مثال: لا تقل ""يا مارفن يا فندم""، بل قل ""يا فندم"" أو ""أستاذ مارفن"").
{toneInstructions}- لا تذكر أبداً اسم الموظف أو ملاحظات إدارية.
- لا تضع أي توقيع أو علامات ترقيم زائدة.
- اكتب نص الرسالة فقط التي سيتم إرسالها للعميل مباشرة وبدون أي مقدمات أو شرح خارجي.
الرسالة:";

            var generatedMessage = await _geminiClient.GenerateReplyAsync(prompt, apiKeyOverride, modelOverride);
            
            if (!string.IsNullOrWhiteSpace(generatedMessage) && !generatedMessage.StartsWith("[Mock"))
            {
                return generatedMessage.Trim();
            }
            else
            {
                return "مرحباً يا فندم، كنا حابين نتابع مع حضرتك بخصوص ميعاد المجموعة الأونلاين والسيشن التجريبية.";
            }
        }
    }
}
