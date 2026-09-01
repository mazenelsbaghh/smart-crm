using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Conversations.Jobs;

public sealed class ConversationReplyWindowDispatcher(
    AppDbContext dbContext,
    ILogger<ConversationReplyWindowDispatcher> logger)
{
    private const int BatchSize = 100;

    public async Task DispatchAsync(CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational())
        {
            await DispatchTrackedAsync(cancellationToken);
            return;
        }
        if (!string.Equals(
            dbContext.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal))
        {
            await DispatchConditionallyAsync(cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var windows = await dbContext.ConversationReplyWindows
            .FromSqlInterpolated($"""
                SELECT *
                FROM "ConversationReplyWindows"
                WHERE "DueAtUtc" <= {now}
                  AND "DispatchedEventId" IS DISTINCT FROM "EventId"
                ORDER BY "DueAtUtc", "Id"
                FOR UPDATE SKIP LOCKED
                LIMIT {BatchSize}
                """)
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

        await EnqueueAsync(windows, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (windows.Count > 0)
            logger.LogInformation("Queued {Count} due conversation reply windows", windows.Count);
    }

    private async Task DispatchConditionallyAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var candidateIds = await dbContext.ConversationReplyWindows.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(window => window.DueAtUtc <= now && window.DispatchedEventId != window.EventId)
            .OrderBy(window => window.DueAtUtc)
            .ThenBy(window => window.Id)
            .Take(BatchSize)
            .Select(window => window.Id)
            .ToListAsync(cancellationToken);
        var claimedWindows = new List<ConversationReplyWindow>(candidateIds.Count);
        foreach (var candidateId in candidateIds)
        {
            var claimed = await dbContext.ConversationReplyWindows.IgnoreQueryFilters()
                .Where(window => window.Id == candidateId
                    && window.DueAtUtc <= now
                    && window.DispatchedEventId != window.EventId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(window => window.DispatchedEventId, window => window.EventId)
                    .SetProperty(window => window.DispatchedAtUtc, now), cancellationToken);
            if (claimed == 0) continue;
            claimedWindows.Add(await dbContext.ConversationReplyWindows.IgnoreQueryFilters()
                .SingleAsync(window => window.Id == candidateId, cancellationToken));
        }

        await EnqueueAsync(claimedWindows, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task DispatchTrackedAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var windows = await dbContext.ConversationReplyWindows
            .IgnoreQueryFilters()
            .Where(window => window.DueAtUtc <= now && window.DispatchedEventId != window.EventId)
            .OrderBy(window => window.DueAtUtc)
            .ThenBy(window => window.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        await EnqueueAsync(windows, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnqueueAsync(
        IEnumerable<ConversationReplyWindow> windows,
        DateTime dispatchedAtUtc,
        CancellationToken cancellationToken)
    {
        foreach (var window in windows)
        {
            var latestSource = await dbContext.Messages.IgnoreQueryFilters()
                .Where(message => message.ConversationId == window.ConversationId)
                .Where(message => message.MessageType != "Reaction")
                .OrderByDescending(message => message.Timestamp)
                .ThenByDescending(message => message.Id)
                .Select(message => new { message.Id, message.Direction })
                .FirstOrDefaultAsync(cancellationToken);
            if (latestSource is null
                || latestSource.Direction != "Incoming"
                || latestSource.Id != window.LatestIncomingMessageId)
            {
                // A human/automation reply or a newer inbound generation won the race.
                // Consume this obsolete window without producing an AI event.
                window.DispatchedEventId = window.EventId;
                window.DispatchedAtUtc = dispatchedAtUtc;
                continue;
            }

            var lastOutgoingAtUtc = await dbContext.Messages.IgnoreQueryFilters()
                .Where(message => message.ConversationId == window.ConversationId
                    && message.Direction == "Outgoing"
                    && message.Timestamp <= window.LatestIncomingAtUtc)
                .MaxAsync(message => (DateTime?)message.Timestamp, cancellationToken);
            var incoming = await dbContext.Messages.IgnoreQueryFilters()
                .Where(message => message.ConversationId == window.ConversationId
                    && message.Direction == "Incoming"
                    && message.MessageType != "Reaction"
                    && message.Timestamp <= window.LatestIncomingAtUtc
                    && (!lastOutgoingAtUtc.HasValue || message.Timestamp > lastOutgoingAtUtc.Value))
                .OrderByDescending(message => message.Timestamp)
                .ThenByDescending(message => message.Id)
                .Take(20)
                .Select(message => message.Content)
                .ToListAsync(cancellationToken);
            incoming.Reverse();
            var content = incoming.Count > 0
                ? string.Join("\n", incoming)
                : window.AggregatedContent;
            IntegrationOutbox.Enqueue(dbContext, new MessageAggregatedEvent
            {
                Id = window.EventId,
                OccurredOn = dispatchedAtUtc,
                ProjectId = window.ProjectId,
                ConversationId = window.ConversationId,
                WhatsAppAccountId = window.WhatsAppAccountId,
                Sender = window.Sender,
                Content = content,
                Channel = window.Channel,
                ChannelMetadata = window.ChannelMetadata,
                SourceMessageTimestampUtc = window.LatestIncomingAtUtc,
                RequiredWhatsAppConnectedAt = window.RequiredWhatsAppConnectedAt,
                WhatsAppDeliveryIdempotencyKey = window.WhatsAppDeliveryIdempotencyKey
            });
            window.DispatchedEventId = window.EventId;
            window.DispatchedAtUtc = dispatchedAtUtc;
        }
    }
}
