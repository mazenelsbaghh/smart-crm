using System.Text.Json;
using Modules.Analytics.Application;
using Modules.Analytics.Domain;
using Modules.Conversations.Domain;

namespace Modules.Analytics.Application.Services;

internal static class ReplyReviewResolutionPolicy
{
    public static bool HasResolutionEvidence(ConversationSalesAnalysis analysis, IReadOnlyCollection<Message> messages, DateTime? afterUtc)
    {
        if (analysis.HasUnresolvedReplyIssue != false || analysis.Confidence < 0.75m) return false;
        var evidence = JsonSerializer.Deserialize<AnalysisEvidence[]>(analysis.EvidenceJson) ?? [];
        return evidence.Any(proof => messages.Any(message => message.Id == proof.MessageId
            && message.Direction == "Outgoing" && message.MessageType != "Reaction"
            && (!afterUtc.HasValue || message.Timestamp > afterUtc.Value)
            && (message.Transcription ?? message.Content).Contains(proof.Quote, StringComparison.OrdinalIgnoreCase)));
    }
}
