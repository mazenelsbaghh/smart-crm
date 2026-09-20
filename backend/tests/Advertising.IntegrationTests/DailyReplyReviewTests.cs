using Microsoft.EntityFrameworkCore;
using Modules.Analytics.Application;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DailyReplyReviewTests(PostgresFixture postgres)
{
    private static readonly DateOnly Day = new(2026, 9, 7);
    private static readonly DateTime Start = new(2026, 9, 6, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Cairo_day_includes_old_active_conversations_and_excludes_adjacent_days_and_other_projects()
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var project = Guid.NewGuid();
        db.ProjectSettings.Add(new() { ProjectId = project, Timezone = "Africa/Cairo" });
        var conversation = SeedConversation(db, project);
        var foreign = SeedConversation(db, Guid.NewGuid());
        db.AddRange(Message(conversation, Start.AddSeconds(-1)), Message(conversation, Start),
            Message(conversation, Start.AddMinutes(10), "Outgoing"), Message(conversation, Start.AddDays(1)), Message(foreign, Start));
        await db.SaveChangesAsync();

        var report = await new DailyReplyReviewService(db).GetAsync(new(project, Day), default);

        Assert.Equal(Start, report.WindowStartUtc);
        Assert.Equal(Start.AddDays(1), report.WindowEndUtc);
        var row = Assert.Single(report.Conversations);
        Assert.Equal(conversation.Id, row.ConversationId);
        Assert.Equal(1, row.IncomingCount);
        Assert.Equal(1, row.OutgoingCount);
        Assert.Equal(10, row.LongestResponseMinutes);
        Assert.Equal("Missing", row.AnalysisStatus);
        Assert.Equal(1, report.Summary.AwaitingAnalysis);
    }

    [Fact]
    public async Task Delivery_history_keeps_actual_message_and_original_schedule_after_due_date_changes()
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var project = Guid.NewGuid();
        db.ProjectSettings.Add(new() { ProjectId = project, Timezone = "Africa/Cairo" });
        var conversation = SeedConversation(db, project);
        var sent = Message(conversation, Start.AddHours(3), "Outgoing");
        sent.Content = "الميعاد المناسب ليك متاح يوم الثلاثاء";
        var followUp = new FollowUp { ProjectId = project, CustomerId = conversation.CustomerId, ConversationId = conversation.Id,
            DueDate = Start.AddDays(2), SentForDueAtUtc = sent.Timestamp.AddMinutes(-7), SentAtUtc = sent.Timestamp,
            SentMessageId = sent.Id, Status = "Completed", Notes = "ملاحظات قديمة" };
        db.AddRange(sent, Message(conversation, sent.Timestamp.AddMinutes(2)), followUp,
            new FollowUp { ProjectId = project, CustomerId = conversation.CustomerId, DueDate = Start.AddHours(2), Status = "Completed", Notes = "أنهى الموظف المهمة" });
        await db.SaveChangesAsync();

        var report = await new DailyReplyReviewService(db).GetAsync(new(project, Day, View: "all"), default);

        var delivery = Assert.Single(report.FollowUps, f => f.Health == "Sent");
        Assert.Equal(sent.Content, delivery.Content);
        Assert.Equal(7, delivery.DelayMinutes);
        Assert.Equal(sent.Timestamp.AddMinutes(-7), delivery.DueAtUtc);
        Assert.True(delivery.CustomerReplied);
        Assert.Equal(1, report.Summary.FollowUpsSent);
        Assert.Equal(1, report.Summary.FollowUpsUnknown);
        Assert.Contains(report.FollowUps, f => f.Health == "Unverified" && f.SentAtUtc == null);
    }

    [Fact]
    public async Task Pagination_keeps_global_counts_and_does_not_use_later_analysis_to_judge_a_past_day()
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var project = Guid.NewGuid();
        db.ProjectSettings.Add(new() { ProjectId = project, Timezone = "Africa/Cairo" });
        for (var index = 0; index < 31; index++)
        {
            var conversation = SeedConversation(db, project);
            db.AddRange(Message(conversation, Start.AddHours(1)), Message(conversation, Start.AddHours(1).AddMinutes(1), "Outgoing"));
            if (index != 0) continue;
            db.ConversationSalesAnalyses.Add(new() { ProjectId = project, ConversationId = conversation.Id, CustomerId = conversation.CustomerId,
                AnalyzedThroughMessageAtUtc = Start.AddDays(2), ReplyQualityScore = 10, AnalysisVersion = ConversationSalesAnalyzer.CurrentAnalysisVersion });
        }
        await db.SaveChangesAsync();
        var service = new DailyReplyReviewService(db);
        var first = await service.GetAsync(new(project, Day, View: "all"), default);
        var second = await service.GetAsync(new(project, Day, 2, "all"), default);
        var attention = await service.GetAsync(new(project, Day), default);
        var unanalyzed = await service.GetAsync(new(project, Day, View: "unanalyzed"), default);
        Assert.Equal(31, first.FilteredCount);
        Assert.Equal(30, first.Conversations.Count);
        Assert.Single(second.Conversations);
        Assert.Equal(31, first.Conversations.Concat(second.Conversations).Select(c => c.ConversationId).Distinct().Count());
        Assert.Equal(31, first.Summary.AwaitingAnalysis);
        Assert.Null(Assert.Single(first.Conversations.Concat(second.Conversations), c => c.AnalysisStatus == "Later").ReplyQualityScore);
        Assert.Empty(attention.Conversations);
        Assert.Equal(31, unanalyzed.FilteredCount);
    }

    internal static Conversation SeedConversation(AppDbContext db, Guid projectId)
    {
        var customer = new Customer { ProjectId = projectId, Name = "عميل المراجعة", City = "Cairo", PhoneNumber = $"01{Random.Shared.NextInt64(100000000, 999999999)}" };
        var conversation = new Conversation { ProjectId = projectId, CustomerId = customer.Id, Channel = "Messenger", Status = "Open",
            CreatedAt = Start.AddDays(-20), LastMessageTimestamp = Start.AddHours(2) };
        db.AddRange(customer, conversation);
        return conversation;
    }

    internal static Message Message(Conversation conversation, DateTime at, string direction = "Incoming") => new()
    { ConversationId = conversation.Id, ExternalMessageId = Guid.NewGuid().ToString(), Direction = direction,
        SenderType = direction == "Incoming" ? "Customer" : "AI", MessageType = "Text", Content = "رسالة المراجعة", Timestamp = at };
}
