# Tasks: Group Appointments Add-on (إضافة مواعيد المجموعات)

**Input**: Design documents from `/specs/021-group-appointments/`

**Prerequisites**: plan.md (required), spec.md (required for user stories)

## Spec Kit Preparation Workflow
- [x] Phase 1: Feature Specification (`speckit-specify`) completed
- [x] Phase 2: Technical Planning (`speckit-plan`) completed
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`) completed

---

## Phase 1: Setup & Foundational Database Layer

**Purpose**: Establish database models and migrations.

- [ ] T001 In file `backend/src/Modules/Projects/Domain/ProjectSettings.cs`, add property:
  `public bool IsGroupAppointmentsEnabled { get; set; } = false;`
- [ ] T002 In folder `backend/src/Modules/GroupAppointments/Domain/`, create [NEW] file [GroupAppointment.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/GroupAppointments/Domain/GroupAppointment.cs) with properties:
  - `ProjectId` (Guid)
  - `Name` (string)
  - `DateTime` (DateTime)
  - `Capacity` (int)
  - `IsActive` (bool = true)
  - Navigation: `ICollection<GroupAppointmentBooking> Bookings = new List<GroupAppointmentBooking>()`
  - It must inherit from `AuditableEntity` and implement `ITenantEntity`.
- [ ] T003 In folder `backend/src/Modules/GroupAppointments/Domain/`, create [NEW] file [GroupAppointmentBooking.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/GroupAppointments/Domain/GroupAppointmentBooking.cs) with properties:
  - `ProjectId` (Guid)
  - `GroupAppointmentId` (Guid)
  - `CustomerId` (Guid)
  - `CustomerName` (string)
  - `CustomerPhone` (string)
  - Navigation: `GroupAppointment GroupAppointment`
  - It must inherit from `AuditableEntity` and implement `ITenantEntity`.
- [ ] T004 In file `backend/src/Shared/Infrastructure/AppDbContext.cs`:
  - Add `DbSet<Modules.GroupAppointments.Domain.GroupAppointment> GroupAppointments { get; set; }`
  - Add `DbSet<Modules.GroupAppointments.Domain.GroupAppointmentBooking> GroupAppointmentBookings { get; set; }`
  - In `OnModelCreating`, configure the cascade relationship:
    ```csharp
    modelBuilder.Entity<Modules.GroupAppointments.Domain.GroupAppointment>()
        .HasMany(g => g.Bookings)
        .WithOne(b => b.GroupAppointment)
        .HasForeignKey(b => b.GroupAppointmentId)
        .OnDelete(DeleteBehavior.Cascade);
    ```
- [ ] T005 Run command:
  `dotnet ef migrations add AddGroupAppointments --project backend/backend.csproj`
  to generate the Entity Framework Core migration.
- [ ] T006 Apply migrations to the database by running `dotnet ef database update --project backend/backend.csproj` or restarting containers.

**Checkpoint**: Foundation ready - DB tables are created and schema contains GroupAppointment and Booking definitions.

---

## Phase 2: Backend API Endpoints & Business Logic

**Purpose**: Implement controllers for managing group appointments and processing bookings.

- [ ] T007 In folder `backend/src/Modules/GroupAppointments/API/`, create [NEW] file [GroupAppointmentsController.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/GroupAppointments/API/GroupAppointmentsController.cs) inheriting from `ControllerBase`, decorated with `[ApiController]` and route `[route("api/group-appointments")]`. Include endpoints:
  - `GET /`: Lists all group appointments for the current tenant (`ProjectId` from context), including current booking counts.
  - `POST /`: Creates a new group appointment.
  - `PUT /{id}`: Updates name, dateTime, capacity, or isActive for a group.
  - `DELETE /{id}`: Deletes a group.
- [ ] T008 Add public booking endpoints in `GroupAppointmentsController.cs` decorated with `[AllowAnonymous]` under route prefix `api/public/group-appointments`:
  - `GET /active/{projectId}`: Return active future groups and their remaining slots. Does:
    `_tenantContext.SetProjectId(projectId);` and returns matching slots.
  - `POST /book`: Action accepts: `ProjectId`, `GroupAppointmentId`, `CustomerName`, `CustomerPhone`. Performs safety checks:
    - Sets project context: `_tenantContext.SetProjectId(request.ProjectId)`.
    - Check settings: `var settings = await _context.ProjectSettings.FindAsync(request.ProjectId)`. Return bad request if disabled.
    - Check capacity: Count existing bookings for group. If >= capacity, return `BadRequest(new { error = "المجموعة ممتلئة" })`.
    - Check duplicate: If customer phone is already booked, return `BadRequest(new { error = "أنت مسجل بالفعل في هذه المجموعة" })`.
    - Resolves customer: Look for Customer in the database with the phone number under this project. If not found, create new `Customer` record with name, phone, tag "حجز مجموعة", and notes.
    - Add `GroupAppointmentBooking` and save.
    - Broadcast real-time `ReceiveNotification` alert for agents: "قام [Name] بحجز موعد في [Group]" using `_hubContext`.
    - Broadcast `CustomerUpdated` event via SignalR.
- [ ] T009 Update `backend/src/Modules/Projects/API/ProjectController.cs` to include `IsGroupAppointmentsEnabled` in settings retrieval (`GetSettings`) and update (`UpdateSettings`) endpoints.

---

## Phase 3: Frontend Add-on Switch & Configuration views

**Purpose**: Implement Settings / Add-on management views in CRM.

- [ ] T010 Create new file `frontend/src/packages/settings/Addons.tsx` implementing a toggle panel for "Group Appointments" (مواعيد المجموعات).
  - Toggles `isGroupAppointmentsEnabled` using `ProjectController` settings endpoints.
- [ ] T011 Create new file `frontend/src/packages/settings/GroupAppointmentsManager.tsx` enabling:
  - Form to add new groups (Name, Date/Time, Capacity, IsActive).
  - Table of created groups with progress bar (Booked/Capacity), edit, and delete buttons.
  - Sub-table or modal listing customer bookings (Name, Phone, Registered Date) for a selected group.
- [ ] T012 In file `frontend/src/packages/settings/Settings.tsx` (or settings routing), register the "Add-ons" (الاضافات) view as a tab/section.

---

## Phase 4: Frontend Public Booking Page

**Purpose**: Implement public customer-facing scheduling.

- [ ] T013 Create [NEW] Next.js route: `frontend/src/app/booking/[projectId]/page.tsx`
  - Safe extraction of `projectId` from URL.
  - Fetches active slots from `/api/public/group-appointments/active/{projectId}`.
  - UI styled in Arabic, using elegant dark/glassmorphic look:
    - Lists slots. If slot count == capacity, disable selection, label "مكتملة" (Full).
    - Form inputs: Customer Name, WhatsApp Number.
    - Handles post request to `/api/public/group-appointments/book`.
    - Displays validation error "المجموعة ممتلئة" or success feedback modal.

---

## Phase 5: Verification & Polish

**Purpose**: Ensure compiler checks, tests, and code quality.

- [ ] T014 Compile backend project and resolve any warnings.
- [ ] T015 Verify React builds cleanly with `npm run build`.
- [ ] T016 Verify booking functionality manually: Toggle addon, add a group, book it, verify customer creation and agent SignalR notifications.
