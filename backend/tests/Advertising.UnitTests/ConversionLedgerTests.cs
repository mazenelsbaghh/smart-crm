using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure;
using Shared.Security;

namespace Advertising.UnitTests;

public sealed class ConversionLedgerTests
{
    [Fact]
    public void Signature_is_stable_and_tampering_is_rejected()
    {
        const string secret = "test-only-secret"; const long timestamp = 1786957200; const string body = "{\"externalEventId\":\"pay-1\"}";
        var signature = "v1=" + ConversionSecurity.Sign(secret, timestamp, body);
        Assert.True(ConversionSecurity.Verify(secret, timestamp, body, signature));
        Assert.False(ConversionSecurity.Verify(secret, timestamp, body + "x", signature));
    }

    [Theory]
    [InlineData(ConsentState.Granted, "a@example.com", null, true)]
    [InlineData(ConsentState.Denied, "a@example.com", null, false)]
    [InlineData(ConsentState.Unknown, null, "+20100", false)]
    public void Match_data_requires_explicit_consent(ConsentState consent, string? email, string? phone, bool expected) =>
        Assert.Equal(expected, ConversionSecurity.CanUseMatchData(consent, email, phone));

    [Theory]
    [InlineData("Refund")]
    [InlineData("Absent")]
    [InlineData("Churn")]
    public void Negative_business_outcomes_are_corrections(string eventType) => Assert.True(ConversionSecurity.IsCorrection(eventType));

    [Fact]
    public async Task Refund_before_purchase_is_held_pending_then_recomputed_without_negative_revenue()
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var ledger = new ConversionLedgerService(db);
        var refundId = await ledger.RecordAsync(new(Guid.NewGuid(), projectId, "Orders", "order-1", "Refund",
            DateTime.UtcNow, "customer-1", 40m, "EGP", true, "Refunded"));
        var pending = await db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync(item => item.Id == refundId);
        Assert.Equal(CorrectionState.PendingBase, pending.CorrectionState);

        var purchaseId = await ledger.RecordAsync(new(Guid.NewGuid(), projectId, "Orders", "order-1", "Purchase",
            DateTime.UtcNow.AddMinutes(-5), "customer-1", 100m, "EGP"));
        var corrected = await db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync(item => item.Id == purchaseId);

        Assert.Equal(refundId, purchaseId);
        Assert.Equal(60m, corrected.CurrentValue);
        Assert.Equal(CorrectionState.Corrected, corrected.CorrectionState);
        Assert.Equal(2, await db.AdvertisingConversionSourceEvents.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Duplicate_source_event_is_idempotent_and_weaker_late_evidence_cannot_downgrade_truth()
    {
        var projectId = Guid.NewGuid(); var eventId = Guid.NewGuid();
        await using var db = Context(projectId);
        var ledger = new ConversionLedgerService(db);
        var input = new InternalConversionInput(eventId, projectId, "CRM", "deal-1", "DealWon", DateTime.UtcNow,
            "customer-1", 500m, "EGP");
        var first = await ledger.RecordAsync(input);
        var duplicate = await ledger.RecordAsync(input);
        await ledger.RecordAsync(new(Guid.NewGuid(), projectId, "CRM", "deal-1", "Lead", DateTime.UtcNow,
            "customer-1", null, null));

        Assert.Equal(first, duplicate);
        Assert.Equal("DealWon", (await db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync()).EventType);
        Assert.Equal(2, await db.AdvertisingConversionSourceEvents.IgnoreQueryFilters().CountAsync());
    }

    private static AppDbContext Context(Guid projectId)
    {
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        return new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant, new ServiceCollection().BuildServiceProvider());
    }
}
