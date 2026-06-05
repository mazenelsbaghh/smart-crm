# Data Model Modifications: Implement Missing Core Features

This document outlines the entity and schema modifications for the database.

## 1. Knowledge Base Module

### `KnowledgeApprovalStatus` Enum
```csharp
namespace Modules.Brain.Domain
{
    public enum KnowledgeApprovalStatus
    {
        Draft = 0,
        PendingApproval = 1,
        Approved = 2,
        Rejected = 3
    }
}
```

### `KnowledgeDocument` Entity Updates
Add `ApprovalStatus` property to `KnowledgeDocument` (`src/Modules/Brain/Domain/KnowledgeDocument.cs`):
```csharp
public KnowledgeApprovalStatus ApprovalStatus { get; set; } = KnowledgeApprovalStatus.Approved; // Default for existing docs
```

---

## 2. Conversations Module (NotificationAlert)

We will use the existing `NotificationAlert` entity (`src/Modules/Conversations/Domain/NotificationAlert.cs`) for the workflow `SendAlert` action.

### `NotificationAlert` Structure (Existing reference)
- `Id` (Guid)
- `ProjectId` (Guid)
- `Title` (string)
- `Message` (string)
- `Severity` (string: Info, Warning, Error, VIP, Sentiment)
- `IsRead` (bool)
- `CreatedAt` (DateTime)
