# Data Model: Group Appointments & Follow-up Extensions

This document outlines the modifications made to the database schema for the Smart Customer Core database.

## Modified Entities

### 1. GroupAppointmentBooking

In `Modules.GroupAppointments.Domain.GroupAppointmentBooking`:

```csharp
public class GroupAppointmentBooking : AuditableEntity, ITenantEntity
{
    // Existing fields
    public Guid ProjectId { get; set; }
    public Guid GroupAppointmentId { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public GroupAppointment GroupAppointment { get; set; } = null!;

    // [NEW] Attendance & Payment Tracking
    public bool IsAttended { get; set; } = false;
    public bool IsPaid { get; set; } = false;
}
```

### 2. FollowUp

In `Modules.CRM.Domain.FollowUp`:

```csharp
public class FollowUp : AuditableEntity, ITenantEntity
{
    // Existing fields
    public Guid ProjectId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = "Pending";
    public string Notes { get; set; }
    public string Type { get; set; } = "Nurturing";
    public DateTime? AppointmentTime { get; set; }

    // [NEW] Follow-up Tone Style
    public string Tone { get; set; } = "Default"; // "Default", "Creative", "Salesy"
}
```

## Database Migration Details

A new EF Core migration will be created:
`AddGroupBookingAttendanceAndFollowUpTone`

This migration will run SQL statements to add columns to:
1. `GroupAppointmentBookings` table:
   - `IsAttended` (boolean, default false, not null)
   - `IsPaid` (boolean, default false, not null)
2. `FollowUps` table:
   - `Tone` (text, default 'Default', not null)
