using System.Security.Claims;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Domain;
using Modules.Projects.Domain;
using Modules.Projects.Services;
using StackExchange.Redis;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ProjectPushSubscriptionsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private IContainer? container;
    private IConnectionMultiplexer redis = null!;

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ADVERTISING_TEST_REDIS_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            container = new ContainerBuilder("redis:7-alpine").WithPortBinding(6379, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
            await container.StartAsync();
            connection = $"{container.Hostname}:{container.GetMappedPublicPort(6379)}";
        }
        redis = await ConnectionMultiplexer.ConnectAsync(connection);
    }

    public async Task DisposeAsync()
    {
        redis.Dispose();
        if (container is not null) await container.DisposeAsync();
    }

    [Fact]
    public async Task Shared_device_moves_to_current_project_and_revoked_sessions_cannot_receive_push()
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = isolated.CreateContext();
        await db.Database.MigrateAsync();
        var firstProject = new Project { Name = "First" };
        var secondProject = new Project { Name = "Second" };
        var user = new User { ProjectId = firstProject.Id, Email = "push@example.test", PasswordHash = "unused", Role = "Agent" };
        var firstSession = Session(user, firstProject.Id);
        var secondSession = Session(user, secondProject.Id);
        db.AddRange(firstProject, secondProject, user, firstSession, secondSession,
            new ProjectMembership { UserId = user.Id, ProjectId = secondProject.Id, Role = "Agent" });
        await db.SaveChangesAsync();
        var tokens = new ProjectPushSubscriptions(db, redis.GetDatabase());
        var device = Guid.NewGuid().ToString();
        var firstPrincipal = Principal(user, firstSession);
        var secondPrincipal = Principal(user, secondSession);
        await tokens.RegisterAsync(firstPrincipal, device);
        Assert.Contains(device, await tokens.ActiveTokensAsync(firstProject.Id));

        await tokens.RegisterAsync(secondPrincipal, device);
        Assert.Empty(await tokens.ActiveTokensAsync(firstProject.Id));
        Assert.False(await tokens.RemoveAsync(firstPrincipal, device));
        Assert.Contains(device, await tokens.ActiveTokensAsync(secondProject.Id));

        var nextSession = Session(user, secondProject.Id);
        db.Add(nextSession);
        await db.SaveChangesAsync();
        await tokens.RegisterAsync(Principal(user, nextSession), device);
        Assert.False(await tokens.RemoveAsync(secondPrincipal, device));
        nextSession.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await redis.GetDatabase().SetAddAsync($"fcm_tokens:{secondProject.Id}", "legacy-unowned-token");
        Assert.Empty(await tokens.ActiveTokensAsync(secondProject.Id));
        Assert.Empty(await redis.GetDatabase().SetMembersAsync($"fcm_tokens:{secondProject.Id}"));
    }

    private static RefreshToken Session(User user, Guid projectId) => new()
    {
        UserId = user.Id, ProjectId = projectId, Token = Guid.NewGuid().ToString(), ExpiresAt = DateTime.UtcNow.AddDays(1)
    };

    private static ClaimsPrincipal Principal(User user, RefreshToken session) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, "Agent"),
        new Claim("ProjectId", session.ProjectId.ToString()!), new Claim("sid", session.Id.ToString())
    ], "test"));
}
