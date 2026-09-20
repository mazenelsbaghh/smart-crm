using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Modules.Analytics.Application;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Modules.Conversations.Domain;
using Modules.Projects.Domain;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class SalesIntelligencePersistenceTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(FollowUpPlanAction.SendNow, 1)]
    [InlineData(FollowUpPlanAction.Schedule, 2)]
    public async Task Limited_sales_batch_persists_priority_order_daily_distribution_and_custom_spacing(
        FollowUpPlanAction action, int days)
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var priorities = new[] { 81, 99, 92, 95, 83, 88 };
        foreach (var priority in priorities)
            SeedSalesOpportunity(db, projectId, action == FollowUpPlanAction.Schedule ? priority - 40 : priority);
        SeedSalesOpportunity(db, Guid.NewGuid(), 100);
        db.ProjectSettings.Add(new ProjectSettings { ProjectId = projectId, Timezone = "Africa/Cairo" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new SalesIntelligenceService(db, new CoordinatedGemini(), new PassthroughVault());
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(1);
        var dashboard = await service.GetDashboardAsync(projectId, from, to, CancellationToken.None);
        var command = new QueueFollowUpPlan(projectId, from, to, action, PlanToken:
            action == FollowUpPlanAction.SendNow ? dashboard.FollowUpPlan.SendNowToken : dashboard.FollowUpPlan.ScheduleToken)
        { DispatchOptions = new(5, 60, 60, days) };

        var invalid = await service.QueueFollowUpPlanAsync(command with { DispatchOptions = new(5, 60, 30, days) }, CancellationToken.None);
        Assert.NotNull(invalid.ValidationError);
        Assert.Empty(await db.FollowUps.Where(f => f.ProjectId == projectId).ToListAsync());
        var queued = await service.QueueFollowUpPlanAsync(command, CancellationToken.None);
        var retry = await service.QueueFollowUpPlanAsync(command, CancellationToken.None);
        db.ChangeTracker.Clear();
        var persisted = await db.FollowUps.Where(f => f.ProjectId == projectId).OrderBy(f => f.DueDate).ToListAsync();
        var analyses = await db.ConversationSalesAnalyses.Where(a => a.ProjectId == projectId).ToDictionaryAsync(a => a.ConversationId);

        Assert.Equal(5, queued.Queued);
        Assert.True(retry.PlanChanged);
        Assert.Equal(5, persisted.Count);
        var expected = new[] { 99, 95, 92, 88, 83 }.Select(p => action == FollowUpPlanAction.Schedule ? p - 40 : p);
        Assert.Equal(expected, persisted.Select(f => analyses[f.ConversationId!.Value].FollowUpPriority));
        Assert.Null(persisted[0].DependsOnFollowUpId);
        for (var index = 1; index < persisted.Count; index++)
        {
            Assert.Equal(persisted[index - 1].Id, persisted[index].DependsOnFollowUpId);
            Assert.Equal(60, persisted[index].DispatchIntervalSeconds);
            var expectedGap = action == FollowUpPlanAction.Schedule && index == 3
                ? TimeSpan.FromDays(1) - TimeSpan.FromSeconds(120) : TimeSpan.FromSeconds(60);
            Assert.Equal(expectedGap, persisted[index].DueDate - persisted[index - 1].DueDate);
        }
        Assert.Equal(persisted.Select(f => f.Id), queued.Dispatches.Select(d => d.Id));
    }

    private static void SeedSalesOpportunity(Shared.Infrastructure.AppDbContext db, Guid projectId, int priority)
    {
        var customer = new Customer { ProjectId = projectId, Name = "عميل", City = "القاهرة",
            PhoneNumber = $"01{Random.Shared.NextInt64(100000000, 999999999)}" };
        var conversation = new Conversation { ProjectId = projectId, CustomerId = customer.Id,
            Channel = "WhatsApp", Status = "Open", LastMessageTimestamp = DateTime.UtcNow.AddMinutes(-priority) };
        var analysis = Analysis(projectId, conversation.Id);
        analysis.CustomerId = customer.Id;
        analysis.FollowUpPriority = priority;
        analysis.NeedsFollowUp = true;
        db.AddRange(customer, conversation, analysis);
    }

    [Fact]
    public async Task Migration_persists_one_tenant_scoped_analysis_per_conversation()
    {
        var projectId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        await using var context = postgres.CreateContext(tenant);
        await context.Database.MigrateAsync();
        context.ConversationSalesAnalyses.Add(Analysis(projectId, conversationId));
        await context.SaveChangesAsync();

        context.ConversationSalesAnalyses.Add(Analysis(projectId, conversationId));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        var persisted = await context.ConversationSalesAnalyses.SingleAsync(
            analysis => analysis.ProjectId == projectId && analysis.ConversationId == conversationId);
        Assert.Equal(SalesLossReason.ScheduleMismatch, persisted.EffectivePrimaryReason);
        Assert.Equal(projectId, persisted.ProjectId);
    }

    [Fact]
    public async Task Concurrent_forced_reanalysis_returns_the_single_persisted_result()
    {
        var projectId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        await using (var seed = postgres.CreateContext(tenant))
        {
            await seed.Database.MigrateAsync();
            var startedAt = DateTime.UtcNow.AddHours(-1);
            seed.AddRange(
                new Customer { Id = customerId, ProjectId = projectId, Name = "عميل", City = "Cairo",
                    PhoneNumber = $"01{Random.Shared.NextInt64(100000000, 999999999)}" },
                new Conversation
                {
                    Id = conversationId,
                    ProjectId = projectId,
                    CustomerId = customerId,
                    Channel = "WhatsApp",
                    Status = "Open",
                    CreatedAt = startedAt,
                    LastMessageTimestamp = startedAt.AddMinutes(5)
                },
                new Message
                {
                    ConversationId = conversationId,
                    ExternalMessageId = Guid.NewGuid().ToString("N"),
                    Direction = "Incoming",
                    Content = "السعر غالي",
                    MessageType = "Text",
                    Timestamp = startedAt.AddMinutes(5)
                },
                new ProjectSettings { ProjectId = projectId, GeminiApiKey = "test-key", GeminiModel = "gemini-3.5-flash" });
            await seed.SaveChangesAsync();
        }

        await using var firstDb = postgres.CreateContext(tenant);
        await using var secondDb = postgres.CreateContext(tenant);
        var gemini = new CoordinatedGemini();
        var first = new ConversationSalesAnalyzer(firstDb, gemini, new PassthroughVault());
        var second = new ConversationSalesAnalyzer(secondDb, gemini, new PassthroughVault());

        var results = await Task.WhenAll(
            first.ReanalyzeAsync(projectId, conversationId, CancellationToken.None),
            second.ReanalyzeAsync(projectId, conversationId, CancellationToken.None));

        await using var verification = postgres.CreateContext(tenant);
        var persisted = await verification.ConversationSalesAnalyses
            .Where(analysis => analysis.ProjectId == projectId && analysis.ConversationId == conversationId)
            .ToListAsync();
        Assert.Single(persisted);
        Assert.All(results, result => Assert.Equal(persisted[0].Id, result.Id));
    }

    [Fact]
    public async Task Demand_sheet_includes_inquiries_with_modes_and_excludes_booked_spam_and_other_tenants()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        await using var context = postgres.CreateContext(tenant);
        await context.Database.MigrateAsync();
        var scenarios = new[]
        {
            (projectId, SalesConversationStage.Engaged, SalesLossReason.None, "Online"),
            (projectId, SalesConversationStage.Qualified, SalesLossReason.None, "Offline"),
            (projectId, SalesConversationStage.New, SalesLossReason.None, "Unknown"),
            (projectId, SalesConversationStage.Booked, SalesLossReason.None, "Online"),
            (projectId, SalesConversationStage.Engaged, SalesLossReason.SpamOrSupport, "Offline"),
            (Guid.NewGuid(), SalesConversationStage.Engaged, SalesLossReason.None, "Offline")
        };
        foreach (var (scope, stage, reason, mode) in scenarios)
        {
            var customer = new Customer { ProjectId = scope, Name = "طالب", City = "القاهرة", PhoneNumber = $"01{Random.Shared.NextInt64(100000000, 999999999)}" };
            var conversation = new Conversation { ProjectId = scope, CustomerId = customer.Id, Channel = "WhatsApp", Status = "Open" };
            var analysis = Analysis(scope, conversation.Id);
            analysis.CustomerId = customer.Id;
            analysis.VerifiedStage = stage;
            analysis.AiPrimaryReason = reason;
            analysis.RequestedAttendanceMode = mode;
            context.AddRange(customer, conversation, analysis);
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = new SalesIntelligenceService(context, new CoordinatedGemini(), new PassthroughVault());

        var sheet = await service.GetScheduleDemandAsync(projectId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), CancellationToken.None);

        Assert.Equal(2, sheet.TotalPeople);
        Assert.Equal(0, sheet.DistinctSchedules);
        Assert.Equal(new[] { "Offline", "Online" }, sheet.Rows.Select(row => row.AttendanceMode).OrderBy(mode => mode));
        Assert.All(sheet.Rows, row => Assert.Equal("InquiryOnly", row.RequestKind));
        Assert.Equal("بيسألوا بس", Assert.Single(sheet.Groups).Label);
    }

    private static ConversationSalesAnalysis Analysis(Guid projectId, Guid conversationId) => new()
    {
        ProjectId = projectId,
        ConversationId = conversationId,
        CustomerId = Guid.NewGuid(),
        ConversationStartedAtUtc = DateTime.UtcNow.AddHours(-1),
        LastMessageAtUtc = DateTime.UtcNow,
        AnalyzedThroughMessageAtUtc = DateTime.UtcNow,
        AnalyzedAtUtc = DateTime.UtcNow,
        AiStage = SalesConversationStage.BookingIntent,
        VerifiedStage = SalesConversationStage.BookingIntent,
        Outcome = SalesConversationOutcome.Dormant,
        AiPrimaryReason = SalesLossReason.ScheduleMismatch,
        Summary = "طلب العميل موعدًا بديلًا.",
        Recommendation = "اعرض موعدًا آخر.",
        Model = "gemini-3.5-flash"
    };

    private sealed class PassthroughVault : IProjectSecretVault
    {
        public bool IsProtected(string? storedValue) => false;
        public string Protect(Guid projectId, string secret) => secret;
        public string? Unprotect(Guid projectId, string? storedValue) => storedValue;
    }

    private sealed class CoordinatedGemini : IGeminiClient
    {
        private readonly TaskCompletionSource _bothRequestsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public async Task<string> GenerateReplyAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null)
        {
            if (Interlocked.Increment(ref _requestCount) == 2) _bothRequestsStarted.TrySetResult();
            await _bothRequestsStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return """
                {"stage":"BookingIntent","outcome":"Lost","primaryReason":"PriceObjection","secondaryReasons":[],
                "summary":"العميل اعترض على السعر.","recommendation":"وضّح القيمة.","evidence":[],
                "lastCustomerIntent":"معرفة السعر","confidence":0.92,"replyQualityScore":66,
                "followUpPriority":88,"needsFollowUp":true,"missedOpportunity":true}
                """;
        }

        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string? apiKeyOverride = null) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string? apiKeyOverride = null) => throw new NotSupportedException();
    }
}
