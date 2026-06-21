# Data Model: UX/UI Unified Inbox Redesign & GSAP Animations

## 1. Entity Extensions (Customer)

The `Customer` class in `Modules.Conversations.Domain` is extended with three new properties:

```csharp
namespace Modules.Conversations.Domain
{
    public class Customer : AuditableEntity, ITenantEntity
    {
        // ... existing properties ...

        /// <summary>
        /// Probability of customer purchase/deal completion, from 0 to 100.
        /// </summary>
        public int PurchaseProbability { get; set; } = 0;

        /// <summary>
        /// JSON-serialized object representing dynamic AI insights for this customer.
        /// </summary>
        public string? AIInsights { get; set; }

        /// <summary>
        /// JSON-serialized object representing rules or configuration for automations active for this customer.
        /// </summary>
        public string? AutomationRules { get; set; }
    }
}
```

---

## 2. Validation & Business Rules

1. **PurchaseProbability**:
   - Must be an integer value between `0` and `100` (inclusive).
   - Validated inside the DTO request and clamped/validated inside `CRMController.cs`.

2. **AIInsights & AutomationRules**:
   - Stored as optional nullable string/text columns mapping to JSON in PostgreSQL.
   - Initialized to empty or null if no values are calculated.
