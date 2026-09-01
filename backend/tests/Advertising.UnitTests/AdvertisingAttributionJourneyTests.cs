using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingAttributionJourneyTests
{
    [Fact]
    public void Ctwa_identifier_uses_a_dedicated_protection_purpose_and_controlled_unwrap()
    {
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"referral-{Guid.NewGuid():N}"));
        var protector = new AdvertisingReferralProtector(DataProtectionProvider.Create(directory));
        var protectedIdentifier = protector.ProtectIdentifier("ctwa-secret-1");

        Assert.Equal("ctwa-secret-1", protector.UnprotectForBusinessMessaging(protectedIdentifier));
        Assert.Throws<CryptographicException>(() => protector.UnprotectInboundJson(protectedIdentifier));
        Assert.DoesNotContain("ctwa-secret-1", protectedIdentifier, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Last_eligible_touch_wins_and_earlier_touch_is_preserved()
    {
        var projectId = Guid.NewGuid(); var customer = Guid.NewGuid(); var now = DateTime.UtcNow;
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant, new ServiceCollection().BuildServiceProvider());
        var first = new AdvertisingAttributionTouch { ProjectId = projectId, JourneyKey = customer.ToString("N"),
            ProtectedCtwaClid = "protected-1", TouchedAtUtc = now.AddDays(-3), Method = "CtwaClid" };
        var last = new AdvertisingAttributionTouch { ProjectId = projectId, JourneyKey = customer.ToString("N"),
            ProtectedCtwaClid = "protected-2", TouchedAtUtc = now.AddDays(-1), Method = "CtwaClid" };
        var conversion = new CanonicalConversion { ProjectId = projectId, CanonicalKey = "crm:paid-1", EventType = "Purchase",
            CustomerReference = customer.ToString("N"), OccurredAtUtc = now, State = ConversionState.Verified };
        db.AddRange(first, last, conversion); await db.SaveChangesAsync();

        var result = await new AdvertisingAttributionService(db).ResolveAsync(projectId, conversion.Id, 7);

        Assert.Equal(last.Id, result.TouchId);
        Assert.Null(first.ConversionId);
        Assert.Equal(conversion.Id, last.ConversionId);
        Assert.Equal(AttributionState.Attributed, conversion.AttributionState);
    }

    [Fact]
    public async Task Expired_touch_never_gets_guessed_into_the_conversion()
    {
        var projectId = Guid.NewGuid(); var customer = Guid.NewGuid(); var now = DateTime.UtcNow;
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant, new ServiceCollection().BuildServiceProvider());
        var touch = new AdvertisingAttributionTouch { ProjectId = projectId, JourneyKey = customer.ToString("N"),
            ProtectedCtwaClid = "protected", TouchedAtUtc = now.AddDays(-10), Method = "CtwaClid" };
        var conversion = new CanonicalConversion { ProjectId = projectId, CanonicalKey = "crm:paid-2", EventType = "Purchase",
            CustomerReference = customer.ToString("N"), OccurredAtUtc = now, State = ConversionState.Verified };
        db.AddRange(touch, conversion); await db.SaveChangesAsync();

        var result = await new AdvertisingAttributionService(db).ResolveAsync(projectId, conversion.Id, 7);

        Assert.Equal(AttributionState.Unattributed, result.State);
        Assert.Null(touch.ConversionId);
    }
}
