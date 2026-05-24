using Shared.Domain;
using System;

namespace Modules.Auth.Domain
{
    public class User : AuditableEntity, ITenantEntity
    {
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; }
        public Guid ProjectId { get; set; }
    }
}
