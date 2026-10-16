using Modules.GroupAppointments.Domain;
using Modules.Projects.Domain;
using Shared.Infrastructure;

namespace Modules.GroupAppointments.Services;

public static class GroupStaffNotifications
{
    public static string[] AdminPhones(ProjectSettings settings) =>
        settings.GroupAutomationAdminPhones.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Append(settings.GroupAutomationManagerPhone)
            .Select(GroupBookingPhone.Normalize).OfType<string>().Distinct().ToArray();

    public static string[] MemberPhones(ProjectSettings settings, GroupAppointment group) =>
        new[] { group.SupervisorPhone, GroupStaffDirectory.FromSettings(settings).InstructorContacts
            .FirstOrDefault(c => c.Name.Equals(group.InstructorName, StringComparison.OrdinalIgnoreCase))?.Phone }
            .Select(GroupBookingPhone.Normalize).OfType<string>().Distinct().Except(AdminPhones(settings)).ToArray();

    public static string[] ParticipantPhones(ProjectSettings settings, GroupAppointment group) =>
        AdminPhones(settings).Concat(MemberPhones(settings, group)).Distinct().ToArray();

    public static void Enqueue(AppDbContext db, GroupAppointment group, GroupAppointmentBooking booking)
    {
        if (string.IsNullOrWhiteSpace(group.SupervisorPhone)) return;
        db.GroupStaffNotifications.Add(new GroupStaffNotification
        {
            ProjectId = group.ProjectId, GroupAppointmentId = group.Id, BookingId = booking.Id,
            RecipientPhone = group.SupervisorPhone,
            Content = $"حجز جديد في مجموعة {group.Name}\nالاسم: {booking.CustomerName}\nرقم واتساب: +{booking.CustomerPhone}\nhttps://wa.me/{booking.CustomerPhone}"
        });
    }
}
