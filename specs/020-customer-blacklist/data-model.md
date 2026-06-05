# Data Model Modifications: Customer Blacklist for AI Exclusion

This document details the database schema and model updates.

## 1. `Customer` Model Property Addition

File: [Customer.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/Conversations/Domain/Customer.cs)

Add `IsBlacklisted` boolean property:
```csharp
public bool IsBlacklisted { get; set; } = false;
```

## 2. EF Core Migration Specification

A new migration `AddIsBlacklistedToCustomer` will be generated. The generated migration file should include a step to add the boolean column to the `Customers` table.

### Migration Schema Definition

```csharp
migrationBuilder.AddColumn<bool>(
    name: "IsBlacklisted",
    table: "Customers",
    type: "boolean",
    nullable: false,
    defaultValue: false);
```
