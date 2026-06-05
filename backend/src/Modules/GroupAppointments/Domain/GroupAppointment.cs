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
        public string Days { get; set; } = string.Empty; // Comma-separated day indices: 0=Sun,1=Mon,2=Tue,3=Wed,4=Thu,5=Fri,6=Sat

        // Navigation property for bookings
        public ICollection<GroupAppointmentBooking> Bookings { get; set; } = new List<GroupAppointmentBooking>();
    }
}
