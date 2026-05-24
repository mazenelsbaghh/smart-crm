using System;

namespace Shared.Security
{
    public class TenantContext : ITenantContext
    {
        public Guid ProjectId { get; private set; } = Guid.Empty;

        public void SetProjectId(Guid projectId)
        {
            ProjectId = projectId;
        }
    }
}
