using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
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
    public async Task Concurrent_analysis_returns_the_single_persisted_result()
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
            first.AnalyzeAsync(projectId, conversationId, CancellationToken.None),
            second.AnalyzeAsync(projectId, conversationId, CancellationToken.None));

        await using var verification = postgres.CreateContext(tenant);
        var persisted = await verification.ConversationSalesAnalyses
            .Where(analysis => analysis.ProjectId == projectId && analysis.ConversationId == conversationId)
            .ToListAsync();
        Assert.Single(persisted);
        Assert.All(results, result => Assert.Equal(persisted[0].Id, result.Id));
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
