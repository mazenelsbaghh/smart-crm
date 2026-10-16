using System.Text.RegularExpressions;
using Shared.Infrastructure;

namespace Modules.AI.Services;

internal static class BookedAppointmentReplyPolicy
{
    public const string ContextMarker = "CURRENT_BOOKING_MUST_BE_PRESERVED";
    public const string Instructions = """
        CURRENT BOOKED CUSTOMER RULE — overrides cached instructions:
        This customer already has a booking. Answer questions using their existing booking. Never volunteer another appointment, offer to change/reschedule, or ask whether they want another time. Do not cancel or transfer their booking unless the customer requests it. Only discuss other available times when the customer explicitly asks for other times or a change. Asking 'when is my appointment?', asking a course question, or booking a friend is not permission to change the requester's booking. A request for other times authorizes showing options; wait for the customer to choose the new appointment before suggesting a transfer. If they ask to book the same appointment, confirm that it is already booked without proposing changes. A direct request such as 'عايزه المعاد 6 بدلا من 10' explicitly requests a new appointment; it is not an unsolicited change.
        """;

    private static readonly Regex AlternativeRequest = new(
        @"(?:معاد|ميعاد|موعد|مواعيد|وقت|يوم)\s+(?:تاني|تانيه|اخر|اخري|مختلف|غير)|(?:اغير|نغير|غيرلي|تغير|تغيير|اعدل|نعدل|تعديل|انقل|نقل|ابدل|بدل).{0,30}(?:حجز|معاد|ميعاد|موعد|مجموع|وقت|يوم)|(?:حجز|معاد|ميعاد|موعد|وقت).{0,25}(?:تاني|تانيه|اخر|اخري|مختلف)|\b(?:reschedule|another time|other times|different time|change.{0,25}(?:booking|appointment|time))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ReplacementTimeRequest = new(
        @"(?:معاد|ميعاد|موعد|وقت|ساعه).{0,30}[0-9٠-٩]{1,2}(?::[0-9٠-٩]{2})?\s*بدل(?:ا)?\s*(?:من\s*)?[0-9٠-٩]{1,2}",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DeclinedChange = new(
        @"(?:مش عايز|مش عاوز|مش حابب|متغيرش|ما تغيرش|مش هنغير|لا تغير|لا تعدل).{0,30}(?:غير|عدل|حجز|معاد|ميعاد|موعد)|\b(?:don't|do not)\s+(?:change|reschedule)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CancellationRequest = new(
        @"(?:الغي|الغاء).{0,25}(?:حجز|موعد|معاد|ميعاد)|\bcancel.{0,25}(?:booking|appointment)\b|^الغيه?[.!؟?]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool RequestsCancellation(string customerMessage)
    {
        var normalized = Normalize(customerMessage).Trim();
        if (Regex.IsMatch(normalized, @"\b(?:مش عايز|مش عاوز|لا|متلغي|متلغيش|ما تلغي|ما تلغيش)\b.{0,20}(?:الغي|الغاء|حجز)")) return false;
        return CancellationRequest.IsMatch(normalized);
    }

    public static bool AllowsOtherTimes(string currentMessage, IEnumerable<string> recentRequests)
    {
        var allowed = false;
        foreach (var message in recentRequests.TakeLast(5).Append(currentMessage))
        {
            if (DeclinedChange.IsMatch(Normalize(message))) allowed = false;
            else if (RequestsOtherTimes(message)) allowed = true;
        }
        return allowed;
    }

    private static bool RequestsOtherTimes(string message)
    {
        var normalized = Normalize(message);
        return AlternativeRequest.IsMatch(normalized) || ReplacementTimeRequest.IsMatch(normalized);
    }

    public static bool AllowsTransfer(string currentMessage, IEnumerable<string> recentRequests)
    {
        if (!AllowsOtherTimes(currentMessage, recentRequests)) return false;
        var normalized = Normalize(currentMessage).Trim();
        return ReplacementTimeRequest.IsMatch(normalized)
            && Regex.IsMatch(normalized, @"(?:عايزه?|عاوزه?|محتاجه?|احجز|خلي|سجلني)\s+(?:ال)?(?:معاد|ميعاد|موعد|وقت|ساعه)")
            || Regex.IsMatch(normalized,
            @"احجز|سجلني|انقلني|غيرلي|(?:الاحد|الاثنين|الثلاثاء|الاربعاء|الخميس|الجمعه|السبت)|^\s*(?:ايوه|تمام|موافق|اوك|نعم|yes|ok|okay)\s*[.!]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static string PreserveBooking(string reply)
    {
        var retained = reply.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(paragraph => string.Join("\n", Regex.Split(paragraph, @"(?<=[.!?؟؛،])\s+|\r?\n")
                .Where(sentence => !AlternativeRequest.IsMatch(Normalize(sentence)))));
        var paragraphs = ReplyMessageParts.SplitParagraphs(string.Join("\n\n", retained));
        return paragraphs.Count == 0 || paragraphs.All(ReplyMessageParts.IsStandaloneSignature)
            ? "حجز حضرتك الحالي ثابت زي ما هو."
            : string.Join("\n\n", paragraphs);
    }

    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant()
        .Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ة', 'ه').Replace('ى', 'ي'),
        "[\\u064B-\\u065F\\u0670]", "");
}
