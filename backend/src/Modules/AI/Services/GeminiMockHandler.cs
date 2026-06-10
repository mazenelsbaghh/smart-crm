using System;
using System.Text.Json;

namespace Modules.AI.Services
{
    public interface IGeminiMockHandler
    {
        bool IsMockKey(string? apiKey);
        string GenerateMockReply(string messageContent, string? apiKey);
        string GenerateMockReply(string messageContent, byte[] fileBytes, string mimeType, string? apiKey, string? model);
    }

    public class GeminiMockHandler : IGeminiMockHandler
    {
        public bool IsMockKey(string? apiKey)
        {
            return string.IsNullOrEmpty(apiKey) || apiKey == "your_gemini_api_key_here" || apiKey.StartsWith("mock_");
        }

        public string GenerateMockReply(string messageContent, string? apiKey)
        {
            if (apiKey != null && apiKey.StartsWith("mock_json_"))
            {
                return apiKey.Substring("mock_json_".Length);
            }

            if (messageContent.Contains("JSON format") || messageContent.Contains("JSON") || messageContent.Contains("\"intent\""))
            {
                // Check if it's the Customer Memory Extraction / Profile generation prompt
                if (messageContent.Contains("Analyze the following WhatsApp conversation"))
                {
                    string transcriptPart = messageContent;
                    int transcriptIdx = messageContent.IndexOf("Conversation Transcript:");
                    if (transcriptIdx != -1)
                    {
                        transcriptPart = messageContent.Substring(transcriptIdx);
                    }

                    string? profileName = null;
                    if (transcriptPart.Contains("اسمي أدهم") || transcriptPart.Contains("معاك أدهم") || transcriptPart.Contains("أدهم مدبولي"))
                    {
                        profileName = "أدهم مدبولي";
                    }
                    else if (transcriptPart.Contains("اسمي أحمد") || transcriptPart.Contains("معاك أحمد") || transcriptPart.Contains("أحمد"))
                    {
                        profileName = "أحمد";
                    }
                    else if (transcriptPart.Contains("اسمي محمد") || transcriptPart.Contains("معاك محمد") || transcriptPart.Contains("محمد"))
                    {
                        profileName = "محمد";
                    }
                    string profileCity = "القاهرة";
                    decimal profileBudget = 1500;
                    int profileLeadScore = 85;
                    string profilePipelineStage = "Proposal";
                    string profileLabel = "استفسار عام";
                    string profileSummary = "عميل مهتم بالتسجيل في الدورة ويبحث عن تفاصيل الأسعار وتسهيلات الدفع ويعيش في القاهرة.";
                    string profileFactsJson = "[\"مهتم بالدورة المكثفة\", \"يفضل التواصل واتساب\", \"يعيش في القاهرة\"]";
                    if (transcriptPart.Contains("email") || transcriptPart.Contains("Email"))
                    {
                        profileFactsJson = "[\"Prefers contact via email\", \"مهتم بالدورة المكثفة\", \"يفضل التواصل واتساب\", \"يعيش في القاهرة\"]";
                    }
                    string profileTriggersJson = "[\"خصم لفترة محدودة\", \"البدء الفوري\"]";
                    string profileObjectionsJson = "[\"السعر مرتفع قليلاً\"]";
                    if (transcriptPart.Contains("expensive") || transcriptPart.Contains("price") || transcriptPart.Contains("Price") || transcriptPart.Contains("Expensive"))
                    {
                        profileObjectionsJson = "[\"Price sensitive / Objections about cost\"]";
                    }

                    if (transcriptPart.Contains("الإسكندرية") || transcriptPart.Contains("اسكندرية"))
                    {
                        profileCity = "الإسكندرية";
                        profileSummary = "عميل مهتم بالتسجيل في الدورة ويبحث عن تفاصيل الأسعار وتسهيلات الدفع ويعيش في الإسكندرية.";
                        profileFactsJson = "[\"مهتم بالدورة المكثفة\", \"يفضل التواصل واتساب\", \"يعيش في الإسكندرية\"]";
                    }
                    else if (transcriptPart.Contains("الجيزة") || transcriptPart.Contains("جيزة"))
                    {
                        profileCity = "الجيزة";
                        profileSummary = "عميل مهتم بالتسجيل في الدورة ويبحث عن تفاصيل الأسعار وتسهيلات الدفع ويعيش في الجيزة.";
                        profileFactsJson = "[\"مهتم بالدورة المكثفة\", \"يفضل التواصل واتساب\", \"يعيش في الجيزة\"]";
                    }

                    if (transcriptPart.Contains("سعر") || transcriptPart.Contains("بكام"))
                    {
                        profileLabel = "استفسار عن السعر";
                    }
                    else if (transcriptPart.Contains("تفاصيل"))
                    {
                        profileLabel = "استفسار عن التفاصيل";
                    }
                    else if (transcriptPart.Contains("حجز") || transcriptPart.Contains("احجز") || transcriptPart.Contains("سجل"))
                    {
                        profileLabel = "طلب حجز";
                    }
                    else if (transcriptPart.Contains("شحن") || transcriptPart.Contains("توصيل"))
                    {
                        profileLabel = "استفسار عن الشحن";
                    }
                    else if (messageContent.Contains("شكوى") || messageContent.Contains("مشكلة"))
                    {
                        profileLabel = "شكوى";
                    }

                    return $@"{{
  ""facts"": {profileFactsJson},
  ""triggers"": {profileTriggersJson},
  ""objections"": {profileObjectionsJson},
  ""summary"": ""{profileSummary}"",
  ""name"": {(profileName == null ? "null" : $"\"{profileName}\"")},
  ""city"": ""{profileCity}"",
  ""budget"": {profileBudget},
  ""leadScore"": {profileLeadScore},
  ""pipelineStage"": ""{profilePipelineStage}"",
  ""label"": ""{profileLabel}""
}}";
                }

                // Extract customer message to formulate a context-appropriate mock reply
                string customerMessage = "";
                int msgIdx = messageContent.LastIndexOf("Customer Message: \"");
                if (msgIdx != -1)
                {
                    customerMessage = messageContent.Substring(msgIdx + "Customer Message: \"".Length).Trim().TrimEnd('"');
                }

                string replyContent = "أهلاً بك! كيف يمكنني مساعدتك اليوم؟";
                string intent = "greeting";
                string label = "ترحيب";
                string? city = null;

                if (customerMessage.Contains("سعر") || customerMessage.Contains("بكام"))
                {
                    intent = "inquiry";
                    label = "استفسار عن السعر";
                    replyContent = "بالتأكيد! تفاصيل السعر هي 500 جنيه مصري، وهناك خصم خاص لفترة محدودة. هل تحب تأكيد الطلب؟";
                }
                else if (customerMessage.Contains("تفاصيل"))
                {
                    intent = "inquiry";
                    label = "استفسار عن التفاصيل";
                    replyContent = "بالتأكيد! تفاصيل الكورس هي كالتالي: الكورس مكثف ويغطي أساسيات الذكاء الاصطناعي وبناء التطبيقات. هل تحب معرفة المزيد؟";
                }
                else if (customerMessage.Contains("شحن") || customerMessage.Contains("facebook.com") || customerMessage.Contains("share"))
                {
                    intent = "inquiry";
                    label = "استفسار";
                    replyContent = "أهلاً بك! سعر الشحن يختلف حسب محافظتك. هل تحب تفاصيل أكثر؟";
                }
                else if (customerMessage.Contains("مشكلة") || customerMessage.Contains("شكوى") || customerMessage.Contains("بطيء"))
                {
                    intent = "complaint";
                    label = "شكوى";
                    replyContent = "نعتذر بشدة عن أي إزعاج. يرجى تزويدنا بالتفاصيل لنقوم بحل المشكلة فوراً.";
                }
                else if (customerMessage.Contains("قاهرة") || customerMessage.Contains("القاهرة"))
                {
                    intent = "inquiry";
                    label = "استفسار";
                    city = "القاهرة";
                    replyContent = "أهلاً بأهل القاهرة! نوصل للقاهرة خلال 24 ساعة.";
                }
                else if (messageContent.Contains("City: Missing"))
                {
                    intent = "inquiry";
                    label = "استفسار";
                    replyContent = "أهلاً بك! تشرفنا بحضرتك يا فندم. ممكن نعرف حضرتك بتكلمنا من أي مدينة؟";
                }

                // Dynamically generate context-aware mock follow-up information
                string dueDateStr = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
                string apptTimeStr = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ");
                bool followUpNeeded = true;
                string followUpType = "Nurturing";
                string followUpNotes = "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟";

                if (customerMessage.Contains("حجز") || customerMessage.Contains("احجز") || customerMessage.Contains("سجل") || customerMessage.Contains("تسجيل") || customerMessage.Contains("موعد"))
                {
                    followUpType = "AppointmentReminder";
                    followUpNotes = "تذكير: موعد كورس الذكاء الاصطناعي غداً في تمام الساعة السابعة مساءً بتوقيت القاهرة. ننتظرك!";
                    dueDateStr = DateTime.UtcNow.AddHours(23).ToString("yyyy-MM-ddTHH:mm:ssZ");
                }
                else if (customerMessage.Contains("سعر") || customerMessage.Contains("بكام"))
                {
                    followUpNotes = "مرحباً يا فندم! كنا اتكلمنا بخصوص الأسعار، هل حابب تستفيد من الخصم المتاح اليوم؟";
                }
                else if (customerMessage.Contains("شحن") || customerMessage.Contains("توصيل"))
                {
                    followUpNotes = "يا فندم بخصوص الشحن، هل تحب نأكد الطلب للشحن غداً؟";
                }

                return $@"{{
  ""intent"": ""{intent}"",
  ""sentiment"": ""positive"",
  ""replyStyle"": ""Casual"",
  ""label"": ""{label}"",
  ""pipelineStage"": ""New"",
  ""entities"": {{
    ""city"": {(city == null ? "null" : $"\"{city}\"")},
    ""interests"": [],
    ""timeline"": null
  }},
  ""replyContent"": ""{replyContent}"",
  ""confidence"": 0.99,
  ""suggestedFollowUp"": {{
    ""needed"": {followUpNeeded.ToString().ToLower()},
    ""type"": ""{followUpType}"",
    ""appointmentTime"": {(followUpType == "AppointmentReminder" ? $"\"{apptTimeStr}\"" : "null")},
    ""dueDate"": ""{dueDateStr}"",
    ""notes"": ""{followUpNotes}""
  }}
}}";
            }

            return $"[Mock Gemini Reply] Re: {messageContent}";
        }

        public string GenerateMockReply(string messageContent, byte[] fileBytes, string mimeType, string? apiKey, string? model)
        {
            if (apiKey != null && apiKey.StartsWith("mock_json_"))
            {
                return apiKey.Substring("mock_json_".Length);
            }

            // Voice Note Transcription Mock Check
            if (mimeType.StartsWith("audio/") && (messageContent.Contains("Voice") || messageContent.Contains("voice") || messageContent.Contains("transcribe") || messageContent.Contains("Transcribe")))
            {
                return @"{
  ""intent"": ""inquiry"",
  ""sentiment"": ""neutral"",
  ""replyStyle"": ""Casual"",
  ""label"": ""استفسار"",
  ""pipelineStage"": ""Contacted"",
  ""entities"": {
    ""city"": null,
    ""interests"": [""كورس الذكاء الاصطناعي""],
    ""timeline"": null
  },
  ""replyContent"": ""أهلاً بك! سعر كورس الذكاء الاصطناعي هو 500 جنيه مصري وهناك خصم لفترة محدودة. هل تود حجز مقعدك؟"",
  ""confidence"": 0.95,
  ""transcription"": ""أنا مهتم بكورس الذكاء الاصطناعي وبدي أعرف السعر""
}";
            }

            // Image/Receipt Analysis Mock Check
            if (mimeType.StartsWith("image/"))
            {
                return @"{
  ""intent"": ""purchase"",
  ""sentiment"": ""positive"",
  ""replyStyle"": ""Sales"",
  ""label"": ""طلب شراء"",
  ""pipelineStage"": ""Qualified"",
  ""entities"": {
    ""city"": ""القاهرة"",
    ""budget"": 50,
    ""interests"": [],
    ""timeline"": null
  },
  ""replyContent"": ""شكراً لإرسال الإيصال! لقد تم استلام مبلغ 50 دولار وتحديث ميزانيتك إلى القاهرة. جاري مراجعة الطلب."",
  ""confidence"": 0.95
}";
            }

            // Default text fallback mock
            return GenerateMockReply(messageContent, apiKey);
        }
    }
}
