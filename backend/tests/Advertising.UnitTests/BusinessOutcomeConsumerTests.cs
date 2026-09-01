using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Services;
using Modules.Advertising.Workers;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class BusinessOutcomeConsumerTests
{
    [Fact]
    public async Task Only_explicit_qualified_sales_classification_creates_a_qualified_lead()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant,
            new ServiceCollection().BuildServiceProvider());
        var consumer = new BusinessOutcomeConsumer(db, new ConversionLedgerService(db));
        var conversationId = Guid.NewGuid(); var customerId = Guid.NewGuid();

        await consumer.HandleAsync(Event(projectId, conversationId, customerId, "Support"));
        await consumer.HandleAsync(Event(projectId, conversationId, customerId, "Qualified"));

        var conversions = await db.AdvertisingConversions.IgnoreQueryFilters().ToListAsync();
        Assert.Single(conversions);
        Assert.Equal("QualifiedLead", conversions[0].EventType);
        Assert.Equal(2, await db.IntegrationInboxReceipts.IgnoreQueryFilters().CountAsync());
    }

    [Theory]
    [InlineData("Qualified", .79)]
    [InlineData("Support", .99)]
    public async Task Weak_or_non_sales_classification_does_not_create_a_qualified_lead(string classification, double confidence)
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant,
            new ServiceCollection().BuildServiceProvider());
        var consumer = new BusinessOutcomeConsumer(db, new ConversionLedgerService(db));
        var conversationId = Guid.NewGuid(); var customerId = Guid.NewGuid();

        await consumer.HandleAsync(Event(projectId, conversationId, customerId, classification, (decimal)confidence));

        Assert.Empty(await db.AdvertisingConversions.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.IntegrationInboxReceipts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task High_confidence_booking_intent_creates_a_qualified_lead()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant,
            new ServiceCollection().BuildServiceProvider());
        var consumer = new BusinessOutcomeConsumer(db, new ConversionLedgerService(db));

        await consumer.HandleAsync(Event(projectId, Guid.NewGuid(), Guid.NewGuid(), "BookingIntent", .86m));

        Assert.Equal("QualifiedLead", (await db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync()).EventType);
    }

    private static AdvertisingQualifiedMessageChanged Event(Guid projectId, Guid conversationId,
        Guid customerId, string classification, decimal confidence = 1m) => new()
    {
        Id = Guid.NewGuid(), ProjectId = projectId, ConversationId = conversationId, CustomerId = customerId,
        Classification = classification, Confidence = confidence, ClassifierVersion = "test-v1",
        ClassifiedAtUtc = DateTime.UtcNow, SourceAggregateType = "Conversation",
        SourceAggregateId = conversationId, SourceVersion = DateTime.UtcNow.Ticks
    };
}
