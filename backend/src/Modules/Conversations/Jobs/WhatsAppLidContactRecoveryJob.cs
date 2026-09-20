using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Services;
using Modules.WhatsApp.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using Modules.Conversations.Services;
using StackExchange.Redis;

namespace Modules.Conversations.Jobs;

public sealed class WhatsAppLidContactRecoveryJob(
    AppDbContext dbContext,
    IEventBus eventBus,
    IConnectionMultiplexer redis,
    WhatsAppGatewaySessionClient whatsAppGateway,
    ILogger<WhatsAppLidContactRecoveryJob> logger)
{
    private const int RecoveryBatchSize = 200;
    private const int SolicitationBatchSize = 20;
    private const string PhoneRequestMessage =
        "أهلاً بحضرتك 🌷 علشان نسجل بياناتك بشكل صحيح، ممكن تبعتلنا رقم الموبايل بتاعك ويبدأ بـ 01؟";
    private static readonly TimeSpan SolicitationHistory = TimeSpan.FromDays(30);
    private static readonly TimeSpan SolicitationDelay = TimeSpan.FromDays(1);
    private static readonly TimeSpan SolicitationLockDuration = SolicitationHistory + TimeSpan.FromDays(1);

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Only provider-supplied LID/phone mappings may merge identities; message text is not ownership proof.
        var requestedCount = await RequestMissingPhoneNumbersAsync(cancellationToken);
        if (requestedCount > 0)
            logger.LogInformation("Requested real phone numbers from {Count} WhatsApp LID customers", requestedCount);
    }

    private async Task<int> RequestMissingPhoneNumbersAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var requestCutoff = now.Subtract(SolicitationHistory);
        var candidateRows = await dbContext.Conversations
            .IgnoreQueryFilters()
            .Join(
                dbContext.Customers.IgnoreQueryFilters(),
                conversation => conversation.CustomerId,
                customer => customer.Id,
                (conversation, customer) => new { conversation, customer })
            .Where(row => row.conversation.Channel == "WhatsApp"
                && row.conversation.HumanHandoffReplyId == null
                && row.conversation.Status != "Closed"
                && !row.customer.IsBlacklisted
                && (row.customer.PhoneNumber.Contains("@lid")
                    || row.customer.PhoneNumber.StartsWith("lid@")
                    || (row.customer.PhoneNumber == string.Empty && row.customer.WhatsAppLid != null))
                && !dbContext.GroupAppointmentBookings.IgnoreQueryFilters()
                    .Any(booking => booking.CustomerId == row.customer.Id)
                && !dbContext.Messages
                    .Any(message => message.ConversationId == row.conversation.Id
                        && message.Direction == "Outgoing"
                        && message.Content == PhoneRequestMessage
                        && message.Timestamp >= requestCutoff))
            .OrderBy(row => row.customer.CreatedAt)
            .Take(RecoveryBatchSize)
            .Select(row => new SolicitationCandidate(
                row.customer.Id,
                row.customer.ProjectId,
                row.conversation.Id,
                row.conversation.WhatsAppAccountId ?? row.customer.ProjectId,
                row.customer.CreatedAt,
                row.customer.PhoneNumber.Contains("@lid") || row.customer.PhoneNumber.StartsWith("lid@")
                    ? row.customer.PhoneNumber
                    : row.customer.WhatsAppLid!))
            .ToListAsync(cancellationToken);
        var candidates = candidateRows
            .GroupBy(candidate => candidate.CustomerId)
            .Select(group => group.First())
            .ToList();

        var requestedCount = 0;
        var sessions = new Dictionary<(Guid ProjectId, Guid AccountId), WhatsAppGatewaySessionStatus>();
        var candidateProjectIds = candidates
            .Select(candidate => candidate.ProjectId)
            .Distinct()
            .ToArray();
        var timezones = await dbContext.ProjectSettings
            .IgnoreQueryFilters()
            .Where(settings => candidateProjectIds.Contains(settings.ProjectId))
            .ToDictionaryAsync(settings => settings.ProjectId, settings => settings.Timezone, cancellationToken);
        foreach (var candidate in candidates)
        {
            if (requestedCount >= SolicitationBatchSize) break;
            var suppliedMessages = await dbContext.Messages.AsNoTracking()
                .Where(message => message.ConversationId == candidate.ConversationId && message.Direction == "Incoming"
                    && message.MessageType != "Reaction")
                .OrderByDescending(message => message.Timestamp)
                .Take(100)
                .Select(message => message.Transcription ?? message.Content)
                .ToListAsync(cancellationToken);
            if (suppliedMessages.Any(message => EgyptianPhoneNumber.Extract(message) != null)) continue;

            var sessionKey = (candidate.ProjectId, candidate.WhatsAppAccountId);
            if (!sessions.TryGetValue(sessionKey, out var session))
            {
                session = await whatsAppGateway.GetAsync(
                    candidate.ProjectId,
                    candidate.WhatsAppAccountId,
                    cancellationToken);
                sessions[sessionKey] = session;
            }
            if (!session.Connected || !session.ConnectedAt.HasValue) continue;

            var timezone = TimezoneHelper.GetTimeZone(timezones.GetValueOrDefault(candidate.ProjectId));
            var firstDueAt = candidate.CreatedAt.Add(SolicitationDelay);
            if (!WhatsAppDailyDeliverySchedule.IsDueInCurrentConnection(
                firstDueAt,
                now,
                session.ConnectedAt.Value,
                timezone)) continue;
            var scheduledFor = WhatsAppDailyDeliverySchedule.ScheduledOccurrenceInCurrentConnection(
                firstDueAt,
                now,
                session.ConnectedAt.Value,
                timezone);

            var deliveryKey = $"lid_{candidate.CustomerId:N}_{scheduledFor.Ticks}";
            var lockKey = $"whatsapp:lid-phone-request:{deliveryKey}";
            var lockAcquired = await redis.GetDatabase().StringSetAsync(
                lockKey,
                DateTime.UtcNow.ToString("O"),
                SolicitationLockDuration,
                When.NotExists);
            if (!lockAcquired) continue;

            var published = false;
            try
            {
                await eventBus.PublishAsync(new AIReplyGeneratedEvent
                {
                    ProjectId = candidate.ProjectId,
                    ConversationId = candidate.ConversationId,
                    WhatsAppAccountId = candidate.WhatsAppAccountId,
                    Sender = candidate.Destination,
                    Content = PhoneRequestMessage,
                    Channel = "WhatsApp",
                    RequiredWhatsAppConnectedAt = session.ConnectedAt,
                    WhatsAppDeliveryIdempotencyKey = deliveryKey
                });
                published = true;
                requestedCount++;
            }
            finally
            {
                if (!published) await redis.GetDatabase().KeyDeleteAsync(lockKey);
            }
        }

        return requestedCount;
    }

    private sealed record SolicitationCandidate(
        Guid CustomerId,
        Guid ProjectId,
        Guid ConversationId,
        Guid WhatsAppAccountId,
        DateTime CreatedAt,
        string Destination);
}
