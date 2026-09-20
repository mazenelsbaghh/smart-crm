using System.Security.Claims;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Conversations.API;
using Modules.AI.Workers;
using Modules.Conversations.Domain;
using Modules.Conversations.Jobs;
using Modules.Conversations.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Shared.Events;
using Shared.Queue;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ConversationReplyRegressionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_stored_conversation_resolves_after_sender_identity_changes_without_crossing_accounts()
    {
        var conversation = await SeedConversationAsync();
        var tenant = new TenantContext();
        tenant.SetProjectId(conversation.ProjectId);
        await using var db = postgres.CreateContext(tenant);
        var source = new MessageAggregatedEvent { ProjectId = conversation.ProjectId, ConversationId = conversation.Id,
            WhatsAppAccountId = conversation.WhatsAppAccountId, Sender = "old@lid", Content = "رقمي اتبعت" };

        Assert.Equal(conversation.Id, (await AIReplyWorker.ResolveConversationAsync(db, source, null))?.Id);
        source.WhatsAppAccountId = Guid.NewGuid();
        Assert.Null(await AIReplyWorker.ResolveConversationAsync(db, source, null));
        source.WhatsAppAccountId = conversation.WhatsAppAccountId;
        source.ProjectId = Guid.NewGuid();
        Assert.Null(await AIReplyWorker.ResolveConversationAsync(db, source, null));
    }

    [Fact]
    public async Task Production_2026_09_08_handoff_survives_retries_and_blocks_automation_until_staff_reopens()
    {
        var conversation = await SeedConversationAsync();
        var firstReply = Reply(conversation);
        var competingReply = Reply(conversation);
        await using var firstDb = postgres.CreateContext();
        await using var secondDb = postgres.CreateContext();
        var firstConversation = await firstDb.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == conversation.Id);
        var secondConversation = await secondDb.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == conversation.Id);

        await Task.WhenAll(
            new ConversationHumanHandoff(firstDb).RequestAsync(firstConversation, firstReply),
            new ConversationHumanHandoff(secondDb).RequestAsync(secondConversation, competingReply));

        await using var verification = postgres.CreateContext();
        var persisted = await verification.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal("Pending", persisted.Status);
        var outbox = Assert.Single(await verification.IntegrationOutboxMessages
            .Where(message => message.EventId == firstReply.Id || message.EventId == competingReply.Id).ToListAsync());
        var acknowledgement = Assert.IsType<AIReplyGeneratedEvent>(
            new IntegrationEventTypeRegistry().Deserialize(outbox.EventType, outbox.SchemaVersion, outbox.PayloadJson));
        Assert.Equal(persisted.HumanHandoffReplyId, acknowledgement.Id);
        Assert.Single(await verification.NotificationAlerts.IgnoreQueryFilters()
            .Where(alert => alert.ProjectId == conversation.ProjectId && alert.Type == "HumanTransferRequest").ToListAsync());
        var handoff = new ConversationHumanHandoff(verification);
        Assert.False(await handoff.BlocksReplyAsync(acknowledgement));
        Assert.True(await handoff.BlocksReplyAsync(Reply(conversation)));
        var otherAccountReply = Reply(conversation);
        otherAccountReply.WhatsAppAccountId = Guid.NewGuid();
        Assert.False(await handoff.BlocksReplyAsync(otherAccountReply));

        var controller = new ConversationController(verification, null!, null!, null!, new ConfigurationBuilder().Build(),
            DispatchProxy.Create<IConnectionMultiplexer, MessagingBookingIntegrityTests.UnusedRedis>(), null!, null!, new ProjectAuthorizationService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("ProjectId", conversation.ProjectId.ToString())], "test")) } }
        };
        Assert.IsType<OkObjectResult>(await controller.UpdateStatus(conversation.Id, new() { Status = "Open" }));
        verification.ChangeTracker.Clear();
        Assert.False(await handoff.BlocksReplyAsync(Reply(conversation)));
        Assert.True(await handoff.BlocksReplyAsync(acknowledgement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_2026_09_08_reactions_do_not_erase_customer_context_and_handoff_stops_dispatch(bool handoffPending)
    {
        var conversation = await SeedConversationAsync();
        await using var db = postgres.CreateContext();
        var start = DateTime.UtcNow.AddMinutes(-3);
        var question = new Message { ConversationId = conversation.Id, ExternalMessageId = Guid.NewGuid().ToString(),
            Direction = "Incoming", Content = "السيشن المجانية امتى؟", MessageType = "Text", Timestamp = start };
        var clarification = new Message { ConversationId = conversation.Id, ExternalMessageId = Guid.NewGuid().ToString(),
            Direction = "Incoming", Content = "آخر سؤال", MessageType = "Text", Timestamp = start.AddSeconds(2) };
        db.Messages.AddRange(question, clarification, new Message { ConversationId = conversation.Id,
            ExternalMessageId = Guid.NewGuid().ToString(), Direction = "Outgoing", Content = "[تفاعل] ❤️",
            MessageType = "Reaction", Timestamp = start.AddSeconds(1) });
        if (handoffPending)
            await db.Conversations.IgnoreQueryFilters().Where(c => c.Id == conversation.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(c => c.HumanHandoffReplyId, Guid.NewGuid()));
        await db.SaveChangesAsync();
        await new ConversationReplyWindowService(db).StageAsync(new ConversationReplyWindowRequest(
            conversation.ProjectId, conversation.Id, clarification.Id, "201123456789", clarification.Content,
            clarification.Timestamp, DateTime.UtcNow.AddSeconds(-1), "regression", "WhatsApp",
            WhatsAppAccountId: conversation.WhatsAppAccountId));

        await new ConversationReplyWindowDispatcher(db, NullLogger<ConversationReplyWindowDispatcher>.Instance).DispatchAsync();

        var window = await db.ConversationReplyWindows.IgnoreQueryFilters().SingleAsync(w => w.ConversationId == conversation.Id);
        Assert.Equal(window.EventId, window.DispatchedEventId);
        var pending = await db.IntegrationOutboxMessages.Where(message => message.EventId == window.EventId).ToListAsync();
        if (handoffPending) Assert.Empty(pending);
        else
        {
            var aggregated = JsonSerializer.Deserialize<MessageAggregatedEvent>(Assert.Single(pending).PayloadJson)!;
            Assert.Equal(question.Content + "\n" + clarification.Content, aggregated.Content);
        }
    }

    private async Task<Conversation> SeedConversationAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var project = new Project { Name = "Reply regression" };
        var customer = new Customer { ProjectId = project.Id, Name = "عميل", PhoneNumber = "201123456789", City = "" };
        var conversation = new Conversation { ProjectId = project.Id, CustomerId = customer.Id, WhatsAppAccountId = project.Id };
        db.AddRange(project, customer,
            new WhatsAppAccount { Id = project.Id, ProjectId = project.Id, Name = "WhatsApp", IsDefault = true }, conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private static AIReplyGeneratedEvent Reply(Conversation conversation) => new()
    {
        ProjectId = conversation.ProjectId, ConversationId = conversation.Id, WhatsAppAccountId = conversation.WhatsAppAccountId,
        Sender = "201123456789", Content = "تم تسجيل طلبك", Channel = "WhatsApp"
    };
}
