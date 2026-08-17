using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.Conversations.Services;
using Shared.Infrastructure;

namespace Modules.Conversations.Jobs;

public sealed class WhatsAppLidContactRecoveryJob(
    AppDbContext dbContext,
    ILogger<WhatsAppLidContactRecoveryJob> logger)
{
    private const int BatchSize = 200;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var customerIds = await LoadLidCustomerIdsAsync(cancellationToken);
        var recoveredCount = 0;

        foreach (var customerIdBatch in customerIds.Chunk(BatchSize))
        {
            recoveredCount += await RecoverBatchAsync(customerIdBatch, cancellationToken);
        }

        if (recoveredCount > 0)
            logger.LogInformation("Recovered real phone details for {Count} WhatsApp LID customers", recoveredCount);
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

        foreach (var customer in customers.Where(customer => recoveredContacts.ContainsKey(customer.Id)))
            ApplyContact(customer, recoveredContacts[customer.Id]);
        foreach (var booking in bookings) ApplyContact(booking, recoveredContacts[booking.CustomerId]);
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

    private sealed record ContactMessage(Guid CustomerId, string Content);
}
