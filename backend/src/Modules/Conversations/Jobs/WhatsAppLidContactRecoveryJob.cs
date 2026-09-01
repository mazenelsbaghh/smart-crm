using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Services;
using Modules.Conversations.Domain;
using Modules.Conversations.Services;
using Modules.WhatsApp.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using StackExchange.Redis;

namespace Modules.Conversations.Jobs;

public sealed class WhatsAppLidContactRecoveryJob(
    AppDbContext dbContext,
    IEventBus eventBus,
    IConnectionMultiplexer redis,
    WhatsAppGatewaySessionClient whatsAppGateway,
    WhatsAppCustomerMergeService customerMerge,
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
        var customerIds = await LoadLidCustomerIdsAsync(cancellationToken);
        var recoveredCount = 0;

        foreach (var customerIdBatch in customerIds.Chunk(RecoveryBatchSize))
        {
            recoveredCount += await RecoverBatchAsync(customerIdBatch, cancellationToken);
        }

        if (recoveredCount > 0)
            logger.LogInformation("Recovered real phone details for {Count} WhatsApp LID customers", recoveredCount);

        var requestedCount = await RequestMissingPhoneNumbersAsync(cancellationToken);
        if (requestedCount > 0)
            logger.LogInformation("Requested real phone numbers from {Count} WhatsApp LID customers", requestedCount);
    }

    private Task<List<Guid>> LoadLidCustomerIdsAsync(CancellationToken cancellationToken) => dbContext.Customers
        .IgnoreQueryFilters()
        .Where(customer => customer.PhoneNumber.Contains("@lid")
            || customer.PhoneNumber.StartsWith("lid@")
            || (customer.PhoneNumber == string.Empty && customer.WhatsAppLid != null))
        .Select(customer => customer.Id)
        .ToListAsync(cancellationToken);

    private async Task<int> RecoverBatchAsync(Guid[] customerIds, CancellationToken cancellationToken)
    {
        var customers = await dbContext.Customers
            .IgnoreQueryFilters()
            .Where(customer => customerIds.Contains(customer.Id))
            .ToListAsync(cancellationToken);
        var messages = await LoadCandidateMessagesAsync(customerIds, cancellationToken);
        var recoveredContacts = FindRecoveredContacts(customers, messages);
        if (recoveredContacts.Count == 0) return 0;

        await UpdateCustomersAndBookingsAsync(customers, recoveredContacts, cancellationToken);
        return recoveredContacts.Count;
    }

    private Task<List<ContactMessage>> LoadCandidateMessagesAsync(
        Guid[] customerIds,
        CancellationToken cancellationToken) => dbContext.Messages
        .Join(
            dbContext.Conversations.IgnoreQueryFilters(),
            message => message.ConversationId,
            conversation => conversation.Id,
            (message, conversation) => new { message, conversation })
        .Where(row => customerIds.Contains(row.conversation.CustomerId)
            && row.message.Direction == "Incoming"
            && (row.message.Content.Contains("01")
                || row.message.Content.Contains("٠١")
                || row.message.Content.Contains("۰۱")))
        .OrderByDescending(row => row.message.Timestamp)
        .Select(row => new ContactMessage(row.conversation.CustomerId, row.message.Content))
        .ToListAsync(cancellationToken);

    private static Dictionary<Guid, WhatsAppSharedContact> FindRecoveredContacts(
        IEnumerable<Customer> customers,
        IReadOnlyCollection<ContactMessage> messages)
    {
        var messagesByCustomer = messages
            .GroupBy(message => message.CustomerId)
            .ToDictionary(group => group.Key, group => group.Select(message => message.Content));
        var recoveredContacts = new Dictionary<Guid, WhatsAppSharedContact>();

        foreach (var customer in customers)
        {
            if (!messagesByCustomer.TryGetValue(customer.Id, out var customerMessages)) continue;
            var contact = customerMessages.Select(WhatsAppSharedContactParser.ExtractOwnContact).FirstOrDefault(value => value != null);
            if (contact != null) recoveredContacts[customer.Id] = contact;
        }
        return recoveredContacts;
    }

    private async Task UpdateCustomersAndBookingsAsync(
        IReadOnlyCollection<Customer> customers,
        IReadOnlyDictionary<Guid, WhatsAppSharedContact> recoveredContacts,
        CancellationToken cancellationToken)
    {
        var customerIds = recoveredContacts.Keys.ToArray();
        var bookings = await dbContext.GroupAppointmentBookings
            .IgnoreQueryFilters()
            .Where(booking => customerIds.Contains(booking.CustomerId))
            .ToListAsync(cancellationToken);
        foreach (var booking in bookings)
        {
            if (recoveredContacts.TryGetValue(booking.CustomerId, out var contact))
                ApplyContact(booking, contact);
        }

        foreach (var customer in customers.Where(customer => recoveredContacts.ContainsKey(customer.Id)))
        {
            var contact = recoveredContacts[customer.Id];
            var canonicalCustomer = await customerMerge.BindPhoneAsync(
                customer.ProjectId,
                customer.Id,
                contact.PhoneNumber,
                cancellationToken);
            ApplyContact(canonicalCustomer, contact);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyContact(Customer customer, WhatsAppSharedContact contact)
    {
        customer.PhoneNumber = contact.PhoneNumber;
        if (contact.Name != null) customer.Name = contact.Name;
        customer.UpdatedAt = DateTime.UtcNow;
    }

    private static void ApplyContact(Modules.GroupAppointments.Domain.GroupAppointmentBooking booking, WhatsAppSharedContact contact)
    {
        booking.CustomerPhone = contact.PhoneNumber;
        if (contact.Name != null) booking.CustomerName = contact.Name;
        booking.UpdatedAt = DateTime.UtcNow;
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
                && row.conversation.Status != "Closed"
                && !row.customer.IsBlacklisted
                && (row.customer.PhoneNumber.Contains("@lid")
                    || row.customer.PhoneNumber.StartsWith("lid@")
                    || (row.customer.PhoneNumber == string.Empty && row.customer.WhatsAppLid != null))
                && !dbContext.GroupAppointmentBookings.IgnoreQueryFilters()
                    .Any(booking => booking.CustomerId == row.customer.Id && booking.IsPaid)
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

    private sealed record ContactMessage(Guid CustomerId, string Content);
    private sealed record SolicitationCandidate(
        Guid CustomerId,
        Guid ProjectId,
        Guid ConversationId,
        Guid WhatsAppAccountId,
        DateTime CreatedAt,
        string Destination);
}
