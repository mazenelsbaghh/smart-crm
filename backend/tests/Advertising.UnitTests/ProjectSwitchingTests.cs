using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.API;
using Modules.Auth.Domain;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ProjectSwitchingTests : IAsyncDisposable
{
    private readonly AppDbContext db;
    private readonly BCryptPasswordHasher hasher = new();
    private readonly JwtService jwt;
    private readonly User account;
    private readonly Project secondProject = new() { Name = "Second" };
    private readonly RefreshToken currentToken;
    private readonly ProjectSessionsController controller;

    public ProjectSwitchingTests()
    {
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new TenantContext(), new ServiceCollection().BuildServiceProvider());
        jwt = new JwtService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["JWT:Secret"] = "project-switch-tests-use-an-explicit-64-character-secret-1234567890" }).Build());
        var firstProject = new Project { Name = "First" };
        account = new User { Email = "current@example.test", Role = "Owner", ProjectId = firstProject.Id, PasswordHash = "unused" };
        currentToken = new RefreshToken { UserId = account.Id, ProjectId = firstProject.Id, Token = "original", ExpiresAt = DateTime.UtcNow.AddDays(1) };
        db.AddRange(firstProject, secondProject, account, currentToken);
        db.SaveChanges();
        controller = new ProjectSessionsController(db, hasher, jwt)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                    new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                    new Claim("ProjectId", account.ProjectId.ToString()), new Claim(ClaimTypes.Role, account.Role)
                }, "test"))
            } }
        };
    }

    [Fact]
    public async Task Switching_to_an_unlinked_project_is_forbidden_and_keeps_the_session()
    {
        Assert.IsType<ForbidResult>(await controller.Switch(new(secondProject.Id, currentToken.Token)));
        Assert.Null(currentToken.RevokedAt);
        var projects = Body(Assert.IsType<OkObjectResult>(await controller.List()));
        Assert.Single(projects.EnumerateArray());
        Assert.Equal(account.ProjectId, projects[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Switching_and_refreshing_keep_the_selected_project_and_its_own_role()
    {
        db.ProjectMemberships.Add(new ProjectMembership { UserId = account.Id, ProjectId = secondProject.Id, Role = "Agent" });
        await db.SaveChangesAsync();
        var response = Body(Assert.IsType<OkObjectResult>(await controller.Switch(new(secondProject.Id, currentToken.Token))));
        Assert.NotNull(currentToken.RevokedAt);
        Assert.Equal(account.Id, response.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal("Agent", response.GetProperty("user").GetProperty("role").GetString());
        var auth = new AuthController(db, hasher, jwt);
        var refreshed = Body(Assert.IsType<OkObjectResult>(await auth.Refresh(new RefreshRequest
        { RefreshToken = response.GetProperty("refreshToken").GetString()! })));
        var principal = jwt.GetPrincipalFromExpiredToken(refreshed.GetProperty("accessToken").GetString()!);
        Assert.Equal(secondProject.Id.ToString(), principal.FindFirstValue("ProjectId"));
        Assert.Equal("Agent", principal.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public async Task Removing_membership_prevents_refreshing_the_project_session()
    {
        currentToken.ProjectId = secondProject.Id;
        await db.SaveChangesAsync();
        var response = Assert.IsType<UnauthorizedObjectResult>(await new AuthController(db, hasher, jwt)
            .Refresh(new RefreshRequest { RefreshToken = currentToken.Token }));
        Assert.Equal("REFRESH_TOKEN_INVALID", Body(response).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Owner", "correct", true)]
    [InlineData("Owner", "wrong", false)]
    [InlineData("Agent", "correct", false)]
    public async Task Linking_requires_the_other_projects_owner_credentials(string role, string password, bool allowed)
    {
        db.Users.Add(new User { Email = "other@example.test", Role = role, ProjectId = secondProject.Id, PasswordHash = hasher.HashPassword("correct") });
        await db.SaveChangesAsync();
        var response = await controller.Link(new LoginRequest { Email = "other@example.test", Password = password });
        if (!allowed)
        {
            Assert.IsType<BadRequestObjectResult>(response);
            Assert.Empty(db.ProjectMemberships);
            return;
        }
        Assert.IsType<OkObjectResult>(response);
        Assert.IsType<OkObjectResult>(await controller.Link(new LoginRequest { Email = "other@example.test", Password = password }));
        var membership = Assert.Single(db.ProjectMemberships);
        Assert.Equal(account.Id, membership.UserId);
        Assert.Equal(secondProject.Id, membership.ProjectId);
        Assert.Equal("Owner", membership.Role);
        Assert.Equal(2, Body(Assert.IsType<OkObjectResult>(await controller.List())).GetArrayLength());
    }

    [Fact]
    public async Task Creating_a_project_grants_ownership_and_opens_an_independent_workspace()
    {
        var response = Body(Assert.IsType<OkObjectResult>(await controller.Create(new("  مشروع جديد  "))));
        var projectId = response.GetProperty("projectId").GetGuid();
        Assert.Equal("مشروع جديد", response.GetProperty("name").GetString());
        Assert.NotEqual(account.ProjectId, projectId);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync(settings => settings.ProjectId == projectId);
        Assert.Equal("Africa/Cairo", settings.Timezone);
        Assert.False(settings.AiAutoReplyEnabled);
        Assert.Empty(settings.GeminiApiKey);
        var switched = Body(Assert.IsType<OkObjectResult>(await controller.Switch(new(projectId, currentToken.Token))));
        var principal = jwt.GetPrincipalFromExpiredToken(switched.GetProperty("accessToken").GetString()!);
        Assert.Equal(projectId.ToString(), principal.FindFirstValue("ProjectId"));
        Assert.Equal("Owner", principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal(2, Body(Assert.IsType<OkObjectResult>(await controller.List())).GetArrayLength());
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("Analyst")]
    public async Task Creating_a_project_rechecks_the_accounts_role(string role)
    {
        account.Role = role;
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await controller.Create(new("Unauthorized")));
        Assert.Empty(db.ProjectMemberships);
        Assert.Equal(2, await db.Projects.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Creating_a_project_rejects_missing_names_without_creating_records(string? name)
    {
        Assert.IsType<BadRequestObjectResult>(await controller.Create(new(name!)));
        Assert.Empty(db.ProjectMemberships);
        Assert.Equal(2, await db.Projects.CountAsync());
    }

    private static JsonElement Body(ObjectResult response) =>
        JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
