using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;

namespace Modules.AI.Services;

internal static class CourseDiscoveryPricingPolicy
{
    public const string Instructions = """
        CURRENT COURSE DISCOVERY RULE — applies to BOTH regular and intensive English courses, on Messenger and WhatsApp, and overrides older cached sales instructions:
        During the FIRST FIVE delivered automated messages, do not volunteer COURSE prices, fees, installments, discounts, or payment terms. Approved facts about job salaries are not course fees and may be discussed when relevant to the customer's work goal, without guaranteeing employment. An explicit question about price/cost/payment in the CURRENT customer message or voice transcription is an exception: answer it directly with verified information, even in the first reply. 'Details', 'benefits', 'I want to book', or 'when is the session?' alone are not requests for price. If a delivered-message count is unavailable, assume the early discovery phase.
        Start with relevant verified benefits. Ask one natural question about WHY the customer wants to learn English if their goal is not already known. Use this as the discovery CTA until answered; do not append a competing phone/city/booking question to the same reply. After they explain, connect specific approved course benefits to THAT goal (work/interviews, study, travel, or confident conversation). Do not repeat the goal question, invent a motivation, use fear/shame, or promise guaranteed employment/results. Answer their direct question first; do not turn every reply into a sales pitch. During discovery, suggested follow-up notes must also emphasize benefits and the stated goal without unsolicited pricing.
        After five messages, price may be discussed when relevant; it is not mandatory in reply six. A broad course overview must emphasize benefits and study format, not automatically include pricing. Never withhold a price the customer explicitly requests.
        """;

    private static readonly Regex FinancialTerms = new(
        @"سعر|أسعار|اسعار|ثمن|تكلف[ةه]|مصاريف|رسوم|أقساط|اقساط|تقسيط|قسط|كاش|جنيه|دولار|ريال|درهم|خصم|اشتراك\s+شهري|\b(?:price|cost|fee|fees|installments?|discount|cash|tuition)\b|EGP|USD|[$€£]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex KnownOrDeclinedPrice = new(
        @"(?:عرفت|عارف|عارفه|عارفة|فهمت)\s+(?:ال)?(?:سعر|أسعار|اسعار|تكلف[ةه])\S*|(?:السعر|الفلوس|التكلفة)\s+مش\s+مهم[ةه]?|(?:من غير|بدون|مش عايز|مش عاوز|مش محتاج|بلاش)\s+(?:ال)?(?:سعر|أسعار|اسعار|تكلف[ةه]|فلوس)\S*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SalaryTerms = new(@"مرتب|مراتب|راتب|رواتب|قبض|salary|salaries|income",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CourseCharges = new(@"سعر|اسعار|أسعار|تكلف|رسوم|مصاريف|قسط|اقساط|أقساط|خصم|اشتراك|price|tuition|fee|installment|discount",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsCourseContext(string? context) => context is not null &&
        (context.Contains("كورس", StringComparison.Ordinal) || context.Contains("course", StringComparison.OrdinalIgnoreCase));

    public static Task<int> DeliveredMessageCountAsync(AppDbContext db, Guid projectId, Guid customerId, DateTime? sourceTimestamp) =>
        (from message in db.Messages.IgnoreQueryFilters()
         join conversation in db.Conversations.IgnoreQueryFilters() on message.ConversationId equals conversation.Id
         where conversation.ProjectId == projectId && conversation.CustomerId == customerId
            && (conversation.Channel == "Messenger" || conversation.Channel == "WhatsApp")
            && message.Direction == "Outgoing" && message.MessageType != "Reaction"
            && (message.SenderType == "AI" || message.SenderType == "System")
            && (!sourceTimestamp.HasValue || message.Timestamp <= sourceTimestamp.Value)
         select message.Id).CountAsync();

    public static string Apply(string reply, string customerMessage, int deliveredMessages)
    {
        if (deliveredMessages >= 5 || PricingGuard.IsPricingQuestion(KnownOrDeclinedPrice.Replace(customerMessage, ""))) return reply;
        if (!FinancialTerms.IsMatch(reply)) return reply;
        var paragraphs = reply.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);
        var retained = paragraphs.Select(paragraph => string.Join(" ",
            Regex.Split(paragraph, @"(?<=[.!?؟؛])\s+|\r?\n")
                .Where(sentence => !FinancialTerms.IsMatch(sentence)
                    || (SalaryTerms.IsMatch(sentence) && !CourseCharges.IsMatch(sentence)))));
        var parts = ReplyMessageParts.SplitParagraphs(string.Join("\n\n", retained));
        if (parts.All(ReplyMessageParts.IsStandaloneSignature))
            return "إيه هدف حضرتك من تعلم الإنجليزي عشان أوضحلك المميزات المناسبة ليه؟";
        return string.Join("\n\n", parts);
    }
}
