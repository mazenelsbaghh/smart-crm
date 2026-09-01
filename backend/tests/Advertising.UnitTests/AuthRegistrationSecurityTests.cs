using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.API;
using Modules.Auth.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AuthRegistrationSecurityTests
{
    [Fact]
    public async Task Public_registration_rejects_owner_project_injection_without_creating_a_user()
    {
        var projectId = Guid.NewGuid();
        var (controller, db) = CreateController(projectId);
        await using var ownedDb = db;
        var request = JsonSerializer.Deserialize<RegisterRequest>($$"""
        {
          "email": "attacker@example.test",
          "password": "irrelevant-password",
          "role": "Owner",
          "projectId": "{{projectId}}"
        }
        """, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var response = Assert.IsType<ObjectResult>(controller.Register(request));

        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        var errorBody = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal("REGISTRATION_DISABLED", errorBody.GetProperty("code").GetString());
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync());
    }

    [Fact]
    public async Task Owner_login_returns_the_stored_safe_identity_with_tokens()
    {
        var projectId = Guid.NewGuid();
        var (controller, db) = CreateController(projectId);
        await using var ownedDb = db;
        var passwordHasher = new BCryptPasswordHasher();
        var owner = new User
        {
            Email = "owner@example.test",
            PasswordHash = passwordHasher.HashPassword("correct-password"),
            Role = "Owner",
            ProjectId = projectId
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var response = Assert.IsType<OkObjectResult>(await controller.Login(new LoginRequest
        {
            Email = owner.Email,
            Password = "correct-password"
        }));
        var responseBody = JsonSerializer.SerializeToElement(
            response.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var returnedUser = responseBody.GetProperty("user");

        Assert.False(string.IsNullOrWhiteSpace(responseBody.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(responseBody.GetProperty("refreshToken").GetString()));
        Assert.Equal(owner.Id, returnedUser.GetProperty("id").GetGuid());
        Assert.Equal(owner.Email, returnedUser.GetProperty("email").GetString());
        Assert.Equal("Owner", returnedUser.GetProperty("role").GetString());
        Assert.False(returnedUser.TryGetProperty("fullName", out _));
        Assert.False(returnedUser.TryGetProperty("projectId", out _));
        Assert.False(returnedUser.TryGetProperty("passwordHash", out _));
    }

    private static (AuthController Controller, AppDbContext Db) CreateController(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
        var jwtConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWT:Secret"] = "auth-contract-tests-use-an-explicit-64-character-secret-value-123456"
        }).Build();
        return (new AuthController(db, new BCryptPasswordHasher(), new JwtService(jwtConfiguration)), db);
    }
}
