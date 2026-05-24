using System;

namespace Shared.Security
{
    public interface ITenantContext
    {
        Guid ProjectId { get; }
        void SetProjectId(Guid projectId);
    }
}
