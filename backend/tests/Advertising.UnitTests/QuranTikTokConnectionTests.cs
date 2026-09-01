using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.QuranChallenge.Domain;
using Modules.QuranChallenge.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class QuranTikTokConnectionTests
{
    [Fact]
    public async Task Legacy_display_name_does_not_claim_a_connection_until_creator_info_is_verified()
    {
        var projectId = Guid.NewGuid();
        await using var db = CreateDb(projectId);
        var legacy = new QuranTikTokSettings
        {
            ProjectId = projectId,
            OpenId = "account-1",
            DisplayName = "@legacy-placeholder"
        };
        db.QuranTikTokSettings.Add(legacy);
        await db.SaveChangesAsync();
        var service = CreateService(db, new Dictionary<string, string?>
        {
            ["ZERNIO_API_KEY"] = "sk_test_only",
            ["ZERNIO_PROFILE_ID"] = "profile-1",
            ["ZERNIO_TIKTOK_ACCOUNT_ID"] = "account-1"
        });

        Assert.False(service.IsVerified(legacy));
        var verified = await service.MarkVerifiedAsync(projectId,
            new TikTokCreatorInfo(null, "verified-user", null, ["SELF_ONLY"], false, false, false, 60),
            default);

        Assert.True(service.IsVerified(verified));
        Assert.Equal("verified-user", verified.DisplayName);
    }

    [Fact]
    public async Task Missing_account_configuration_never_creates_a_fake_identity()
    {
        var projectId = Guid.NewGuid();
        await using var db = CreateDb(projectId);
        var service = CreateService(db, new Dictionary<string, string?>
        {
            ["ZERNIO_API_KEY"] = "sk_test_only"
        });

        var settings = await service.EnsureSettingsAsync(projectId, default);

        Assert.False(service.IsConfigured);
        Assert.Null(settings.OpenId);
        Assert.Null(settings.DisplayName);
        Assert.Throws<InvalidOperationException>(() => _ = service.AccountId);
    }

    private static TikTokConnectionService CreateService(
        AppDbContext db,
        Dictionary<string, string?> values)
    {
        var api = new TikTokApiClient(
            new NoOpHttpClientFactory(),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return new TikTokConnectionService(
            db,
            api,
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    private static AppDbContext CreateDb(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
