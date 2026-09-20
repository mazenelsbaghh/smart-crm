using System.Text.RegularExpressions;
using Modules.Conversations.Domain;
using Modules.Conversations.Services;
using Modules.GroupAppointments.Services;

namespace Modules.AI.Services;

internal static class AiRequesterBookingPhone
{
    public static string? FromConversation(IReadOnlyCollection<Message> history, string latestContent)
    {
        string? suppliedPhone = null;
        string? precedingReply = null;
        foreach (var message in history.Where(message => message.MessageType != "Reaction")
                     .OrderBy(message => message.Timestamp).ThenBy(message => message.Id))
        {
            var content = message.Transcription ?? message.Content;
            if (message.Direction == "Outgoing")
                precedingReply = content;
            else if (message.Direction == "Incoming")
                suppliedPhone = ExplicitRequesterPhone(content, precedingReply) ?? suppliedPhone;
        }
        return ExplicitRequesterPhone(latestContent, precedingReply) ?? suppliedPhone;
    }

    public static void Apply(
        string? knownBookingPhone,
        MarketingAnalysisResult analysis,
        IReadOnlyCollection<string> customerMessages)
    {
        // A repeated request must stop automation instead of trusting another model-generated promise.
        if (GroupBookingPhone.Normalize(knownBookingPhone) != null && IsRequesterPhoneQuestion(analysis.ReplyContent))
        {
            analysis.RequestHuman = true;
            analysis.SuggestedGroupBookingId = null;
            if (analysis.SuggestedFollowUp != null) analysis.SuggestedFollowUp.Needed = false;
            return;
        }
        if (string.IsNullOrWhiteSpace(analysis.SuggestedGroupBookingId)) return;
        var requesters = analysis.SuggestedGroupBookingPeople.Where(person => person.IsRequester).ToArray();
        if (analysis.SuggestedGroupBookingPeople.Length > 0 && requesters.Length == 0) return;

        if (requesters.Length == 0)
        {
            requesters = [new SuggestedGroupBookingPerson { IsRequester = true }];
            analysis.SuggestedGroupBookingPeople = requesters;
        }
        foreach (var requester in requesters)
            requester.PhoneNumber = GroupBookingPhone.Normalize(knownBookingPhone)
                ?? SuppliedPhone(requester.PhoneNumber, customerMessages);

        if (requesters.All(person => person.PhoneNumber != null)) return;
        analysis.SuggestedGroupBookingId = null;
        analysis.ReplyContent = "محتاج رقم الموبايل اللي تحب تسجل بيه الحجز. ممكن تبعته؟";
        if (analysis.SuggestedFollowUp?.Type == "AppointmentReminder")
            analysis.SuggestedFollowUp.Needed = false;
    }

    private static string? ExplicitRequesterPhone(string content, string? precedingReply)
    {
        // Only an explicit self-reference or an answer to a requester-phone question identifies its owner.
        var selfReference = Regex.Match(content.Trim(), @"^(?:رقمي|رقمى|موبايلي|موبايلى|تليفوني|تليفونى|my (?:phone|number))\s*[:：-]?\s*(.+)$", RegexOptions.IgnoreCase);
        if (!selfReference.Success && (precedingReply == null || !IsRequesterPhoneQuestion(precedingReply))) return null;
        var phoneText = selfReference.Success ? selfReference.Groups[1].Value : content;
        var normalized = GroupBookingPhone.Normalize(phoneText);
        return normalized != null && EgyptianPhoneNumber.Extract(phoneText) == normalized ? normalized : null;
    }

    private static bool IsRequesterPhoneQuestion(string content)
    {
        var normalized = Regex.Replace(content, @"[\u064B-\u065F\u0670\u0640]", "")
            .Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا');
        const string phoneReference = @"(?:رقمك|رقم\s+(?:موبايلك|تليفونك|هاتفك|واتسابك|الموبايل|الهاتف)|your\s+(?:phone|mobile|number))";
        return Regex.IsMatch(normalized,
            @"(?:ابعت|تبعت|ارسل|ترسل|محتاج|نحتاج|ممكن|ما هو|send|provide|need|what is)[^.؟?\n،]{0,80}" + phoneReference
            + "|" + phoneReference + @"\s*(?:ايه|مش ظاهر|غير ظاهر)", RegexOptions.IgnoreCase);
    }

    private static string? SuppliedPhone(string? suggestedPhone, IReadOnlyCollection<string> customerMessages)
    {
        var normalized = GroupBookingPhone.Normalize(suggestedPhone);
        return normalized != null && customerMessages.Any(message =>
            EgyptianPhoneNumber.Extract(message) == normalized)
            ? normalized
            : null;
    }
}
