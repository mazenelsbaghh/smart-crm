using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.API;
using Modules.Advertising.Services;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MetaOAuthAndSecretsTests
{
    [Fact]
    public void OAuth_state_is_short_lived_and_single_use()
    {
        var now = DateTime.UtcNow;
        Assert.Equal("Valid", MetaOAuthStatePolicy.Evaluate(now.AddMinutes(-2), consumed: false, now).ToString());
        Assert.Equal("Consumed", MetaOAuthStatePolicy.Evaluate(now.AddMinutes(-2), consumed: true, now).ToString());
        Assert.Equal("Expired", MetaOAuthStatePolicy.Evaluate(now.AddMinutes(-11), consumed: false, now).ToString());
    }

    [Fact]
    public void Callback_is_global_and_anonymous()
    {
        var route = Assert.Single(typeof(FacebookAdsOAuthCallbackController).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>());
        Assert.Equal("api/ad-manager/meta/oauth/callback", route.Template);
        Assert.NotNull(typeof(FacebookAdsOAuthCallbackController).GetCustomAttributes(typeof(AllowAnonymousAttribute), false).SingleOrDefault());
    }

    [Fact]
    public void Provider_secret_is_versioned_and_never_stored_in_plaintext()
    {
        var vault = new AdvertisingSecretVault(new EphemeralDataProtectionProvider());
        var protectedValue = vault.Protect("provider-secret");

        Assert.StartsWith("v1:", protectedValue);
        Assert.DoesNotContain("provider-secret", protectedValue);
        Assert.Equal("provider-secret", vault.Unprotect(protectedValue));
    }

    [Fact]
    public void Project_secret_is_encrypted_and_bound_to_its_project()
    {
        var vault = new ProjectSecretVault(new EphemeralDataProtectionProvider());
        var projectId = Guid.NewGuid();
        var protectedValue = vault.Protect(projectId, "gemini-secret");

        Assert.True(vault.IsProtected(protectedValue));
        Assert.DoesNotContain("gemini-secret", protectedValue);
        Assert.Equal("gemini-secret", vault.Unprotect(projectId, protectedValue));
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => vault.Unprotect(Guid.NewGuid(), protectedValue));
    }

    [Fact]
    public async Task Legacy_project_key_is_protected_once_during_startup_migration()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
        db.ProjectSettings.Add(new ProjectSettings { ProjectId = projectId, GeminiApiKey = "legacy-secret" });
        await db.SaveChangesAsync();
        var vault = new ProjectSecretVault(new EphemeralDataProtectionProvider());

        var migrated = await ProjectSecretMigration.ProtectLegacyGeminiKeysAsync(db, vault);
        var repeated = await ProjectSecretMigration.ProtectLegacyGeminiKeysAsync(db, vault);
        var stored = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();

        Assert.Equal(1, migrated);
        Assert.Equal(0, repeated);
        Assert.True(vault.IsProtected(stored.GeminiApiKey));
        Assert.Equal("legacy-secret", vault.Unprotect(projectId, stored.GeminiApiKey));
    }
}
