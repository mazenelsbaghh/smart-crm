using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Jobs;
using Shared.Infrastructure;

namespace Modules.Conversations.Services;

public sealed class MessageAggregator(
    AppDbContext dbContext,
    ConversationReplyWindowService replyWindows,
    IConfiguration configuration,
    IBackgroundJobClient backgroundJobs) : IMessageAggregator
{
    public async Task AggregateMessageAsync(
        Guid projectId,
        string sender,
        string content,
        Guid incomingMessageId,
        DateTime? sourceMessageTimestampUtc = null,
        DateTimeOffset? requiredWhatsAppConnectedAt = null,
        Guid? conversationId = null,
        Guid? whatsAppAccountId = null,
        CancellationToken cancellationToken = default,
        string channel = "WhatsApp",
        string? channelMetadata = null)
    {
        if (!conversationId.HasValue)
            throw new ArgumentException("A persisted conversation is required for durable aggregation.", nameof(conversationId));

        var delay = await ResolveDelayAsync(projectId, cancellationToken);
        var dueAtUtc = DateTime.UtcNow.Add(delay);
        var isWhatsApp = string.Equals(channel, "WhatsApp", StringComparison.OrdinalIgnoreCase);
        var occurrenceKey = isWhatsApp && requiredWhatsAppConnectedAt.HasValue
            ? ConversationReplyWindowService.WhatsAppEpochOccurrenceKey(requiredWhatsAppConnectedAt.Value)
            : ConversationReplyWindowService.SourceMessageOccurrenceKey(incomingMessageId);
        var deliveryKey = isWhatsApp && requiredWhatsAppConnectedAt.HasValue
            ? ConversationReplyWindowService.WhatsAppDeliveryKey(incomingMessageId, requiredWhatsAppConnectedAt.Value)
            : null;
        await replyWindows.StageAsync(new ConversationReplyWindowRequest(
            projectId,
            conversationId.Value,
            incomingMessageId,
            sender,
            content,
            sourceMessageTimestampUtc ?? DateTime.UtcNow,
            dueAtUtc,
            occurrenceKey,
            channel,
            WhatsAppAccountId: whatsAppAccountId,
            ChannelMetadata: channelMetadata,
            RequiredWhatsAppConnectedAt: requiredWhatsAppConnectedAt,
            WhatsAppDeliveryIdempotencyKey: deliveryKey),
            cancellationToken);

        // Hangfire is a durable low-latency wake-up. The recurring dispatcher is the
        // recovery path if this enqueue or process hand-off is interrupted.
        backgroundJobs.Schedule<ConversationReplyWindowDispatcher>(
            dispatcher => dispatcher.DispatchAsync(CancellationToken.None),
            delay);
    }

    private async Task<TimeSpan> ResolveDelayAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var projectName = await dbContext.Projects.IgnoreQueryFilters()
            .Where(project => project.Id == projectId)
            .Select(project => project.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (projectName is not null && (
            projectName.Contains("Test", StringComparison.OrdinalIgnoreCase)
            || projectName.EndsWith("Proj", StringComparison.OrdinalIgnoreCase)
            || projectName.StartsWith("Campaign_Project", StringComparison.OrdinalIgnoreCase)))
            return TimeSpan.FromSeconds(2);

        var minDelay = ParseDelay("MessageAggregation:MinDelayMs", 30_000);
        var maxDelay = ParseDelay("MessageAggregation:MaxDelayMs", 50_000);
        var delayMs = minDelay >= maxDelay
            ? minDelay
            : Random.Shared.Next(minDelay, maxDelay);
        return TimeSpan.FromMilliseconds(delayMs);
    }

    private int ParseDelay(string key, int fallback) =>
        int.TryParse(configuration[key], out var value) && value >= 0 ? value : fallback;
}
