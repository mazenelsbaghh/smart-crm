using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Services;
using Modules.Conversations.Domain;
using Modules.Projects.Domain;
using Modules.Conversations.Services;
using Modules.WhatsApp.Services;
using Shared.Events;
using Shared.Infrastructure;
using System.Text.Json;

namespace Modules.Conversations.Jobs;

public sealed record UnansweredConversationRecoveryDependencies(
    WhatsAppGatewaySessionClient WhatsAppGateway,
    ILogger<UnansweredConversationRecoveryJob> Logger);

public sealed class UnansweredConversationRecoveryJob(
    AppDbContext dbContext,
    ConversationReplyWindowService replyWindows,
    UnansweredConversationRecoveryDependencies dependencies)
{
    private const int BatchSize = 25;
    private const int WhatsAppCandidateScanSize = BatchSize * 20;
    private static readonly TimeSpan MinimumMessageAge = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MessengerReplyWindow = TimeSpan.FromHours(23);
    private static readonly TimeSpan RecoveryLookback = TimeSpan.FromDays(30);

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.Subtract(MinimumMessageAge);
        var recoveryStart = now.Subtract(RecoveryLookback);
        var selection = await LoadUnansweredConversationsAsync(now, recoveryStart, cutoff, cancellationToken);
        if (selection.Conversations.Count == 0) return;

        var messages = await LoadLatestIncomingMessagesAsync(selection.Conversations, cutoff, cancellationToken);
        if (messages.Count == 0) return;

        var recoveryContext = await LoadRecoveryContextAsync(
            selection.Conversations,
            selection.WhatsAppSessions,
            now,
            cancellationToken);
        var recoveredCount = await RequeueMessagesAsync(messages, recoveryContext, cancellationToken);
        if (recoveredCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            dependencies.Logger.LogInformation("Requeued {Count} unanswered conversations for AI reply recovery", recoveredCount);
        }
    }

    private async Task<Dictionary<WhatsAppSessionKey, WhatsAppGatewaySessionStatus>> LoadWhatsAppSessionsAsync(
        IEnumerable<WhatsAppSessionKey> whatsAppAccounts,
        CancellationToken cancellationToken)
    {
        var connectionChecks = whatsAppAccounts.Select(account =>
            GetWhatsAppSessionAsync(account, cancellationToken));
        var projectConnections = await Task.WhenAll(connectionChecks);
        return projectConnections.ToDictionary(connection => connection.Account, connection => connection.Session);
    }

    private async Task<(WhatsAppSessionKey Account, WhatsAppGatewaySessionStatus Session)> GetWhatsAppSessionAsync(
        WhatsAppSessionKey account,
        CancellationToken cancellationToken)
    {
        var session = await dependencies.WhatsAppGateway.GetAsync(
            account.ProjectId,
            account.AccountId,
            cancellationToken);
        if (!session.Connected || !session.ConnectedAt.HasValue)
            dependencies.Logger.LogInformation(
                "Skipping WhatsApp AI recovery for project {ProjectId}, account {AccountId} because the gateway status is {Status} or has no connection epoch.",
                account.ProjectId,
                account.AccountId,
                session.Status);
        return (account, session);
    }

    private async Task<RecoverySelection> LoadUnansweredConversationsAsync(
        DateTime now,
        DateTime recoveryStart,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var candidates = UnansweredConversationCandidates(now, recoveryStart, cutoff);
        var whatsAppAccountRows = await candidates
            .Where(conversation => conversation.Channel == "WhatsApp"
                && conversation.WhatsAppDestinationId == null)
            .Select(conversation => new
            {
                conversation.ProjectId,
                AccountId = conversation.WhatsAppAccountId ?? conversation.ProjectId
            })
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var whatsAppAccounts = whatsAppAccountRows
            .Select(row => new WhatsAppSessionKey(row.ProjectId, row.AccountId))
            .ToArray();
        var whatsAppSessions = await LoadWhatsAppSessionsAsync(
            whatsAppAccounts,
            cancellationToken);
        var accountsWithConnectionEpoch = whatsAppSessions
            .Where(connection => connection.Value.Connected && connection.Value.ConnectedAt.HasValue)
            .Select(connection => connection.Key)
            .ToArray();
        var projectsWithConnectionEpoch = accountsWithConnectionEpoch
            .Select(account => account.ProjectId)
            .Distinct()
            .ToArray();
        var projectTimezones = await dbContext.ProjectSettings
            .IgnoreQueryFilters()
            .Where(settings => projectsWithConnectionEpoch.Contains(settings.ProjectId))
            .ToDictionaryAsync(
                settings => settings.ProjectId,
                settings => settings.Timezone,
                cancellationToken);
        var candidateBuckets = new List<IReadOnlyList<Conversation>>();
        foreach (var account in accountsWithConnectionEpoch)
        {
            var accountCandidates = await candidates
                .Where(conversation => conversation.Channel == "WhatsApp"
                    && conversation.WhatsAppDestinationId == null
                    && conversation.ProjectId == account.ProjectId
                    && (conversation.WhatsAppAccountId ?? conversation.ProjectId) == account.AccountId)
                .OrderBy(conversation => conversation.LastUnansweredRecoveryAttemptAt.HasValue)
                .ThenBy(conversation => conversation.LastUnansweredRecoveryAttemptAt)
                .ThenBy(conversation => conversation.LastMessageTimestamp)
                .Take(WhatsAppCandidateScanSize)
                .ToListAsync(cancellationToken);
            var scheduled = accountCandidates
                .Where(conversation => IsDueForRecovery(
                    conversation,
                    now,
                    whatsAppSessions[account].ConnectedAt!.Value,
                    TimezoneHelper.GetTimeZone(projectTimezones.GetValueOrDefault(conversation.ProjectId))))
                .ToList();
            if (scheduled.Count > 0) candidateBuckets.Add(scheduled);
        }
        var otherSources = await candidates
            .Where(conversation => conversation.Channel != "WhatsApp")
            .Select(conversation => new { conversation.ProjectId, conversation.Channel })
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var source in otherSources)
        {
            var sourceCandidates = await candidates
                .Where(conversation => conversation.ProjectId == source.ProjectId
                    && conversation.Channel == source.Channel)
                .OrderBy(conversation => conversation.LastUnansweredRecoveryAttemptAt.HasValue)
                .ThenBy(conversation => conversation.LastUnansweredRecoveryAttemptAt)
                .ThenBy(conversation => conversation.LastMessageTimestamp)
                .Take(WhatsAppCandidateScanSize)
                .ToListAsync(cancellationToken);
            if (sourceCandidates.Count > 0) candidateBuckets.Add(sourceCandidates);
        }

        var selectedConversations = RoundRobinCandidates(
            candidateBuckets,
            WhatsAppCandidateScanSize);
        var selectedWhatsAppAccounts = selectedConversations
            .Where(conversation => conversation.Channel == "WhatsApp")
            .Select(SessionKey)
            .ToHashSet();
        var selectedSessions = whatsAppSessions
            .Where(session => selectedWhatsAppAccounts.Contains(session.Key))
            .ToDictionary(session => session.Key, session => session.Value);
        return new RecoverySelection(selectedConversations, selectedSessions);
    }

    private static bool IsDueForRecovery(
        Conversation conversation,
        DateTime nowUtc,
        DateTimeOffset connectedAt,
        TimeZoneInfo timezone)
    {
        var originalDueUtc = conversation.LastMessageTimestamp.Add(MinimumMessageAge);
        var eligibleDueUtc = originalDueUtc;
        if (conversation.LastMessageTimestamp < connectedAt.UtcDateTime)
        {
            var boundaryUtc = originalDueUtc > connectedAt.UtcDateTime
                ? originalDueUtc
                : connectedAt.UtcDateTime;
            eligibleDueUtc = WhatsAppDailyDeliverySchedule.NextOccurrenceAfter(
                originalDueUtc,
                boundaryUtc,
                timezone);
        }

        return WhatsAppDailyDeliverySchedule.IsDueInCurrentConnection(
            eligibleDueUtc,
            nowUtc,
            connectedAt,
            timezone);
    }

    private static List<Conversation> RoundRobinCandidates(
        IReadOnlyList<IReadOnlyList<Conversation>> buckets,
        int limit)
    {
        var selected = new List<Conversation>(Math.Min(limit, buckets.Sum(bucket => bucket.Count)));
        for (var index = 0; selected.Count < limit; index++)
        {
            var added = false;
            foreach (var bucket in buckets)
            {
                if (index >= bucket.Count) continue;
                selected.Add(bucket[index]);
                added = true;
                if (selected.Count == limit) break;
            }
            if (!added) break;
        }
        return selected;
    }

    private IQueryable<Conversation> UnansweredConversationCandidates(
        DateTime now,
        DateTime recoveryStart,
        DateTime cutoff) => dbContext.Conversations
        .IgnoreQueryFilters()
        .Where(conversation => (conversation.Status == "Open" || conversation.Status == "Pending")
            && conversation.LastMessageTimestamp >= recoveryStart
            && conversation.LastMessageTimestamp <= cutoff
            && conversation.WhatsAppDeliveryUnknownAt == null
            && ((conversation.Channel != "Messenger" && conversation.Channel != "FacebookComment")
                || conversation.LastMessageTimestamp >= now.Subtract(MessengerReplyWindow))
            && !dbContext.Conversations.IgnoreQueryFilters().Any(other =>
                other.ProjectId == conversation.ProjectId
                && other.Channel == conversation.Channel
                && (conversation.Channel != "WhatsApp"
                    || (other.WhatsAppAccountId ?? other.ProjectId)
                        == (conversation.WhatsAppAccountId ?? conversation.ProjectId))
                && (other.Status == "Open" || other.Status == "Pending")
                && other.LastMessageTimestamp > conversation.LastMessageTimestamp
                && dbContext.Customers.IgnoreQueryFilters().Any(currentCustomer =>
                    currentCustomer.Id == conversation.CustomerId
                    && dbContext.Customers.IgnoreQueryFilters().Any(otherCustomer =>
                        otherCustomer.Id == other.CustomerId
                        && ((conversation.Channel == "WhatsApp"
                                && currentCustomer.PhoneNumber != null
                                && currentCustomer.PhoneNumber == otherCustomer.PhoneNumber)
                            || (conversation.Channel != "WhatsApp"
                                && currentCustomer.FacebookPSID != null
                                && currentCustomer.FacebookPSID == otherCustomer.FacebookPSID)))))
            && dbContext.Customers.IgnoreQueryFilters().Any(customer => customer.Id == conversation.CustomerId && !customer.IsBlacklisted)
            && !dbContext.GroupAppointmentBookings.IgnoreQueryFilters().Any(booking => booking.CustomerId == conversation.CustomerId && booking.IsPaid)
            && dbContext.ProjectSettings.IgnoreQueryFilters().Any(settings => settings.ProjectId == conversation.ProjectId
                && ((conversation.Channel == "WhatsApp" && settings.AiAutoReplyEnabled)
                    || (conversation.Channel == "Messenger" && settings.MessengerAiAutoReplyEnabled)
                    || (conversation.Channel == "FacebookComment" && settings.CommentsAiAutoReplyEnabled)))
            && dbContext.Messages
                .Where(message => message.ConversationId == conversation.Id && message.MessageType == "Text")
                .OrderByDescending(message => message.Timestamp)
                .ThenByDescending(message => message.Id)
                .Select(message => message.Direction)
                .FirstOrDefault() == "Incoming");

    private async Task<List<Message>> LoadLatestIncomingMessagesAsync(
        IReadOnlyCollection<Conversation> conversations,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var conversationIds = conversations.Select(conversation => conversation.Id).ToArray();
        var latestMessages = await dbContext.Messages
            .Where(message => conversationIds.Contains(message.ConversationId) && message.MessageType == "Text")
            .GroupBy(message => message.ConversationId)
            .Select(group => group.OrderByDescending(message => message.Timestamp).ThenByDescending(message => message.Id).First())
            .ToListAsync(cancellationToken);

        var conversationOrder = conversations
            .Select((conversation, index) => new { conversation.Id, index })
            .ToDictionary(item => item.Id, item => item.index);
        return latestMessages
            .Where(message => message.Direction == "Incoming" && message.Timestamp <= cutoff)
            .OrderBy(message => conversationOrder[message.ConversationId])
            .ToList();
    }

    private async Task<RecoveryContext> LoadRecoveryContextAsync(
        IReadOnlyCollection<Conversation> conversations,
        IReadOnlyDictionary<WhatsAppSessionKey, WhatsAppGatewaySessionStatus> whatsAppSessions,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var customerIds = conversations.Select(conversation => conversation.CustomerId).Distinct().ToArray();
        var projectIds = conversations.Select(conversation => conversation.ProjectId).Distinct().ToArray();
        var customers = await LoadCustomersAsync(customerIds, cancellationToken);
        var settings = await LoadSettingsAsync(projectIds, cancellationToken);
        var paidCustomers = await LoadPaidCustomersAsync(customerIds, cancellationToken);
        var pages = await LoadActivePagesAsync(projectIds, cancellationToken);

        return new RecoveryContext(
            conversations.ToDictionary(conversation => conversation.Id),
            customers,
            settings,
            paidCustomers,
            pages,
            whatsAppSessions,
            nowUtc);
    }

    private async Task<Dictionary<Guid, Customer>> LoadCustomersAsync(
        Guid[] customerIds,
        CancellationToken cancellationToken) => await dbContext.Customers
        .IgnoreQueryFilters()
        .Where(customer => customerIds.Contains(customer.Id))
        .ToDictionaryAsync(customer => customer.Id, cancellationToken);

    private async Task<Dictionary<Guid, ProjectSettings>> LoadSettingsAsync(
        Guid[] projectIds,
        CancellationToken cancellationToken) => await dbContext.ProjectSettings
        .IgnoreQueryFilters()
        .Where(settings => projectIds.Contains(settings.ProjectId))
        .ToDictionaryAsync(settings => settings.ProjectId, cancellationToken);

    private async Task<HashSet<Guid>> LoadPaidCustomersAsync(
        Guid[] customerIds,
        CancellationToken cancellationToken)
    {
        var paidCustomerIds = await dbContext.GroupAppointmentBookings
            .IgnoreQueryFilters()
            .Where(booking => customerIds.Contains(booking.CustomerId) && booking.IsPaid)
            .Select(booking => booking.CustomerId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return paidCustomerIds.ToHashSet();
    }

    private async Task<Dictionary<Guid, string>> LoadActivePagesAsync(
        Guid[] projectIds,
        CancellationToken cancellationToken)
    {
        var activePages = await dbContext.ConnectedPages
            .IgnoreQueryFilters()
            .Where(page => projectIds.Contains(page.ProjectId) && page.IsActive)
            .OrderBy(page => page.CreatedAt)
            .ToListAsync(cancellationToken);
        return activePages.GroupBy(page => page.ProjectId)
            .ToDictionary(group => group.Key, group => group.First().FacebookPageId);
    }

    private async Task<int> RequeueMessagesAsync(
        IEnumerable<Message> messages,
        RecoveryContext recoveryContext,
        CancellationToken cancellationToken)
    {
        var recoveredCount = 0;
        foreach (var message in messages)
        {
            if (recoveredCount >= BatchSize) break;
            var recoveryEvent = CreateRecoveryEvent(message, recoveryContext);
            if (recoveryEvent is null) continue;
            if (!await TryStageRecoveryAsync(
                recoveryEvent,
                message,
                recoveryContext.NowUtc,
                cancellationToken)) continue;
            recoveryContext.Conversations[message.ConversationId].LastUnansweredRecoveryAttemptAt = recoveryContext.NowUtc;
            recoveredCount++;
        }
        return recoveredCount;
    }

    private static MessageAggregatedEvent? CreateRecoveryEvent(Message message, RecoveryContext recoveryContext)
    {
        var conversation = recoveryContext.Conversations[message.ConversationId];
        if (!recoveryContext.Customers.TryGetValue(conversation.CustomerId, out var customer)
            || customer.IsBlacklisted
            || recoveryContext.PaidCustomers.Contains(customer.Id)
            || !recoveryContext.Settings.TryGetValue(conversation.ProjectId, out var settings)
            || !IsAutoReplyEnabled(conversation.Channel, settings)) return null;

        var sender = conversation.Channel == "WhatsApp" ? customer.PhoneNumber : customer.FacebookPSID;
        if (string.IsNullOrWhiteSpace(sender)) return null;
        if (conversation.Channel == "FacebookComment" && string.IsNullOrWhiteSpace(message.FacebookCommentId)) return null;

        var metadata = BuildChannelMetadata(conversation, message, sender, recoveryContext.Pages);
        if (conversation.Channel != "WhatsApp" && metadata is null) return null;

        WhatsAppGatewaySessionStatus? whatsAppSession = null;
        if (conversation.Channel == "WhatsApp")
            recoveryContext.WhatsAppSessions.TryGetValue(SessionKey(conversation), out whatsAppSession);

        return new MessageAggregatedEvent
        {
            ProjectId = conversation.ProjectId,
            ConversationId = conversation.Id,
            WhatsAppAccountId = conversation.Channel == "WhatsApp"
                ? conversation.WhatsAppAccountId ?? conversation.ProjectId
                : null,
            Sender = sender,
            Content = message.Content,
            Channel = conversation.Channel,
            ChannelMetadata = metadata,
            SourceMessageTimestampUtc = message.Timestamp,
            WhatsAppDeliveryIdempotencyKey = whatsAppSession?.ConnectedAt.HasValue == true
                ? ConversationReplyWindowService.WhatsAppDeliveryKey(
                    message.Id,
                    whatsAppSession.ConnectedAt.Value)
                : null,
            RequiredWhatsAppConnectedAt = whatsAppSession?.ConnectedAt
        };
    }

    private async Task<bool> TryStageRecoveryAsync(
        MessageAggregatedEvent recoveryEvent,
        Message message,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.Equals(recoveryEvent.Channel, "WhatsApp", StringComparison.Ordinal))
        {
            if (!recoveryEvent.WhatsAppAccountId.HasValue
                || !recoveryEvent.RequiredWhatsAppConnectedAt.HasValue) return false;

            var liveSession = await dependencies.WhatsAppGateway.GetAsync(
                recoveryEvent.ProjectId,
                recoveryEvent.WhatsAppAccountId.Value,
                cancellationToken);
            if (!liveSession.Connected
                || liveSession.ConnectedAt != recoveryEvent.RequiredWhatsAppConnectedAt)
            {
                dependencies.Logger.LogInformation(
                    "Skipping WhatsApp AI recovery for project {ProjectId}, account {AccountId} because the connection epoch changed before staging.",
                    recoveryEvent.ProjectId,
                    recoveryEvent.WhatsAppAccountId);
                return false;
            }
        }

        var occurrenceKey = recoveryEvent.RequiredWhatsAppConnectedAt.HasValue
            ? ConversationReplyWindowService.WhatsAppEpochOccurrenceKey(
                recoveryEvent.RequiredWhatsAppConnectedAt.Value)
            : ConversationReplyWindowService.SourceMessageOccurrenceKey(message.Id);
        await replyWindows.StageAsync(new ConversationReplyWindowRequest(
            recoveryEvent.ProjectId,
            message.ConversationId,
            message.Id,
            recoveryEvent.Sender,
            recoveryEvent.Content,
            message.Timestamp,
            nowUtc,
            occurrenceKey,
            recoveryEvent.Channel,
            recoveryEvent.WhatsAppAccountId,
            recoveryEvent.ChannelMetadata,
            recoveryEvent.RequiredWhatsAppConnectedAt,
            recoveryEvent.WhatsAppDeliveryIdempotencyKey), cancellationToken);
        return true;
    }

    private static bool IsAutoReplyEnabled(string channel, ProjectSettings settings) => channel switch
    {
        "Messenger" => settings.MessengerAiAutoReplyEnabled,
        "FacebookComment" => settings.CommentsAiAutoReplyEnabled,
        _ => settings.AiAutoReplyEnabled
    };

    private static string? BuildChannelMetadata(
        Conversation conversation,
        Message message,
        string sender,
        IReadOnlyDictionary<Guid, string> pages)
    {
        if (conversation.Channel == "WhatsApp") return null;
        if (!pages.TryGetValue(conversation.ProjectId, out var pageId)) return null;

        return conversation.Channel == "FacebookComment"
            ? JsonSerializer.Serialize(new
            {
                pageId,
                commentId = message.FacebookCommentId,
                postId = message.FacebookPostId,
                senderPSID = sender
            })
            : JsonSerializer.Serialize(new { pageId, senderPSID = sender });
    }

    private sealed record RecoveryContext(
        IReadOnlyDictionary<Guid, Conversation> Conversations,
        IReadOnlyDictionary<Guid, Customer> Customers,
        IReadOnlyDictionary<Guid, ProjectSettings> Settings,
        IReadOnlySet<Guid> PaidCustomers,
        IReadOnlyDictionary<Guid, string> Pages,
        IReadOnlyDictionary<WhatsAppSessionKey, WhatsAppGatewaySessionStatus> WhatsAppSessions,
        DateTime NowUtc);

    private sealed record RecoverySelection(
        IReadOnlyList<Conversation> Conversations,
        IReadOnlyDictionary<WhatsAppSessionKey, WhatsAppGatewaySessionStatus> WhatsAppSessions);

    private static WhatsAppSessionKey SessionKey(Conversation conversation) =>
        new(conversation.ProjectId, conversation.WhatsAppAccountId ?? conversation.ProjectId);

    private readonly record struct WhatsAppSessionKey(Guid ProjectId, Guid AccountId);
}
