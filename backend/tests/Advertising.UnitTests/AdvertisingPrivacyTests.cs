using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Jobs;
using Modules.Advertising.Services;
using Shared.Audit;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingPrivacyTests
{
    private const string RawReferral = "raw-click-identifier";
    private const string RawPhone = "+201000000000";
    private const string RawEmail = "buyer@example.com";
    private const string RawToken = "meta-secret-token";

    [Fact]
    public void Logs_and_provider_errors_redact_secrets_referrals_and_customer_pii()
    {
        var raw = $"ctwa_clid={RawReferral} phone={RawPhone} email={RawEmail} access_token={RawToken}";

        AssertRedacted(AdvertisingLogSanitizer.Redact(raw));
        AssertRedacted(AdvertisingErrorEnvelope.ProviderFailure("CreateAd", raw, "trace-safe").Message);
    }

    [Fact]
    public async Task Audit_export_truth_and_elasticsearch_document_are_both_redacted()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant,
            new ServiceCollection().BuildServiceProvider());
        var service = new AdvertisingAuditService(db);
        var raw = $"{{\"ctwa_clid\":\"{RawReferral}\",\"phone\":\"{RawPhone}\",\"email\":\"{RawEmail}\",\"access_token\":\"{RawToken}\"}}";

        var record = service.Append(new(projectId, "Conversion", "Delivered", "Customer", RawEmail,
            "System", null, raw, Guid.NewGuid()));
        await db.SaveChangesAsync();

        AssertRedacted(JsonSerializer.Serialize(record));
        AssertRedacted(JsonSerializer.Serialize(AdvertisingAuditDocument.From(record)));
    }

    [Fact]
    public void Retention_windows_are_bounded_and_keep_aggregated_evidence_longer_than_raw_identifiers()
    {
        Assert.InRange(AdvertisingRetentionPolicy.AiWorkDays, 1, AdvertisingRetentionPolicy.ProtectedAttributionDays);
        Assert.Equal(AdvertisingRetentionPolicy.ProtectedAttributionDays, AdvertisingRetentionPolicy.DeliveryAttemptDays);
        Assert.True(AdvertisingRetentionPolicy.InsightsYears * 365 > AdvertisingRetentionPolicy.ProtectedAttributionDays);
    }

    private static void AssertRedacted(string value)
    {
        Assert.DoesNotContain(RawReferral, value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawPhone, value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawEmail, value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawToken, value, StringComparison.OrdinalIgnoreCase);
    }
}
