using Microsoft.EntityFrameworkCore;
using Modules.CRM.Domain;
using Modules.GroupAppointments.Services;
using Shared.Infrastructure;

namespace Modules.CRM.Services;

public sealed record CustomerExportRow(string PhoneNumber, string Name, string City, string? Label);

public sealed class CustomerExportQuery(AppDbContext context)
{
    public async Task<IReadOnlyList<CustomerExportRow>> GetCustomersAsync(
        Guid projectId, DateTime? cutoff, CancellationToken cancellationToken)
    {
        var customers = await context.Customers.AsNoTracking().Where(customer => customer.ProjectId == projectId)
            .Select(customer => new
            {
                customer.Id, customer.PhoneNumber, customer.Name, customer.City, customer.Label,
                Excluded = customer.IsBlacklisted || customer.Label == "المحظورين للدفع"
                    || customer.Tags.Contains("المحظورين للدفع")
                    || context.Deals.Any(deal => deal.ProjectId == projectId
                        && deal.CustomerId == customer.Id && deal.Status == DealStatus.Won)
            }).ToListAsync(cancellationToken);
        var excludedIds = customers.Where(customer => customer.Excluded).Select(customer => customer.Id).ToHashSet();
        excludedIds.UnionWith(await GetRecentCustomerIdsAsync(projectId, cutoff, cancellationToken));
        var paidBookings = await context.GroupAppointmentBookings.AsNoTracking()
            .Where(booking => booking.ProjectId == projectId && booking.IsPaid)
            .Select(booking => new { booking.CustomerId, booking.CustomerPhone }).ToListAsync(cancellationToken);
        excludedIds.UnionWith(paidBookings.Select(booking => booking.CustomerId));
        var excludedPhones = paidBookings.Select(booking => GroupBookingPhone.Normalize(booking.CustomerPhone))
            .Where(phone => phone != null).ToHashSet();
        var identities = await context.WhatsAppPhoneCustomerIdentities.AsNoTracking()
            .Where(identity => identity.ProjectId == projectId)
            .Select(identity => new { identity.CustomerId, identity.NormalizedPhone }).ToListAsync(cancellationToken);
        excludedPhones.UnionWith(customers.Where(customer => excludedIds.Contains(customer.Id))
            .Select(customer => GroupBookingPhone.Normalize(customer.PhoneNumber)).Where(phone => phone != null));
        excludedPhones.UnionWith(identities.Where(identity => excludedIds.Contains(identity.CustomerId))
            .Select(identity => GroupBookingPhone.Normalize(identity.NormalizedPhone)).Where(phone => phone != null));
        return customers.Where(customer => !excludedIds.Contains(customer.Id))
            .Select(customer => new CustomerExportRow(GroupBookingPhone.Normalize(customer.PhoneNumber)!,
                customer.Name, customer.City, customer.Label))
            .Where(customer => customer.PhoneNumber != null && !excludedPhones.Contains(customer.PhoneNumber))
            .DistinctBy(customer => customer.PhoneNumber).OrderBy(customer => customer.PhoneNumber).ToList();
    }

    private async Task<List<Guid>> GetRecentCustomerIdsAsync(
        Guid projectId, DateTime? cutoff, CancellationToken cancellationToken)
    {
        if (cutoff == null) return [];
        // The boundary is inclusive; closed conversations and both message directions still count as contact.
        return await context.Conversations.AsNoTracking().Where(conversation => conversation.ProjectId == projectId
            && (conversation.CreatedAt >= cutoff || conversation.LastMessageTimestamp >= cutoff
                || context.Messages.Any(message => message.ConversationId == conversation.Id && message.Timestamp >= cutoff)))
            .Select(conversation => conversation.CustomerId).Distinct().ToListAsync(cancellationToken);
    }
}
