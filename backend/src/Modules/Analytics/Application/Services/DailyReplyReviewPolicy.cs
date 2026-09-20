using System.Text.RegularExpressions;
using Modules.Analytics.Application;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;

namespace Modules.Analytics.Application.Services;

internal sealed record ConversationReplyTiming(
    int IncomingCount, int OutgoingCount, double? LongestResponseMinutes,
    DateTime? WaitingSinceUtc, IReadOnlyList<ReplyReviewIssue> Issues);

internal static class DailyReplyReviewPolicy
{
    internal const int LateAfterMinutes = 5;

    public static ConversationReplyTiming Evaluate(IReadOnlyList<Message> messages, DateTime evaluatedAtUtc)
    {
        var ordered = messages.Where(message => message.MessageType != "Reaction")
            .OrderBy(message => message.Timestamp).ThenBy(message => message.Id).ToArray();
        var issues = new List<ReplyReviewIssue>();
        var repeats = RepeatedReplies(ordered);
        if (repeats.Count > 0) issues.Add(new("RepeatedReply", "رد متكرر يحتاج مراجعة", "Messages", repeats));
        var timing = ResponseTiming(ordered);
        if (timing.LongestMinutes > LateAfterMinutes)
            issues.Add(new("SlowReply", "رد بعد أكثر من ٥ دقائق", "Messages", timing.SlowMessageIds));
        if (timing.WaitingSince.HasValue && evaluatedAtUtc - timing.WaitingSince.Value > TimeSpan.FromMinutes(LateAfterMinutes))
            issues.Add(new("Unanswered", "رسالة تنتظر الرد", "Messages", ordered.Where(m => m.Direction == "Incoming").TakeLast(1).Select(m => m.Id).ToArray()));
        return new(ordered.Count(m => m.Direction == "Incoming"), ordered.Count(m => m.Direction == "Outgoing"),
            timing.LongestMinutes, timing.WaitingSince, issues);
    }

    private static IReadOnlyList<Guid> RepeatedReplies(IEnumerable<Message> messages)
    {
        return messages.Where(message => message.Direction == "Outgoing" && message.SenderType is "AI" or "System")
            .GroupBy(message => NormalizeReply(message.Content))
            .Where(group => group.Key.Length >= 12 && group.Count() > 1)
            .SelectMany(group => group.Take(3).Select(message => message.Id)).Take(12).ToArray();
    }

    private static string NormalizeReply(string content) => Regex.Replace(content.Trim().ToLowerInvariant(),
        @"[^\p{L}\p{N}]+", " ").Trim();

    private static (double? LongestMinutes, DateTime? WaitingSince, Guid[] SlowMessageIds) ResponseTiming(IEnumerable<Message> messages)
    {
        Message? pending = null;
        double? longest = null;
        Guid[] slowMessages = [];
        foreach (var message in messages)
        {
            if (message.Direction == "Incoming") { pending ??= message; continue; }
            if (pending == null || message.Direction != "Outgoing") continue;
            var minutes = (message.Timestamp - pending.Timestamp).TotalMinutes;
            if (!longest.HasValue || minutes > longest.Value)
            {
                longest = minutes;
                slowMessages = [pending.Id, message.Id];
            }
            pending = null;
        }
        return (longest, pending?.Timestamp, slowMessages);
    }

    public static string FollowUpHealth(FollowUp followUp, DateTime nowUtc) => followUp.Status switch
    {
        "DeliveryUnknown" => "Unknown",
        "Completed" or "Done" => followUp.SentAtUtc.HasValue && followUp.SentMessageId.HasValue ? "Sent" : "Unverified",
        "Cancelled" or "Bypassed" => "Stopped",
        "Missed" or "Failed" => "Failed",
        "Pending" or "Processing" when followUp.DueDate < nowUtc.AddMinutes(-LateAfterMinutes) => "Overdue",
        _ => "Scheduled"
    };
}
