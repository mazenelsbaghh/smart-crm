using Shared.Domain;

namespace Modules.Auth.Domain;

public sealed class ProjectMembership : Entity
{
    public Guid UserId { get; set; }
    public Guid ProjectId { get; set; }
    public string Role { get; set; } = "Agent";
}
