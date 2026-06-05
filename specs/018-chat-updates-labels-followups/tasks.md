# Task Checklist: Real-time Chat Sync, AI Labeling Flexibility, and Follow-up Automation Rules

## Spec Kit Preparation Workflow
- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Technical Planning (`speckit-plan`)
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`)

---

## Tasks

### Component 1: Frontend Inbox Integration

- [ ] **Task FE-001 (Dynamic Sorting & Live Fetching)**:
  - **File**: `frontend/src/packages/inbox/Inbox.tsx`
  - **Action**: Modify the SignalR `ReceiveMessage` listener callback in `initSignalR` (around lines 249-278) to check if the incoming message's `conversationId` exists in the `conversations` list.
  - **Code Change**:
    ```typescript
    // Inside registerOnMessage callback:
    const exists = prev.some((c) => c.id === message.conversationId);
    if (!exists) {
      fetchConversations();
      return prev;
    }
    ```
  - **Action 2**: In `filteredConversations` calculation (around lines 495-501), add a `.sort` call to sort conversations descending by `lastMessageAt` timestamp.
  - **Code Change**:
    ```typescript
    const filteredConversations = conversations
      .filter((c) => {
        if (!c || !c.customer) return false;
        const statusMatch = filterStatus === 'All' || c.status === filterStatus;
        const nameMatch = (c.customer.name || '').toLowerCase().includes(searchQuery.toLowerCase()) || 
                          (c.customer.phone || '').includes(searchQuery);
        return statusMatch && nameMatch;
      })
      .sort((a, b) => new Date(b.lastMessageAt).getTime() - new Date(a.lastMessageAt).getTime());
    ```
  - **Checkpoint**: Open the UI, send an incoming message from a new customer, and check that the list updates with the new card at the top without a browser refresh.

---

### Component 2: Backend AI Labeling updates

- [ ] **Task AI-001 (Relax System Prompt Labels Rule)**:
  - **File**: `backend/src/Modules/AI/Services/AIMarketingBrain.cs`
  - **Action**: Edit the system prompt string around line 133. Change the strict label requirement.
  - **Code Change**:
    Change:
    ```csharp
    Choose ONE of these exact existing labels for the "label" field. Do not invent a new label if existing labels are provided above.
    ```
    To:
    ```csharp
    Choose ONE of these exact existing labels if it fits the customer's intent perfectly. If none of the existing labels match, or if a more specific/accurate label is needed, you may generate a new short Arabic label (max 3 words) classifying the customer's current state/need.
    ```

- [ ] **Task AI-002 (Refine GeminiClient Mock Fallbacks)**:
  - **File**: `backend/src/Modules/AI/Services/GeminiClient.cs`
  - **Action**: In `GeminiClient.cs` mock logic (around line 129 and 162), update the hardcoded "استفسار عن السعر" default fallback when the message contains "تفاصيل".
  - **Code Change**:
    At line 127-130:
    ```csharp
    if (messageContent.Contains("سعر") || messageContent.Contains("بكام"))
    {
        profileLabel = "استفسار عن السعر";
    }
    else if (messageContent.Contains("تفاصيل"))
    {
        profileLabel = "استفسار عن التفاصيل";
    }
    ```
    At line 159-164:
    ```csharp
    if (customerMessage.Contains("سعر") || customerMessage.Contains("بكام"))
    {
        intent = "inquiry";
        label = "استفسار عن السعر";
        replyContent = "بالتأكيد! تفاصيل السعر هي 500 جنيه مصري، وهناك خصم خاص لفترة محدودة. هل تحب تأكيد الطلب؟";
    }
    else if (customerMessage.Contains("تفاصيل"))
    {
        intent = "inquiry";
        label = "استفسار عن التفاصيل";
        replyContent = "بالتأكيد! تفاصيل الكورس هي كالتالي: الكورس مكثف ويغطي أساسيات الذكاء الاصطناعي وبناء التطبيقات. هل تحب معرفة المزيد؟";
    }
    ```
  - **Checkpoint**: Run the customer memory generation or send a mock message containing "تفاصيل" and check that the generated label is "استفسار عن التفاصيل" instead of "استفسار عن السعر".

---

### Component 3: Backend Follow-up Automation

- [ ] **Task CRM-001 (Automated Follow-up lifecycle on Webhook Inbound)**:
  - **File**: `backend/src/Modules/Conversations/API/WebhookController.cs`
  - **Action**: In `ReceiveMessage` (after saving the incoming message around line 114):
    1. Query all "Pending" follow-ups for this customer and set their status to "Completed".
    2. Add/insert a new default "Pending" follow-up scheduled for `DateTime.UtcNow.AddHours(24)` with Nurturing type and standard notes.
  - **Code Change**:
    ```csharp
    // Complete existing pending follow-ups for this customer
    var pendingFollowUps = await _context.FollowUps
        .IgnoreQueryFilters()
        .Where(f => f.CustomerId == customer.Id && f.Status == "Pending")
        .ToListAsync();

    foreach (var fu in pendingFollowUps)
    {
        fu.Status = "Completed";
        _context.Entry(fu).State = EntityState.Modified;
    }

    // Schedule default follow-up in 24 hours
    var defaultFollowUp = new Modules.CRM.Domain.FollowUp
    {
        Id = Guid.NewGuid(),
        ProjectId = payload.ProjectId,
        CustomerId = customer.Id,
        Type = "Nurturing",
        DueDate = DateTime.UtcNow.AddHours(24),
        Notes = "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟",
        Status = "Pending"
    };

    _context.FollowUps.Add(defaultFollowUp);
    await _context.SaveChangesAsync();
    ```

- [ ] **Task CRM-002 (Automated Follow-up lifecycle on Agent Outbound)**:
  - **File**: `backend/src/Modules/Conversations/API/ConversationController.cs`
  - **Action**: In `SendMessage` (after saving the outgoing message around line 144):
    1. Query all "Pending" follow-ups for this customer and set their status to "Completed".
    2. Add/insert a new default "Pending" follow-up scheduled for `DateTime.UtcNow.AddHours(24)` with Nurturing type.
  - **Code Change**:
    ```csharp
    // Complete existing pending follow-ups for this customer
    var pendingFollowUps = await _context.FollowUps
        .IgnoreQueryFilters()
        .Where(f => f.CustomerId == conversation.CustomerId && f.Status == "Pending")
        .ToListAsync();

    foreach (var fu in pendingFollowUps)
    {
        fu.Status = "Completed";
        _context.Entry(fu).State = EntityState.Modified;
    }

    // Schedule default follow-up in 24 hours
    var defaultFollowUp = new Modules.CRM.Domain.FollowUp
    {
        Id = Guid.NewGuid(),
        ProjectId = conversation.ProjectId,
        CustomerId = conversation.CustomerId,
        Type = "Nurturing",
        DueDate = DateTime.UtcNow.AddHours(24),
        Notes = "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟",
        Status = "Pending"
    };

    _context.FollowUps.Add(defaultFollowUp);
    await _context.SaveChangesAsync();
    ```

- [ ] **Task CRM-003 (Sync AI Suggested Follow-up in CRM Engine)**:
  - **File**: `backend/src/Modules/CRM/Services/CRMAutoUpdateEngine.cs`
  - **Action**: In `ProcessSuggestionAsync` under `// Process Suggested Follow-up` (line 307):
    - Update the logic to modify/use the newly created "Pending" follow-up if `FollowUpNeeded` is `true`.
    - If `FollowUpNeeded` is `false`, mark the existing "Pending" follow-up as "Completed".
  - **Code Change**:
    Change the follow-up processing block to:
    ```csharp
    // Process Suggested Follow-up
    var existingFollowUp = await _context.FollowUps
        .FirstOrDefaultAsync(f => f.CustomerId == @event.CustomerId && f.Status == "Pending");

    if (@event.FollowUpNeeded)
    {
        Console.WriteLine($"[CRMAutoUpdateEngine] Processing suggested follow-up for Customer: {@event.CustomerId}. Type: {@event.FollowUpType}");
        try
        {
            DateTime? appTime = null;
            if (!string.IsNullOrEmpty(@event.FollowUpAppointmentTime) && DateTime.TryParse(@event.FollowUpAppointmentTime, out var parsedAppTime))
            {
                appTime = DateTime.SpecifyKind(parsedAppTime, DateTimeKind.Utc);
            }

            DateTime dueDate = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(@event.FollowUpDueDate) && DateTime.TryParse(@event.FollowUpDueDate, out var parsedDueDate))
            {
                dueDate = DateTime.SpecifyKind(parsedDueDate, DateTimeKind.Utc);
            }

            string followUpType = !string.IsNullOrEmpty(@event.FollowUpType) ? @event.FollowUpType : "Nurturing";

            if (existingFollowUp != null)
            {
                existingFollowUp.Type = followUpType;
                existingFollowUp.AppointmentTime = appTime;
                existingFollowUp.DueDate = dueDate;
                existingFollowUp.Notes = @event.FollowUpNotes ?? string.Empty;
                _context.Entry(existingFollowUp).State = EntityState.Modified;
                Console.WriteLine($"[CRMAutoUpdateEngine] Updated existing pending follow-up {existingFollowUp.Id} with AI context.");
            }
            else
            {
                var newFollowUp = new FollowUp
                {
                    Id = Guid.NewGuid(),
                    ProjectId = @event.ProjectId,
                    CustomerId = @event.CustomerId,
                    Type = followUpType,
                    AppointmentTime = appTime,
                    DueDate = dueDate,
                    Notes = @event.FollowUpNotes ?? string.Empty,
                    Status = "Pending"
                };
                _context.FollowUps.Add(newFollowUp);
                Console.WriteLine($"[CRMAutoUpdateEngine] Created new pending follow-up {newFollowUp.Id} for customer {@event.CustomerId}.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRMAutoUpdateEngine] Error processing suggested follow-up: {ex.Message}");
        }
    }
    else
    {
        if (existingFollowUp != null)
        {
            existingFollowUp.Status = "Completed";
            _context.Entry(existingFollowUp).State = EntityState.Modified;
            Console.WriteLine($"[CRMAutoUpdateEngine] Completed pending follow-up {existingFollowUp.Id} because AI suggested no follow-up is needed.");
        }
    }
    ```
  - **Checkpoint**: Send a customer message. Verify in DB that the pending follow-up is modified to match the AI suggestions if AI is enabled, or completed if the AI says no follow-up is needed.
