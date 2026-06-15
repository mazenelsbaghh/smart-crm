# Research: Group Appointments & Follow-up Extensions

## Decisions & Solutions

### 1. Disabling Auto Read Receipt ("seen" status)
- **Problem**: Incoming messages are immediately marked as read by the gateway via `sock.readMessages([msg.key])`, causing blue ticks to show on the user's phone.
- **Solution**: Comment out or remove the `await sock.readMessages([msg.key]);` block inside `whatsapp-gateway/src/baileys-manager.js`. Webhook handling and media downloads will continue normally without notifying the sender.
- **Alternatives Considered**: Making it a per-project setting. However, the request specifies a flat change ("عايزين نشيل السين"), so we will simply remove/disable it directly.

### 2. Single Group Booking Limitation
- **Problem**: Students can register in multiple groups.
- **Solution**: Add an EF Core query in `BookGroupSlot` endpoint to check if `GroupAppointmentBookings.AnyAsync` is true for the current `ProjectId` and `CustomerPhone`. If so, reject the request with `BadRequest`.
- **Note**: The check should be project-wide (`b.ProjectId == request.ProjectId && b.CustomerPhone == cleanPhone`) to prevent registering in different groups under the same project.

### 3. Database Schema Extensions (Data Model)
- **Changes**:
  1. Add `IsAttended` (bool, default false) and `IsPaid` (bool, default false) to `GroupAppointmentBooking`.
  2. Add `Tone` (string, default "Default") to `FollowUp`.
- **Migration**: Run a standard EF Core migration to update PostgreSQL database.

### 4. AI Auto-Reply Suppression
- **Problem**: Stop AI replies if the student has paid.
- **Solution**: In `AIReplyWorker.cs` where project settings and customer are loaded, query the database:
  ```csharp
  var isPaid = await dbContext.GroupAppointmentBookings
      .AnyAsync(b => b.CustomerId == customer.Id && b.IsPaid);
  ```
  If `isPaid` is true, log the bypass, complete pending follow-ups, and exit.

### 5. Tone Options and Attendance in Follow-Up Message Rewrite
- **Problem**: Follow-ups should adapt if a student has attended, and support "Creative" or "Salesy" tones.
- **Solution**:
  - Update `IAIMarketingBrain.RewriteFollowUpNotesAsync` to accept `bool hasAttended` and `string? tone` (or `style`).
  - In `AIMarketingBrain.cs`:
    - If `hasAttended` is true, append Egyptian Arabic instructions:
      `تنبيه هام: هذا الطالب حضر الحصة بالفعل. يجب أن تكون الرسالة ترحيبية وتثني على حضوره وتبني على ذلك.`
    - If `tone` is `"Creative"`, append:
      `الأسلوب المطلوب: أسلوب إبداعي ومبتكر وجذاب ومؤثر.`
    - If `tone` is `"Salesy"`, append:
      `الأسلوب المطلوب: أسلوب سلزجي صايع وذكي ومقنع، يركز على الفوائد ويحث على اتخاذ قرار فوري بذكاء.`
  - In `FollowUpScheduler.cs` and `CRMController.cs` (in `SendFollowUp`):
    - Check if the student has any booking marked as attended.
    - Pass the attendance status and `followUp.Tone` to the rewrite service.

### 6. CSV Export
- **Solution**: Implement client-side CSV export in `GroupAppointmentsManager.tsx` by generating the CSV content with `\uFEFF` (UTF-8 BOM) to support Arabic characters in Microsoft Excel, then downloading as a file blob.

### 7. AI Group Mode & Existence Instruction Injection
- **Problem**: The AI auto-reply system might claim groups do not exist because they are omitted when they are full, and online students are sometimes told they can attend in the center normally.
- **Solution**:
  1. Retrieve all active groups (both available and full) from the database in `AIReplyWorker.cs`.
  2. For available groups, list them normally under "المجموعات المتاحة حالياً".
  3. For full groups, list them under "المجموعات مكتملة العدد حالياً" and specify that they are full.
  4. Inject strict prompt guidelines indicating:
     - All online groups are online-only (Zoom/Meet/etc.). Under no circumstances should online students be told they can attend physically in the center.
     - All offline groups are center-only (no online streaming/attendance).
     - Full groups are closed for registration (do not suggest booking them), but they *do exist*. The AI must not say a group is missing if it is in the list of full groups, but rather explain that it is currently full.
