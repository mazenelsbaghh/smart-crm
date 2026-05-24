using System;

namespace Shared.Domain
{
    public interface ITenantEntity
    {
        Guid ProjectId { get; set; }
    }
}
