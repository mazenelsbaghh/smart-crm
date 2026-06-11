using Shared.Domain;
using System;

namespace Modules.GroupAppointments.Domain
{
    public class GroupAppointmentBooking : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid GroupAppointmentId { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;

        // Navigation property for group appointment
        public GroupAppointment GroupAppointment { get; set; } = null!;

        // Attendance & Payment status
        public bool IsAttended { get; set; } = false;
        public bool IsPaid { get; set; } = false;
    }
}
