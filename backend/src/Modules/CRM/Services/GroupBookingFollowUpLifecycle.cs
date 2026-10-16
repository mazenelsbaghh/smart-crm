using Microsoft.EntityFrameworkCore;
using Modules.CRM.Domain;
using Modules.GroupAppointments.Domain;
using Shared.Infrastructure;

namespace Modules.CRM.Services;

public sealed class GroupBookingFollowUpLifecycle(AppDbContext db)
{
    public static void RestoreScheduled(GroupAppointment group, IReadOnlyCollection<GroupAppointmentBooking> bookings,
        IReadOnlyCollection<FollowUp> followUps)
    {
        if (!group.IsActive) return;
        foreach (var followUp in followUps.Where(item => item.Status == "Cancelled"))
        {
            var stillBooked = bookings.Any(booking => booking.Id == followUp.GroupAppointmentBookingId
                || (followUp.GroupAppointmentBookingId == null && booking.CustomerId == followUp.CustomerId));
            var appointment = followUp.Type is GroupSessionPreparationScheduler.VideoType or GroupSessionPreparationScheduler.SiteType
                ? group.FreeSessionDateTime ?? group.DateTime : group.DateTime;
            if (!stillBooked || followUp.AppointmentTime != appointment) continue;
            followUp.Status = "Pending";
            if (followUp.DueDate < DateTime.UtcNow) followUp.DueDate = DateTime.UtcNow;
        }
    }

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
        if (GroupSessionPreparationScheduler.IsPreparation(followUp))
        {
            var sessionUtc = await db.GroupAppointments.IgnoreQueryFilters().AsNoTracking()
                .Where(group => group.Id == followUp.GroupAppointmentId && group.ProjectId == followUp.ProjectId)
                .Select(group => (DateTime?)(group.FreeSessionDateTime ?? group.DateTime)).SingleOrDefaultAsync();
            if (!sessionUtc.HasValue || sessionUtc <= DateTime.UtcNow) return false;
        }
        // Groups expire after 24 hours; their two-day post-session follow-up must survive that expiry.
        var isPostSession = followUp.Type == "Nurturing"
            && followUp.AppointmentTime < DateTime.UtcNow
            && followUp.DueDate > followUp.AppointmentTime;
        return await db.GroupAppointmentBookings.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(booking => booking.Id == followUp.GroupAppointmentBookingId
                && booking.ProjectId == followUp.ProjectId
                && booking.CustomerId == followUp.CustomerId
                && booking.GroupAppointmentId == followUp.GroupAppointmentId
                && (booking.GroupAppointment.IsActive || isPostSession)
                && ((followUp.Type == GroupSessionPreparationScheduler.VideoType
                        || followUp.Type == GroupSessionPreparationScheduler.SiteType)
                    ? booking.GroupAppointment.FreeSessionDateTime ?? booking.GroupAppointment.DateTime
                    : booking.GroupAppointment.DateTime) == followUp.AppointmentTime);
    }
}
