using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Shared.Infrastructure;

namespace Modules.Conversations.Services;

public sealed record ConversationReplyWindowRequest(
    Guid ProjectId,
    Guid ConversationId,
    Guid LatestIncomingMessageId,
    string Sender,
    string Content,
    DateTime SourceMessageTimestampUtc,
    DateTime DueAtUtc,
    string EventOccurrenceKey,
    string Channel = "WhatsApp",
    Guid? WhatsAppAccountId = null,
    string? ChannelMetadata = null,
    DateTimeOffset? RequiredWhatsAppConnectedAt = null,
    string? WhatsAppDeliveryIdempotencyKey = null);

public sealed class ConversationReplyWindowService(AppDbContext dbContext)
{
    public static string WhatsAppEpochOccurrenceKey(DateTimeOffset connectedAt) =>
        $"wa-epoch:{connectedAt.ToUniversalTime().Ticks}";

    public static string WhatsAppDeliveryKey(Guid messageId, DateTimeOffset connectedAt) =>
        $"reply_{messageId:N}_{connectedAt.ToUniversalTime().Ticks}";

    public static string SourceMessageOccurrenceKey(Guid messageId) =>
        $"source-message:{messageId:N}";

    public async Task StageAsync(
        ConversationReplyWindowRequest request,
        CancellationToken cancellationToken = default)
    {
        var eventId = DeterministicId(
            $"conversation-reply:{request.ProjectId:N}:{request.ConversationId:N}:" +
            $"{request.LatestIncomingMessageId:N}:{request.EventOccurrenceKey}");
        var windowId = DeterministicId($"conversation-reply-window:{request.ConversationId:N}");

        if (!string.Equals(
            dbContext.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal))
        {
            await StageTrackedAsync(windowId, eventId, request, cancellationToken);
            return;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ConversationReplyWindows"
                ("Id", "ProjectId", "ConversationId", "WhatsAppAccountId", "Channel",
                 "LatestIncomingMessageId", "LatestIncomingVersion", "LatestIncomingAtUtc",
                 "Sender", "AggregatedContent", "ChannelMetadata", "DueAtUtc",
                 "RequiredWhatsAppConnectedAt", "EventId", "WhatsAppDeliveryIdempotencyKey",
                 "CreatedAt", "UpdatedAt")
            VALUES
                ({windowId}, {request.ProjectId}, {request.ConversationId}, {request.WhatsAppAccountId},
                 {request.Channel}, {request.LatestIncomingMessageId}, 1,
                 {request.SourceMessageTimestampUtc}, {request.Sender}, {request.Content},
                 {request.ChannelMetadata}, {request.DueAtUtc},
                 {request.RequiredWhatsAppConnectedAt}, {eventId},
                 {request.WhatsAppDeliveryIdempotencyKey}, {DateTime.UtcNow}, {DateTime.UtcNow})
            ON CONFLICT ("ConversationId") DO UPDATE SET
                "WhatsAppAccountId" = EXCLUDED."WhatsAppAccountId",
                "Channel" = EXCLUDED."Channel",
                "LatestIncomingMessageId" = EXCLUDED."LatestIncomingMessageId",
                "LatestIncomingVersion" = CASE
                    WHEN "ConversationReplyWindows"."LatestIncomingMessageId" = EXCLUDED."LatestIncomingMessageId"
                        THEN "ConversationReplyWindows"."LatestIncomingVersion"
                    ELSE "ConversationReplyWindows"."LatestIncomingVersion" + 1
                END,
                "LatestIncomingAtUtc" = EXCLUDED."LatestIncomingAtUtc",
                "Sender" = EXCLUDED."Sender",
                "AggregatedContent" = CASE
                    WHEN "ConversationReplyWindows"."LatestIncomingMessageId" = EXCLUDED."LatestIncomingMessageId"
                        THEN "ConversationReplyWindows"."AggregatedContent"
                    WHEN "ConversationReplyWindows"."DispatchedEventId" IS DISTINCT FROM "ConversationReplyWindows"."EventId"
                        THEN "ConversationReplyWindows"."AggregatedContent" || E'\n' || EXCLUDED."AggregatedContent"
                    ELSE EXCLUDED."AggregatedContent"
                END,
                "ChannelMetadata" = EXCLUDED."ChannelMetadata",
                "DueAtUtc" = CASE
                    WHEN "ConversationReplyWindows"."LatestIncomingMessageId" = EXCLUDED."LatestIncomingMessageId"
                         AND "ConversationReplyWindows"."EventId" = EXCLUDED."EventId"
                        THEN "ConversationReplyWindows"."DueAtUtc"
                    ELSE EXCLUDED."DueAtUtc"
                END,
                "RequiredWhatsAppConnectedAt" = EXCLUDED."RequiredWhatsAppConnectedAt",
                "EventId" = EXCLUDED."EventId",
                "WhatsAppDeliveryIdempotencyKey" = EXCLUDED."WhatsAppDeliveryIdempotencyKey",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            WHERE ("ConversationReplyWindows"."LatestIncomingMessageId" = EXCLUDED."LatestIncomingMessageId"
                    AND ("ConversationReplyWindows"."EventId" = EXCLUDED."EventId"
                         OR ("ConversationReplyWindows"."Channel" = 'WhatsApp'
                             AND EXCLUDED."Channel" = 'WhatsApp'
                             AND EXCLUDED."RequiredWhatsAppConnectedAt" IS NOT NULL
                             AND ("ConversationReplyWindows"."RequiredWhatsAppConnectedAt" IS NULL
                                  OR EXCLUDED."RequiredWhatsAppConnectedAt"
                                      > "ConversationReplyWindows"."RequiredWhatsAppConnectedAt"))))
               OR EXCLUDED."LatestIncomingAtUtc" > "ConversationReplyWindows"."LatestIncomingAtUtc"
               OR (EXCLUDED."LatestIncomingAtUtc" = "ConversationReplyWindows"."LatestIncomingAtUtc"
                   AND EXCLUDED."LatestIncomingMessageId" > "ConversationReplyWindows"."LatestIncomingMessageId");
            """, cancellationToken);
    }

    private async Task StageTrackedAsync(
        Guid windowId,
        Guid eventId,
        ConversationReplyWindowRequest request,
        CancellationToken cancellationToken)
    {
        var window = await dbContext.ConversationReplyWindows
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.ConversationId == request.ConversationId, cancellationToken);
        if (window is null)
        {
            dbContext.ConversationReplyWindows.Add(new ConversationReplyWindow
            {
                Id = windowId,
                ProjectId = request.ProjectId,
                ConversationId = request.ConversationId,
                WhatsAppAccountId = request.WhatsAppAccountId,
                Channel = request.Channel,
                LatestIncomingMessageId = request.LatestIncomingMessageId,
                LatestIncomingAtUtc = request.SourceMessageTimestampUtc,
                Sender = request.Sender,
                AggregatedContent = request.Content,
                ChannelMetadata = request.ChannelMetadata,
                DueAtUtc = request.DueAtUtc,
                RequiredWhatsAppConnectedAt = request.RequiredWhatsAppConnectedAt,
                EventId = eventId,
                WhatsAppDeliveryIdempotencyKey = request.WhatsAppDeliveryIdempotencyKey
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var sameMessage = window.LatestIncomingMessageId == request.LatestIncomingMessageId;
        var sameEvent = window.EventId == eventId;
        var hasNewerWhatsAppEpoch = string.Equals(window.Channel, "WhatsApp", StringComparison.Ordinal)
            && string.Equals(request.Channel, "WhatsApp", StringComparison.Ordinal)
            && request.RequiredWhatsAppConnectedAt.HasValue
            && (!window.RequiredWhatsAppConnectedAt.HasValue
                || request.RequiredWhatsAppConnectedAt.Value > window.RequiredWhatsAppConnectedAt.Value);
        var isNewer = request.SourceMessageTimestampUtc > window.LatestIncomingAtUtc
            || (request.SourceMessageTimestampUtc == window.LatestIncomingAtUtc
                && request.LatestIncomingMessageId.CompareTo(window.LatestIncomingMessageId) > 0);
        if ((sameMessage && !sameEvent && !hasNewerWhatsAppEpoch)
            || (!sameMessage && !isNewer)) return;
        if (!sameMessage)
        {
            window.LatestIncomingVersion++;
            window.AggregatedContent = window.DispatchedEventId != window.EventId
                ? $"{window.AggregatedContent}\n{request.Content}"
                : request.Content;
        }
        window.WhatsAppAccountId = request.WhatsAppAccountId;
        window.Channel = request.Channel;
        window.LatestIncomingMessageId = request.LatestIncomingMessageId;
        window.LatestIncomingAtUtc = request.SourceMessageTimestampUtc;
        window.Sender = request.Sender;
        window.ChannelMetadata = request.ChannelMetadata;
        if (!sameMessage || !sameEvent) window.DueAtUtc = request.DueAtUtc;
        window.RequiredWhatsAppConnectedAt = request.RequiredWhatsAppConnectedAt;
        window.EventId = eventId;
        window.WhatsAppDeliveryIdempotencyKey = request.WhatsAppDeliveryIdempotencyKey;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Guid DeterministicId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
