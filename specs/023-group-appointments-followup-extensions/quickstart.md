# Quickstart: Group Appointments & Follow-up Extensions

This guide outlines how to run, apply migrations, and verify the new features locally.

## Prerequisite Commands

1. Make sure you have the environment set up and Docker containers running:
   ```bash
   make up
   ```

2. Add the EF Core migration for the database schema updates:
   ```bash
   dotnet ef migrations add AddGroupBookingAttendanceAndFollowUpTone --project backend
   ```

3. Apply migrations to the running database:
   ```bash
   make db-migrate
   ```

## Local Verification Steps

### 1. WhatsApp Seen Receipt Verification
- Open `whatsapp-gateway/src/baileys-manager.js`.
- Send an incoming message to the registered project WhatsApp number.
- Verify that the message reaches the backend (check backend logs or SignalR updates) but does NOT trigger a double blue checkmark on the sending phone (since seen marking is commented out).

### 2. Single Group Booking Constraint
- Perform a public group booking via:
  `POST /api/public/group-appointments/book`
  With:
  ```json
  {
    "projectId": "YOUR_PROJECT_ID",
    "groupAppointmentId": "GROUP_A_ID",
    "customerName": "Test Student",
    "customerPhone": "201000000000"
  }
  ```
- Attempt to book the same student in another group:
  `POST /api/public/group-appointments/book`
  With:
  ```json
  {
    "projectId": "YOUR_PROJECT_ID",
    "groupAppointmentId": "GROUP_B_ID",
    "customerName": "Test Student",
    "customerPhone": "201000000000"
  }
  ```
- The second request must return a `400 Bad Request` with an error message.

### 3. Attendance & Payment Status Toggles
- Open the web dashboard, navigate to **Settings** -> **Addons** -> **Group Appointments Manager**.
- Select a group with active bookings.
- Verify that the bookings list shows checkboxes/toggles for "حضور" (Attended) and "دفع" (Paid).
- Toggle them and reload to verify that status is persisted.

### 4. AI reply Suppression on Paid
- Mark a booking as Paid.
- Send a WhatsApp message from that customer's phone number.
- Check backend logs: AI reply worker must output that the AI reply is skipped for this paid customer.

### 5. Follow-Up Message Rewrite & Tone Styles
- Mark a student booking as Attended.
- Create a follow-up for this customer.
- Set the style of the follow-up to "Salesy" or "Creative".
- Manually trigger or schedule sending the follow-up.
- Verify that the sent message includes references to their attendance and matches the selected tone (Creative or Salesy).
