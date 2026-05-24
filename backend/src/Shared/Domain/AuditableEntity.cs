using System;

namespace Shared.Domain
{
    public abstract class AuditableEntity : Entity
    {
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
