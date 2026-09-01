using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Modules.Advertising.Workers;
using Modules.Conversations.Services;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingAttributionTests
{
    [Fact]
    public async Task First_message_is_always_counted_and_later_referral_is_preserved_once()
    {
        var setup = Setup();
        var publisher = new WhatsAppInboundEventPublisher(setup.Db, setup.Protector);
        var conversationId = Guid.NewGuid(); var customerId = Guid.NewGuid(); var destinationId = Guid.NewGuid();
        publisher.PublishObservation(setup.ProjectId, conversationId, customerId, destinationId, 2, "first",
            DateTime.UtcNow.AddMinutes(-2), new("Missing", null, null, null, "CloudApi"), true);
        publisher.PublishObservation(setup.ProjectId, conversationId, customerId, destinationId, 2, "second",
            DateTime.UtcNow.AddMinutes(-1), new("Missing", null, null, null, "CloudApi"), false);
        publisher.PublishObservation(setup.ProjectId, conversationId, customerId, destinationId, 2, "third",
            DateTime.UtcNow, new("CtwaClid", "click-3", "ad-3", null, "CloudApi"), false);
        await setup.Db.SaveChangesAsync();
        var outbox = await setup.Db.IntegrationOutboxMessages.ToListAsync();
        var events = outbox.Select(item =>
            System.Text.Json.JsonSerializer.Deserialize<WhatsAppAttributionObserved>(item.PayloadJson)!).ToArray();
        Assert.Equal(2, events.Length);
        Assert.True(events[0].IsFirstConversationMessage);
        Assert.False(events[1].IsFirstConversationMessage);

        var consumer = new WhatsAppAttributionObservationConsumer(setup.Db);
        foreach (var item in events) await consumer.HandleAsync(item);
        await consumer.HandleAsync(events[1]);

        var context = await setup.Db.AdvertisingAttributionContexts.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(2, context.ObservationCount);
        Assert.Equal(1, context.ValidReferralCount);
        Assert.Single(await setup.Db.AdvertisingAttributionTouches.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task First_baileys_conversation_is_one_unqualified_gateway_lead_without_invented_attribution()
    {
        var setup = Setup();
        var consumer = new GatewayLeadObservationConsumer(setup.Db, new ConversionLedgerService(setup.Db));
        var conversationId = Guid.NewGuid();
        var observed = new WhatsAppAttributionObserved
        {
            Id = Guid.NewGuid(), ProjectId = setup.ProjectId, ConversationId = conversationId,
            CustomerId = Guid.NewGuid(), DestinationId = Guid.NewGuid(), DestinationVersion = 1,
            MessageExternalId = "gateway-first", MessageOccurredAtUtc = DateTime.UtcNow,
            IdentifierState = "Missing", GatewayType = "BaileysExperimental",
            IsFirstConversationMessage = true, SourceAggregateType = "WhatsAppMessage",
            SourceAggregateId = Guid.NewGuid(), SourceVersion = 1
        };

        await consumer.HandleAsync(observed);
        await consumer.HandleAsync(observed);

        var lead = await setup.Db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Lead", lead.EventType);
        Assert.Equal("InternalBusinessOutcome", lead.AttributionMethod);
        Assert.Empty(await setup.Db.AdvertisingAttributionTouches.IgnoreQueryFilters().ToListAsync());
    }

    private static SetupState Setup()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant, new ServiceCollection().BuildServiceProvider());
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"wa-attribution-{Guid.NewGuid():N}"));
        IAdvertisingReferralProtector protector = new AdvertisingReferralProtector(DataProtectionProvider.Create(directory));
        return new(projectId, db, protector);
    }

    private sealed record SetupState(Guid ProjectId, AppDbContext Db, IAdvertisingReferralProtector Protector);
}
