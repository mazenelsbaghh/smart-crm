using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingTenantIsolationTests
{
    [Fact]
    public async Task Advertising_query_filters_never_return_another_project()
    {
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var context = new AppDbContext(options, tenant, services);
        context.AdvertisingConnections.AddRange(
            new AdvertisingConnection { ProjectId = projectId },
            new AdvertisingConnection { ProjectId = otherProjectId });
        await context.SaveChangesAsync();

        var visible = await context.AdvertisingConnections.AsNoTracking().ToListAsync();

        var connection = Assert.Single(visible);
        Assert.Equal(projectId, connection.ProjectId);
        Assert.Equal(2, await context.AdvertisingConnections.IgnoreQueryFilters().CountAsync());
    }
}
