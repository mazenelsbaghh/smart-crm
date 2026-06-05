using Shared.Domain;
using System;
using System.Collections.Generic;

namespace Modules.GroupAppointments.Domain
{
    public class GroupAppointment : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime DateTime { get; set; }
        public int Capacity { get; set; }
        public bool IsActive { get; set; } = true;

        // Navigation property for bookings
        public ICollection<GroupAppointmentBooking> Bookings { get; set; } = new List<GroupAppointmentBooking>();
    }
}
