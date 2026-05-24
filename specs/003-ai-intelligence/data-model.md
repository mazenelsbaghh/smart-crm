# Database Schemas & Data Model Updates

The following tables are added or modified to support Phase 2:

## 1. Modifying Existing Entities

### Customer Table Updates
Ensure the Customer entity (created in Phase 1) has the following fields:
```sql
ALTER TABLE "Customers" ADD COLUMN "City" VARCHAR(100) NULL;
ALTER TABLE "Customers" ADD COLUMN "LeadScore" INT DEFAULT 0;
ALTER TABLE "Customers" ADD COLUMN "Tags" TEXT[] NULL;
ALTER TABLE "Customers" ADD COLUMN "Notes" TEXT NULL;
ALTER TABLE "Customers" ADD COLUMN "Budget" DECIMAL(18,2) NULL;
ALTER TABLE "Customers" ADD COLUMN "Interests" TEXT[] NULL;
```

---

## 2. New Entities

### CRMUpdateProposals Table
Stores AI-suggested customer info updates for auditing and manual approval.
```sql
CREATE TABLE "CRMUpdateProposals" (
    "Id" UUID PRIMARY KEY,
    "CustomerId" UUID NOT NULL REFERENCES "Customers"("Id") ON DELETE CASCADE,
    "FieldName" VARCHAR(100) NOT NULL,
    "SuggestedValue" TEXT NOT NULL,
    "ConfidenceScore" DOUBLE PRECISION NOT NULL,
    "Status" VARCHAR(50) NOT NULL, -- 'Applied', 'PendingApproval', 'Rejected'
    "ProjectId" UUID NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL
);
```

### NotificationAlerts Table
Stores historical alerts pushed to project users.
```sql
CREATE TABLE "NotificationAlerts" (
    "Id" UUID PRIMARY KEY,
    "UserId" UUID NOT NULL,
    "Message" TEXT NOT NULL,
    "Type" VARCHAR(50) NOT NULL, -- 'VIP', 'SLA_Breach', 'Complaint'
    "IsRead" BOOLEAN DEFAULT FALSE,
    "ProjectId" UUID NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL
);
```

---

## 3. Redis Data Structures

### Agent Presence
Tracks real-time agent connectivity and load for conversation routing.
- **Key**: `project:{projectId}:agent:{agentId}:presence`
- **Value (Hash)**:
  - `IsOnline`: "true" / "false"
  - `LastActive`: UTC timestamp
  - `ActiveConversationsCount`: int
- **Expiration**: 60 seconds
