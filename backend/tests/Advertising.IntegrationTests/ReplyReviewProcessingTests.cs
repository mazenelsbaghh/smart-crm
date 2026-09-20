using System.Security.Claims;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Modules.Analytics.Jobs;
using Modules.Brain.Services;
using Modules.Conversations.Domain;
using Modules.CRM.API;
using Modules.CRM.Domain;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ReplyReviewProcessingTests(PostgresFixture postgres)
{
    private readonly ProjectSecretVault _vault = new(new EphemeralDataProtectionProvider());

    [Fact]
    public async Task Scheduler_creates_durable_work_once_and_includes_old_chats_with_due_followups()
    {
        var seeded = await SeedAsync();
        await using var db = postgres.CreateContext();
        var conversation = await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == seeded.ConversationId);
        conversation.LastMessageTimestamp = DateTime.UtcNow.AddDays(-5);
        db.FollowUps.Add(new() { ProjectId = seeded.ProjectId, CustomerId = conversation.CustomerId, ConversationId = conversation.Id,
            DueDate = DateTime.UtcNow.AddMinutes(-20), Status = "Pending", Notes = "متابعة لم تُرسل" });
        await db.SaveChangesAsync();
        var queue = new ReplyReviewQueue(db);
        var schedule = await queue.ScheduleAsync(seeded.ProjectId, default);
        var jobs = new RecordingJobs();
        var scheduler = new ReplyReviewScheduler(db, queue, jobs);

        await scheduler.TickAsync(default);
        schedule.NextScanAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        await scheduler.TickAsync(default);

        var review = Assert.Single(await db.ReplyReviewCases.IgnoreQueryFilters().Where(r => r.ProjectId == seeded.ProjectId).ToListAsync());
        Assert.Equal(seeded.MessageId, review.SourceMessageId);
        Assert.NotNull(review.SourceFollowUpAtUtc);
        Assert.NotNull(schedule.LastScanAtUtc);
        Assert.True(schedule.NextScanAtUtc > DateTime.UtcNow);
        Assert.Contains(jobs.Created, job => job.Args.Contains(review.Id));
    }

    [Fact]
    public async Task Draft_is_saved_once_and_only_a_new_evidenced_reply_can_resolve_the_case()
    {
        var seeded = await SeedAsync();
        var caseId = await EnqueueAsync(seeded);
        var gemini = new ReviewGemini(() => Analysis(seeded.MessageId, true));
        await RunAsync(caseId, gemini);
        await RunAsync(caseId, gemini);
        await using var verification = postgres.CreateContext();
        var draft = await verification.ReplyReviewCases.IgnoreQueryFilters().SingleAsync(r => r.Id == caseId);
        Assert.Equal(ReplyReviewState.DraftReady, draft.State);
        Assert.Equal("الموعد يحتاج مراجعة من الموظف.", draft.DraftContent);
        Assert.Equal(seeded.MessageId, draft.DraftBasedOnMessageId);
        Assert.Single(await verification.ReplyReviewRuns.IgnoreQueryFilters().Where(r => r.CaseId == caseId).ToListAsync());
        Assert.Equal(1, await verification.Messages.CountAsync(m => m.ConversationId == seeded.ConversationId));
        Assert.False(await verification.GroupAppointmentBookings.IgnoreQueryFilters().AnyAsync(b => b.ProjectId == seeded.ProjectId));

        await DueNowAsync(caseId);
        var scheduledJobs = new RecordingJobs();
        await new ReplyReviewScheduler(verification, new ReplyReviewQueue(verification), scheduledJobs).TickAsync(default);
        Assert.DoesNotContain(scheduledJobs.Created, job => job.Args.Contains(caseId));
        await RunAsync(caseId, new ReviewGemini(() => throw new InvalidOperationException("No new reply: do not call AI to invent resolution.")));
        await verification.Entry(draft).ReloadAsync();
        Assert.Equal(ReplyReviewState.DraftReady, draft.State);
        Assert.True(draft.NeedsResolution);
        Assert.Single(await verification.ReplyReviewRuns.IgnoreQueryFilters().Where(r => r.CaseId == caseId).ToListAsync());

        var reply = new Message { ConversationId = seeded.ConversationId, Content = "موعد السيشن الاثنين الساعة ٨ مساءً.",
            Direction = "Outgoing", SenderType = "Agent", ExternalMessageId = Guid.NewGuid().ToString(), MessageType = "Text", Timestamp = DateTime.UtcNow };
        verification.Messages.Add(reply);
        await verification.SaveChangesAsync();
        await EnqueueAsync(seeded);
        await RunAsync(caseId, new ReviewGemini(() => Analysis(reply.Id, false, reply.Content)));
        await verification.Entry(draft).ReloadAsync();
        Assert.Equal(ReplyReviewState.Resolved, draft.State);
        Assert.False(draft.NeedsResolution);
        Assert.Equal(2, await verification.Messages.CountAsync(m => m.ConversationId == seeded.ConversationId));
    }

    [Fact]
    public async Task Simultaneous_workers_do_not_duplicate_review_and_new_messages_invalidate_the_draft()
    {
        var seeded = await SeedAsync();
        var caseId = await EnqueueAsync(seeded);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueReview = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gemini = new ReviewGemini(() => Analysis(seeded.MessageId, true), async () => { started.TrySetResult(); await continueReview.Task; });
        var first = RunAsync(caseId, gemini);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await RunAsync(caseId, new ReviewGemini(() => throw new InvalidOperationException("Duplicate worker must not reach AI.")));
        await using var incoming = postgres.CreateContext();
        var newMessage = new Message { ConversationId = seeded.ConversationId, ExternalMessageId = Guid.NewGuid().ToString(), Direction = "Incoming", MessageType = "Text", Content = "رديت خلاص", Timestamp = DateTime.UtcNow };
        incoming.Messages.Add(newMessage);
        await incoming.SaveChangesAsync();
        continueReview.SetResult();
        await first;

        var review = await incoming.ReplyReviewCases.IgnoreQueryFilters().SingleAsync(r => r.Id == caseId);
        Assert.Equal(ReplyReviewState.Queued, review.State);
        Assert.Equal(newMessage.Id, review.SourceMessageId);
        Assert.Empty(review.DraftContent);
        var run = Assert.Single(await incoming.ReplyReviewRuns.IgnoreQueryFilters().Where(r => r.CaseId == caseId).ToListAsync());
        Assert.Equal(seeded.MessageId, run.SourceMessageId);
        Assert.Equal("Queued", run.Outcome);
    }

    [Fact]
    public async Task Provider_failures_are_bounded_and_pausing_prevents_new_attempts()
    {
        var seeded = await SeedAsync();
        var caseId = await EnqueueAsync(seeded);
        await using var db = postgres.CreateContext();
        await db.ReplyReviewSchedules.IgnoreQueryFilters().Where(s => s.ProjectId == seeded.ProjectId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Enabled, false));
        await RunAsync(caseId, new ReviewGemini(() => throw new InvalidOperationException("Paused work must not reach AI.")));
        Assert.Empty(await db.ReplyReviewRuns.IgnoreQueryFilters().Where(r => r.CaseId == caseId).ToListAsync());
        await db.ReplyReviewSchedules.IgnoreQueryFilters().Where(s => s.ProjectId == seeded.ProjectId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Enabled, true));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await DueNowAsync(caseId);
            await RunAsync(caseId, new ReviewGemini(() => "[AI_ERROR] unavailable"));
        }
        var review = await db.ReplyReviewCases.IgnoreQueryFilters().SingleAsync(r => r.Id == caseId);
        Assert.Equal(ReplyReviewState.Failed, review.State);
        Assert.Equal(3, review.Attempts);
        Assert.Equal(3, await db.ReplyReviewRuns.IgnoreQueryFilters().CountAsync(r => r.CaseId == caseId));
        Assert.Empty(review.DraftContent);
    }

    [Fact]
    public async Task Production_cost_guard_stops_review_dispatch_after_one_hundred_runs_per_project_per_day()
    {
        var seeded = await SeedAsync();
        var caseId = await EnqueueAsync(seeded);
        await using var db = postgres.CreateContext();
        db.ReplyReviewRuns.AddRange(Enumerable.Range(0, 100).Select(attempt => new ReplyReviewRun
        {
            ProjectId = seeded.ProjectId,
            CaseId = caseId,
            SourceMessageId = seeded.MessageId,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            FinishedAtUtc = DateTime.UtcNow.AddHours(-1),
            Outcome = ReplyReviewState.Reviewed.ToString(),
            Attempt = attempt + 1
        }));
        await db.SaveChangesAsync();
        var jobs = new RecordingJobs();

        await new ReplyReviewScheduler(db, new ReplyReviewQueue(db), jobs).TickAsync(default);

        Assert.DoesNotContain(jobs.Created, job => job.Args.Contains(caseId));
    }

    [Fact]
    public async Task Followup_delivery_problems_prevent_a_false_resolved_result_and_changes_are_requeued()
    {
        var seeded = await SeedAsync();
        await using var db = postgres.CreateContext();
        var original = await db.Messages.SingleAsync(m => m.Id == seeded.MessageId);
        original.Direction = "Outgoing";
        original.Content = "موعد السيشن الاثنين الساعة ٨ مساءً.";
        var customerId = await db.Conversations.IgnoreQueryFilters().Where(c => c.Id == seeded.ConversationId).Select(c => c.CustomerId).SingleAsync();
        var followUp = new FollowUp { ProjectId = seeded.ProjectId, ConversationId = seeded.ConversationId, CustomerId = customerId,
            DueDate = DateTime.UtcNow.AddMinutes(-10), Status = "DeliveryUnknown", Notes = "متابعة" };
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync();
        var caseId = await EnqueueAsync(seeded);
        var gemini = new ReviewGemini(() => Analysis(original.Id, false, original.Content));
        await RunAsync(caseId, gemini);
        var review = await db.ReplyReviewCases.IgnoreQueryFilters().SingleAsync(r => r.Id == caseId);
        Assert.Equal(ReplyReviewState.NeedsHuman, review.State);

        followUp.Status = "Cancelled";
        await db.SaveChangesAsync();
        await EnqueueAsync(seeded);
        await RunAsync(caseId, gemini);
        await db.Entry(review).ReloadAsync();
        Assert.Equal(ReplyReviewState.Resolved, review.State);
    }

    [Fact]
    public async Task Processing_endpoints_require_the_project_and_manager_role()
    {
        var seeded = await SeedAsync();
        await using var db = postgres.CreateContext();
        var controller = new ReplyReviewProcessingController(db, new ReplyReviewQueue(db), new ProjectAuthorizationService())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User(seeded.ProjectId, "Agent") } } };
        Assert.IsType<ForbidResult>(await controller.Enqueue(seeded.ProjectId, seeded.ConversationId, "review", default));
        Assert.IsType<ForbidResult>(await controller.Get(Guid.NewGuid()));
        controller.HttpContext.User = User(seeded.ProjectId, "Owner");
        Assert.IsType<NotFoundObjectResult>(await controller.Enqueue(seeded.ProjectId, Guid.NewGuid(), "review", default));
        Assert.IsType<AcceptedResult>(await controller.Enqueue(seeded.ProjectId, seeded.ConversationId, "review", default));
        Assert.IsType<OkObjectResult>(await controller.Get(seeded.ProjectId));
        Assert.IsType<OkObjectResult>(await controller.Detail(seeded.ProjectId, seeded.ConversationId, default));
    }

    private async Task<Seeded> SeedAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var projectId = Guid.NewGuid();
        var conversation = DailyReplyReviewTests.SeedConversation(db, projectId);
        conversation.LastMessageTimestamp = DateTime.UtcNow.AddMinutes(-20);
        var message = DailyReplyReviewTests.Message(conversation, conversation.LastMessageTimestamp);
        message.Content = "السيشن الساعة كام؟";
        db.AddRange(message, new ProjectSettings { ProjectId = projectId, GeminiApiKey = "test-key", CustomerReplyProvider = "Gemini" });
        await db.SaveChangesAsync();
        return new(projectId, conversation.Id, message.Id);
    }

    private async Task<Guid> EnqueueAsync(Seeded seeded)
    {
        await using var db = postgres.CreateContext();
        return (await new ReplyReviewQueue(db).EnqueueAsync(seeded.ProjectId, seeded.ConversationId, "Automatic", default)).Id;
    }

    private async Task DueNowAsync(Guid caseId)
    {
        await using var db = postgres.CreateContext();
        await db.ReplyReviewCases.IgnoreQueryFilters().Where(r => r.Id == caseId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.NextRunAtUtc, DateTime.UtcNow.AddMinutes(-1)));
    }

    private async Task RunAsync(Guid caseId, IGeminiClient gemini)
    {
        await using var db = postgres.CreateContext();
        var analyzer = new ConversationSalesAnalyzer(db, gemini, _vault);
        var drafts = new CorrectiveReplyDraftService(db, new AIMarketingBrain(gemini, null!, null!, new AIBehaviorSettingsService()),
            _vault, new AIBehaviorSettingsService(), new AICompanyBrain(db, gemini));
        await new ReplyReviewWorker(db, new ReplyReviewQueue(db), analyzer, drafts, NullLogger<ReplyReviewWorker>.Instance).ExecuteAsync(caseId, default);
    }

    private static string Analysis(Guid evidenceId, bool unresolved, string quote = "السيشن الساعة كام؟") => JsonSerializer.Serialize(new
    {
        stage = "Engaged", outcome = "Active", primaryReason = "Unknown", secondaryReasons = Array.Empty<string>(),
        summary = unresolved ? "العميل لم يحصل على موعد السيشن." : "الرد الأخير أجاب عن سؤال الموعد.", recommendation = "راجع موعد السيشن.",
        evidence = new[] { new { messageId = evidenceId, quote } }, lastCustomerIntent = "سؤال عن موعد", confidence = 0.9,
        replyQualityScore = unresolved ? 25 : 90, hasUnresolvedReplyIssue = unresolved, followUpPriority = 50,
        needsFollowUp = unresolved, missedOpportunity = unresolved, replyContent = "الموعد يحتاج مراجعة من الموظف.",
        suggestedGroupBookingId = Guid.NewGuid(), cancelGroupBooking = true
    });

    private static ClaimsPrincipal User(Guid projectId, string role) => new(new ClaimsIdentity(
        [new Claim("ProjectId", projectId.ToString()), new Claim(ClaimTypes.Role, role)], "test"));
    private sealed record Seeded(Guid ProjectId, Guid ConversationId, Guid MessageId);
    private sealed class RecordingJobs : IBackgroundJobClient
    {
        public List<Job> Created { get; } = [];
        public string Create(Job job, IState state) { Created.Add(job); return Guid.NewGuid().ToString(); }
        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }
    private sealed class ReviewGemini(Func<string> response, Func<Task>? duringGeneration = null) : IGeminiClient
    {
        public async Task<string> GenerateReplyAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null)
        { if (duringGeneration != null) await duringGeneration(); return response(); }
        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string? apiKeyOverride = null) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string? apiKeyOverride = null) => throw new NotSupportedException();
    }
}
