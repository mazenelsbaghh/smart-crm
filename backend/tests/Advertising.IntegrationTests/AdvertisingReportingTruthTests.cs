using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.API;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AdvertisingReportingTruthTests(PostgresFixture postgres)
{
    private static readonly DateTime Day = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Overview_revenue_uses_verified_paid_values_in_one_currency_and_separates_ad_attribution()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var paid = Outcome(projectId, "Purchase", Guid.NewGuid());
        paid.CurrentValue = 100m; paid.Currency = "EGP";
        paid.AttributionState = AttributionState.Attributed; paid.AdvertisementId = Guid.NewGuid();
        var organic = Outcome(projectId, "DealWon", Guid.NewGuid());
        organic.CurrentValue = 40m; organic.Currency = "EGP";
        var foreign = Outcome(projectId, "SubscriptionRenewed", Guid.NewGuid());
        foreign.CurrentValue = 200m; foreign.Currency = "USD";
        var leadValue = Outcome(projectId, "QualifiedLead", Guid.NewGuid()); leadValue.CurrentValue = 999m; leadValue.Currency = "EGP";
        var unverified = Outcome(projectId, "Purchase", Guid.NewGuid()); unverified.CurrentValue = 500m; unverified.Currency = "EGP"; unverified.TruthState = "Observed";
        var corrected = Outcome(projectId, "EnrollmentPaid", Guid.NewGuid());
        corrected.CurrentValue = 25m; corrected.Currency = "EGP"; corrected.TruthState = "Corrected"; corrected.CorrectionState = CorrectionState.Corrected;
        var attendance = Outcome(projectId, "AttendanceConfirmed", Guid.NewGuid()); attendance.CurrentValue = 999m; attendance.Currency = "EGP";
        var suppressed = Outcome(projectId, "Purchase", Guid.NewGuid()); suppressed.CurrentValue = 999m; suppressed.Currency = "EGP"; suppressed.State = ConversionState.Suppressed;
        db.AddRange(paid, organic, foreign, leadValue, unverified, corrected, attendance, suppressed);
        await db.SaveChangesAsync();

        var metrics = await AdvertisingOverviewQuery.ConversionsAsync(db, projectId, new(Day, Day.AddDays(1)), "EGP", default);

        Assert.Equal(165m, metrics.Revenue);
        Assert.Equal(100m, metrics.AttributedRevenue);
        Assert.Equal(1, metrics.ExcludedRevenueCurrencies);
        Assert.Equal(4, metrics.Purchases);
        Assert.Equal(1, metrics.QualifiedLeads);
    }

    [Fact]
    public async Task Daily_report_does_not_turn_missing_ad_insights_into_zero_cost()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var zeroSpend = new ManagedAdvertisement { ProjectId = projectId, Name = "Reported zero", AdExternalId = "zero" };
        var missingSpend = new ManagedAdvertisement { ProjectId = projectId, Name = "Not loaded", AdExternalId = "missing" };
        var zeroCustomer = Guid.NewGuid(); var missingCustomer = Guid.NewGuid();
        db.AddRange(new AdvertisingConnection { ProjectId = projectId, AccountTimezoneIana = "Africa/Cairo", AccountCurrency = "EGP" },
            zeroSpend, missingSpend, Observation(projectId, zeroCustomer, "zero"), Observation(projectId, missingCustomer, "missing"),
            Outcome(projectId, "QualifiedLead", zeroCustomer), Outcome(projectId, "QualifiedLead", missingCustomer),
            Insight(projectId, zeroSpend.Id, Day, 0));
        await db.SaveChangesAsync();
        var controller = new AdvertisingConversionsController(new ProjectAuthorizationService(), db, null!, null!, null!, null!)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("ProjectId", projectId.ToString())], "test")) } } };

        var response = Assert.IsType<OkObjectResult>(await controller.DailyReport(projectId, new DateOnly(2026, 10, 16), default));
        var report = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var rows = report.GetProperty("rows").EnumerateArray().ToArray();
        var zero = rows.Single(row => row.GetProperty("id").GetGuid() == zeroSpend.Id);
        var missing = rows.Single(row => row.GetProperty("id").GetGuid() == missingSpend.Id);
        Assert.False(report.GetProperty("spendAvailable").GetBoolean());
        Assert.True(zero.GetProperty("spendAvailable").GetBoolean());
        Assert.Equal(0m, zero.GetProperty("costPerQualified").GetDecimal());
        Assert.False(missing.GetProperty("spendAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("costPerQualified").ValueKind);
    }

    [Fact]
    public async Task Daily_report_keeps_missing_referrals_unattributed_deduplicates_people_and_uses_account_local_days()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var advertisement = new ManagedAdvertisement { ProjectId = projectId, Name = "Published ad", AdExternalId = "ad-42" };
        var anonymousCustomer = Guid.NewGuid(); var linkedCustomer = Guid.NewGuid(); var orphanCustomer = Guid.NewGuid();
        db.AddRange(new AdvertisingConnection { ProjectId = projectId, AccountTimezoneIana = "Africa/Cairo", AccountCurrency = "EGP" },
            new AutonomyEnvelope { ProjectId = projectId, ReportingTimezoneIana = "America/New_York", Currency = "USD" },
            advertisement, new ManagedAdvertisement { ProjectId = projectId, Name = "Unpublished draft" });
        db.AdvertisingAttributionObservations.AddRange(Observation(projectId, anonymousCustomer, null), Observation(projectId, linkedCustomer, "ad-42"));
        var unverifiedBooking = Outcome(projectId, "BookingConfirmed", anonymousCustomer); unverifiedBooking.TruthState = "Observed";
        db.AdvertisingConversions.AddRange(Outcome(projectId, "QualifiedLead", anonymousCustomer), Outcome(projectId, "QualifiedLead", anonymousCustomer),
            Outcome(projectId, "BookingConfirmed", linkedCustomer), unverifiedBooking,
            Outcome(projectId, "QualifiedLead", orphanCustomer), Outcome(projectId, "BookingConfirmed", orphanCustomer), Outcome(projectId, "EnrollmentPaid", orphanCustomer));
        db.AdvertisingInsights.AddRange(Insight(projectId, advertisement.Id, Day.AddDays(-1), 99),
            Insight(projectId, advertisement.Id, Day, 10), Insight(projectId, advertisement.Id, Day.AddDays(1), 88));
        await db.SaveChangesAsync();
        var controller = new AdvertisingConversionsController(new ProjectAuthorizationService(), db, null!, null!, null!, null!)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("ProjectId", projectId.ToString())], "test")) } } };

        var response = Assert.IsType<OkObjectResult>(await controller.DailyReport(projectId, new DateOnly(2026, 10, 16), default));
        var report = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Africa/Cairo", report.GetProperty("timezone").GetString());
        Assert.Equal("EGP", report.GetProperty("currency").GetString());
        Assert.Equal(10m, report.GetProperty("totals").GetProperty("spend").GetDecimal());
        Assert.Equal(2, report.GetProperty("totals").GetProperty("qualified").GetInt32());
        Assert.Equal(1, report.GetProperty("totals").GetProperty("bookings").GetInt32());
        var row = Assert.Single(report.GetProperty("rows").EnumerateArray());
        Assert.Equal(advertisement.Id, row.GetProperty("id").GetGuid());
        Assert.Equal(1, row.GetProperty("entrants").GetInt32());
        Assert.Equal(1, report.GetProperty("unattributed").GetProperty("entrants").GetInt32());
        Assert.Equal(1, report.GetProperty("unattributed").GetProperty("qualified").GetInt32());
        Assert.Equal(0, report.GetProperty("unattributed").GetProperty("bookings").GetInt32());
        Assert.Equal(1, report.GetProperty("unlinkedOutcomes").GetProperty("qualified").GetInt32());
        Assert.Equal(1, report.GetProperty("unlinkedOutcomes").GetProperty("bookings").GetInt32());
    }

    private static CanonicalConversion Outcome(Guid project, string type, Guid customer) => new() {
        ProjectId = project, CanonicalKey = Guid.NewGuid().ToString(), EventType = type, CustomerReference = customer.ToString("N"),
        OccurredAtUtc = Day.AddHours(10), TruthState = "Verified", State = ConversionState.Verified };
    private static WhatsAppAttributionObservation Observation(Guid project, Guid customer, string? ad) => new() {
        ProjectId = project, CustomerId = customer, ConversationId = Guid.NewGuid(), MessageExternalId = Guid.NewGuid().ToString(),
        MessageOccurredAtUtc = Day.AddHours(1), ProviderAdExternalId = ad };
    private static InsightsSnapshot Insight(Guid project, Guid target, DateTime day, decimal spend) => new() {
        ProjectId = project, TargetId = target, IntervalStartUtc = day, IntervalEndUtc = day.AddDays(1),
        Spend = spend, Currency = "EGP", AccountTimezone = "Africa/Cairo", FetchedAtUtc = day.AddDays(1) };
}
