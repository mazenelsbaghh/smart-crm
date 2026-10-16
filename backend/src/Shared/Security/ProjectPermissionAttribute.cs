namespace Shared.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ProjectPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
