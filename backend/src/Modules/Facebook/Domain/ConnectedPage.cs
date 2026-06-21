using Shared.Domain;
using System;

namespace Modules.Facebook.Domain
{
    public class ConnectedPage : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string FacebookPageId { get; set; } = string.Empty;
        public string PageName { get; set; } = string.Empty;
        public string PageAccessToken { get; set; } = string.Empty;
        public string? UserAccessToken { get; set; }
        public string? FacebookUserId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? TokenExpiresAt { get; set; }
    }
}
