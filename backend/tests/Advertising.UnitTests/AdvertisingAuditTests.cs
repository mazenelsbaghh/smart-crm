using Modules.Advertising.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingAuditTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(9, 300)]
    public void Audit_index_retry_is_bounded(int attempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), AdvertisingAuditIndexPolicy.RetryDelay(attempt));
    }

    [Fact]
    public void Audit_index_failure_dead_letters_without_removing_postgres_truth()
    {
        Assert.False(AdvertisingAuditIndexPolicy.ShouldDeadLetter(9));
        Assert.True(AdvertisingAuditIndexPolicy.ShouldDeadLetter(10));
    }

    [Fact]
    public void Audit_redaction_removes_referral_and_customer_identifiers()
    {
        var sanitized = AdvertisingLogSanitizer.Redact(
            "ctwa_clid=raw-click-id phone=201000000000 access_token=token");

        Assert.DoesNotContain("raw-click-id", sanitized);
        Assert.DoesNotContain("201000000000", sanitized);
        Assert.DoesNotContain("token", sanitized.Replace("access_token", string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Planning_wait_and_ai_schema_outcome_preserve_versions_reasons_and_outbox_evidence()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant,
            new ServiceCollection().BuildServiceProvider());
        var audit = new AdvertisingAuditService(db);
        var offerId = Guid.NewGuid();

        await audit.RecordPlanningDecisionAsync(projectId, offerId, "CampaignPlanWait",
            ["ADS_PROFILE_STALE_OR_BLOCKED"], new { sourceVersions = new[] { new { documentId = Guid.NewGuid(), version = 7L } } });
        audit.RecordAiSchemaResult(projectId, Guid.NewGuid(), "Strategist", "input-hash",
            "Rejected", "gemini-test", "ADS_AI_RESULT_SCHEMA_INVALID");
        await db.SaveChangesAsync();

        var records = await db.AdvertisingAuditRecords.IgnoreQueryFilters().OrderBy(item => item.OccurredAtUtc).ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.Contains("ADS_PROFILE_STALE_OR_BLOCKED", records[0].SafeEvidenceJson);
        Assert.Contains("version\":7", records[0].SafeEvidenceJson);
        Assert.Contains("ADS_AI_RESULT_SCHEMA_INVALID", records[1].SafeEvidenceJson);
        Assert.Equal(2, await db.IntegrationOutboxMessages.CountAsync());
    }
}
