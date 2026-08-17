using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.Projects.Domain;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using StackExchange.Redis;
using System.Text.Json;

namespace Modules.Conversations.Jobs;

public sealed class UnansweredConversationRecoveryJob(
    AppDbContext dbContext,
    IEventBus eventBus,
    IConnectionMultiplexer redis,
    ILogger<UnansweredConversationRecoveryJob> logger)
{
    private const int BatchSize = 200;
    private static readonly TimeSpan MinimumMessageAge = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RecoveryLookback = TimeSpan.FromHours(48);
    private static readonly TimeSpan RetryLockDuration = TimeSpan.FromMinutes(30);

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.Subtract(MinimumMessageAge);
        var recoveryStart = now.Subtract(RecoveryLookback);
        var conversations = await LoadUnansweredConversationsAsync(recoveryStart, cutoff, cancellationToken);
        if (conversations.Count == 0) return;

        var messages = await LoadLatestIncomingMessagesAsync(conversations, cutoff, cancellationToken);
        if (messages.Count == 0) return;

        var recoveryContext = await LoadRecoveryContextAsync(conversations, cancellationToken);
        var recoveredCount = await RequeueMessagesAsync(messages, recoveryContext);
        if (recoveredCount > 0)
            logger.LogInformation("Requeued {Count} unanswered conversations for AI reply recovery", recoveredCount);
    }

    private Task<List<Conversation>> LoadUnansweredConversationsAsync(
        DateTime recoveryStart,
        DateTime cutoff,
        CancellationToken cancellationToken) => dbContext.Conversations
        .IgnoreQueryFilters()
        .Where(conversation => (conversation.Status == "Open" || conversation.Status == "Pending")
            && conversation.LastMessageTimestamp >= recoveryStart
            && conversation.LastMessageTimestamp <= cutoff
            && dbContext.Messages
                .Where(message => message.ConversationId == conversation.Id)
                .OrderByDescending(message => message.Timestamp)
                .ThenByDescending(message => message.Id)
                .Select(message => message.Direction)
                .FirstOrDefault() == "Incoming")
        .OrderBy(conversation => conversation.LastMessageTimestamp)
        .Take(BatchSize)
        .ToListAsync(cancellationToken);

    private async Task<List<Message>> LoadLatestIncomingMessagesAsync(
        IReadOnlyCollection<Conversation> conversations,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var conversationIds = conversations.Select(conversation => conversation.Id).ToArray();
        var latestMessages = await dbContext.Messages
            .Where(message => conversationIds.Contains(message.ConversationId))
            .GroupBy(message => message.ConversationId)
            .Select(group => group.OrderByDescending(message => message.Timestamp).ThenByDescending(message => message.Id).First())
            .ToListAsync(cancellationToken);

        return latestMessages.Where(message => message.Direction == "Incoming"
            && message.MessageType != "Reaction"
            && message.Timestamp <= cutoff).ToList();
    }

    private async Task<RecoveryContext> LoadRecoveryContextAsync(
        IReadOnlyCollection<Conversation> conversations,
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
            pages);
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
        RecoveryContext recoveryContext)
    {
        var recoveredCount = 0;
        foreach (var message in messages)
        {
            var recoveryEvent = CreateRecoveryEvent(message, recoveryContext);
            if (recoveryEvent is null) continue;
            if (await PublishOnceAsync(recoveryEvent, message)) recoveredCount++;
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
        var metadata = BuildChannelMetadata(conversation, message, sender, recoveryContext.Pages);
        if (conversation.Channel != "WhatsApp" && metadata is null) return null;

        return new MessageAggregatedEvent
        {
            ProjectId = conversation.ProjectId,
            Sender = sender,
            Content = message.Content,
            Channel = conversation.Channel,
            ChannelMetadata = metadata
        };
    }

    private async Task<bool> PublishOnceAsync(MessageAggregatedEvent recoveryEvent, Message message)
    {
        var lockKey = $"ai:unanswered-recovery:{message.ConversationId}:{message.Id}";
        var redisDatabase = redis.GetDatabase();
        var lockAcquired = await redisDatabase.StringSetAsync(
            lockKey,
            DateTime.UtcNow.ToString("O"),
            RetryLockDuration,
            When.NotExists);
        if (!lockAcquired) return false;

        var published = false;
        try
        {
            await eventBus.PublishAsync(recoveryEvent);
            published = true;
            return true;
        }
        finally
        {
            if (!published) await redisDatabase.KeyDeleteAsync(lockKey);
        }
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
        IReadOnlyDictionary<Guid, string> Pages);
}
