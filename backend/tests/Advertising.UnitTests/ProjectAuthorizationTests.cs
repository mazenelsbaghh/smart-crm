using System.Security.Claims;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ProjectAuthorizationTests
{
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly ProjectAuthorizationService _authorization = new();

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Admin", true)]
    [InlineData("Agent", false)]
    [InlineData("Supervisor", false)]
    public void Only_owner_and_admin_can_manage_advertising(string role, bool expected)
    {
        Assert.Equal(expected, _authorization.CanManageAdvertising(User(role, _projectId), _projectId));
    }

    [Fact]
    public void Project_claim_cannot_authorize_another_project()
    {
        Assert.False(_authorization.CanRead(User("Owner", _projectId), Guid.NewGuid()));
        Assert.False(_authorization.CanManageAdvertising(User("Owner", _projectId), Guid.NewGuid()));
    }

    private static ClaimsPrincipal User(string role, Guid projectId) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, role),
        new Claim("ProjectId", projectId.ToString())
    ], "test"));
}
