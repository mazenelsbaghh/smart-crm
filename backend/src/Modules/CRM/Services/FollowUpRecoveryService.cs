using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Shared.Infrastructure;

namespace Modules.CRM.Services;

public sealed record RecoverFollowUpRequest(string PhoneNumber, DateTime DueDate);
public sealed record FollowUpRecoveryResult(string Code, FollowUp? FollowUp = null, string? CustomerName = null);

public sealed class FollowUpRecoveryService(AppDbContext db)
{
    public async Task<FollowUpRecoveryResult> RecoverAsync(
        Guid projectId, RecoverFollowUpRequest request, CancellationToken cancellationToken = default)
    {
        var phone = GroupBookingPhone.Normalize(request.PhoneNumber);
        if (phone is null) return new("INVALID_PHONE");
        if (request.DueDate.Kind != DateTimeKind.Utc || request.DueDate <= DateTime.UtcNow)
            return new("INVALID_DUE_DATE");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockRecoveryAsync($"follow-up-recovery:{projectId:N}:{phone}", cancellationToken);
        var customers = await MatchingCustomers(projectId, phone).Take(2).ToListAsync(cancellationToken);
        if (customers.Count == 0) return new("CUSTOMER_NOT_FOUND");
        if (customers.Count > 1) return new("AMBIGUOUS_CUSTOMER");

        var recovery = await RecoverCustomerAsync(customers[0], request.DueDate, cancellationToken);
        if (recovery.Code == "CREATED") await transaction.CommitAsync(cancellationToken);
        return recovery;
    }

    private async Task<FollowUpRecoveryResult> RecoverCustomerAsync(
        Customer customer, DateTime dueDate, CancellationToken cancellationToken)
    {
        await LockRecoveryAsync($"follow-up-recovery-customer:{customer.ProjectId:N}:{customer.Id:N}", cancellationToken);
        var blockedReason = await RecoveryBlockedReasonAsync(customer, cancellationToken);
        if (blockedReason is not null) return new(blockedReason);
        var active = await ActiveFollowUpAsync(customer, cancellationToken);
        if (active is not null)
            return new(active.Status == "DeliveryUnknown" ? "DELIVERY_UNKNOWN" : "ALREADY_ACTIVE", active, customer.Name);

        var conversation = await LatestWhatsAppConversationAsync(customer, cancellationToken);
        if (conversation is null || conversation.Status == "Closed"
            || conversation.WhatsAppDestinationId.HasValue || !conversation.WhatsAppAccountId.HasValue)
            return new("NO_SUPPORTED_CONVERSATION");
        if (conversation.WhatsAppDeliveryUnknownAt.HasValue) return new("DELIVERY_UNKNOWN");
        if (conversation.HumanHandoffReplyId.HasValue) return new("HUMAN_HANDOFF_PENDING");

        var followUp = CreateRecovery(customer, conversation, dueDate);
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync(cancellationToken);
        return new("CREATED", followUp, customer.Name);
    }

    private Task<int> LockRecoveryAsync(string lockIdentity, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockIdentity}, 0))", cancellationToken);

    private IQueryable<Customer> MatchingCustomers(Guid projectId, string phone) =>
        db.Customers.IgnoreQueryFilters().Where(customer => customer.ProjectId == projectId
            && (EF.Property<string>(customer, GroupBookingPhoneFields.CustomerCanonical) == phone
                || db.WhatsAppPhoneCustomerIdentities.IgnoreQueryFilters().Any(identity =>
                    identity.ProjectId == projectId && identity.CustomerId == customer.Id
                    && identity.NormalizedPhone == phone)
                || db.GroupAppointmentBookings.IgnoreQueryFilters().Any(booking =>
                    booking.ProjectId == projectId && booking.CustomerId == customer.Id
                    && EF.Property<string>(booking, GroupBookingPhoneFields.BookingCanonical) == phone)));

    private async Task<string?> RecoveryBlockedReasonAsync(Customer customer, CancellationToken cancellationToken)
    {
        if (customer.IsBlacklisted) return "CUSTOMER_BLOCKED";
        if (ReminderDisabled(customer.AutomationRules)) return "REMINDERS_DISABLED";
        if (GroupBookingPhone.Normalize(customer.PhoneNumber) is null) return "CUSTOMER_HAS_NO_PHONE";
        if (await db.GroupAppointmentBookings.IgnoreQueryFilters().AnyAsync(booking =>
                booking.ProjectId == customer.ProjectId && booking.CustomerId == customer.Id && booking.IsPaid,
                cancellationToken))
            return "CUSTOMER_PAID";
        return null;
    }

    private static bool ReminderDisabled(string? automationRules)
    {
        if (string.IsNullOrWhiteSpace(automationRules)) return false;
        try
        {
            using var rules = JsonDocument.Parse(automationRules);
            return rules.RootElement.ValueKind != JsonValueKind.Object
                || (rules.RootElement.TryGetProperty("whatsappReminder24h", out var reminder)
                    && reminder.ValueKind != JsonValueKind.True);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private Task<FollowUp?> ActiveFollowUpAsync(Customer customer, CancellationToken cancellationToken) =>
        db.FollowUps.IgnoreQueryFilters().Where(followUp =>
                followUp.ProjectId == customer.ProjectId && followUp.CustomerId == customer.Id
                && (followUp.Channel == "WhatsApp" || followUp.Channel == null)
                && (followUp.Status == "Pending" || followUp.Status == "Processing"
                    || followUp.Status == "DeliveryUnknown"))
            .OrderByDescending(followUp => followUp.Status == "DeliveryUnknown")
            .ThenBy(followUp => followUp.DueDate).FirstOrDefaultAsync(cancellationToken);

    private Task<Conversation?> LatestWhatsAppConversationAsync(Customer customer, CancellationToken cancellationToken) =>
        db.Conversations.IgnoreQueryFilters()
            .Where(conversation => conversation.ProjectId == customer.ProjectId
                && conversation.CustomerId == customer.Id && conversation.Channel == "WhatsApp")
            .OrderByDescending(conversation => conversation.LastMessageTimestamp)
            .ThenByDescending(conversation => conversation.Id).FirstOrDefaultAsync(cancellationToken);

    private static FollowUp CreateRecovery(Customer customer, Conversation conversation, DateTime dueDate) => new()
    {
        ProjectId = customer.ProjectId,
        CustomerId = customer.Id,
        ConversationId = conversation.Id,
        WhatsAppAccountId = conversation.WhatsAppAccountId,
        Channel = "WhatsApp",
        ActiveAutomationSlotKey = $"manual-recovery:{customer.Id:N}",
        DueDate = dueDate,
        Status = "Pending",
        Type = "Nurturing",
        Tone = "Default",
        Notes = string.Empty
    };
}
