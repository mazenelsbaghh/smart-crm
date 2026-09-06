using Microsoft.EntityFrameworkCore;
using Modules.CRM.Domain;
using Modules.GroupAppointments.Domain;
using Shared.Infrastructure;

namespace Modules.CRM.Services;

public sealed class GroupBookingFollowUpLifecycle(AppDbContext db)
{
    public async Task CancelForBookingAsync(GroupAppointmentBooking booking, GroupAppointment group)
    {
        var pending = await db.FollowUps.IgnoreQueryFilters()
            .Where(followUp => followUp.ProjectId == booking.ProjectId
                && (followUp.Status == "Pending" || followUp.Status == "Processing")
                && (followUp.GroupAppointmentBookingId == booking.Id
                    || (followUp.GroupAppointmentBookingId == null
                        && followUp.CustomerId == booking.CustomerId
                        && followUp.AppointmentTime == group.DateTime
                        && (followUp.WhatsAppAccountId == group.WhatsAppAccountId
                            || followUp.WhatsAppAccountId == null))))
            .ToListAsync();
        foreach (var followUp in pending)
        {
            followUp.Status = "Cancelled";
            followUp.UpdatedAt = DateTime.UtcNow;
        }
    }

    public async Task CancelForGroupAsync(GroupAppointment group)
    {
        var bookings = await db.GroupAppointmentBookings.IgnoreQueryFilters()
            .Where(booking => booking.ProjectId == group.ProjectId
                && booking.GroupAppointmentId == group.Id).ToListAsync();
        foreach (var booking in bookings) await CancelForBookingAsync(booking, group);
    }

    public async Task<bool> CanDispatchAsync(FollowUp followUp)
    {
        var cancelled = await db.FollowUps.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(candidate => candidate.Id == followUp.Id
                && candidate.ProjectId == followUp.ProjectId && candidate.Status == "Cancelled");
        if (cancelled) return false;
        if (!followUp.GroupAppointmentBookingId.HasValue) return true;
        return await db.GroupAppointmentBookings.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(booking => booking.Id == followUp.GroupAppointmentBookingId
                && booking.ProjectId == followUp.ProjectId
                && booking.CustomerId == followUp.CustomerId
                && booking.GroupAppointmentId == followUp.GroupAppointmentId
                && booking.GroupAppointment.IsActive
                && booking.GroupAppointment.DateTime == followUp.AppointmentTime);
    }
}
