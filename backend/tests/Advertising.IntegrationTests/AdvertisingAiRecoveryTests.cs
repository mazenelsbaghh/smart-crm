using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Modules.Advertising.Workers;
using Modules.AI.Services;
using Modules.AI.Workers;
using Modules.Projects.Domain;
using System.Text.Json;
using Shared.Queue;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AdvertisingAiRecoveryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Duplicate_paid_work_is_generated_once_and_completion_survives_delayed_delivery()
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var work = await SeedWorkAsync(db);
        var request = Request(work);
        var gemini = new CountingGemini();
        await using var duplicate = postgres.CreateContext();

        await Task.WhenAll(new AdvertisingAiWorkConsumer(db, gemini, new TestSecrets()).HandleAsync(request),
            new AdvertisingAiWorkConsumer(duplicate, gemini, new TestSecrets()).HandleAsync(request));
        await using var replay = postgres.CreateContext();
        await new AdvertisingAiWorkConsumer(replay, gemini, new TestSecrets()).HandleAsync(request);

        var envelopes = await replay.IntegrationOutboxMessages.Where(envelope =>
            envelope.EventType == "AdvertisingAiWorkCompleted.v1" && envelope.PayloadJson.Contains(work.Id.ToString())).ToListAsync();
        var completion = JsonSerializer.Deserialize<AdvertisingAiWorkCompleted>(Assert.Single(envelopes).PayloadJson)!;
        await new AdvertisingAiWorkResultConsumer(replay, new AdvertisingAuditService(replay)).HandleAsync(completion);
        replay.ChangeTracker.Clear();
        var saved = await replay.AdvertisingAiWorkItems.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == work.Id);
        Assert.Equal(AiWorkState.Completed, saved.State);
        Assert.Equal(completion.StructuredResultJson, saved.ResultJson);
        Assert.Equal(1, gemini.CallCount);
    }

    [Theory]
    [InlineData("Expired")]
    [InlineData("Completed")]
    [InlineData("ChangedInput")]
    public async Task Obsolete_work_is_ignored_before_paid_generation(string reason)
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var work = await SeedWorkAsync(db);
        var request = Request(work);
        if (reason == "Expired") work.DeadlineUtc = DateTime.UtcNow.AddSeconds(-1);
        if (reason == "Completed") work.State = AiWorkState.Completed;
        if (reason == "ChangedInput") work.InputHash = "changed";
        await db.SaveChangesAsync();
        var gemini = new CountingGemini();

        await new AdvertisingAiWorkConsumer(db, gemini, new TestSecrets()).HandleAsync(request);

        Assert.Equal(0, gemini.CallCount);
        Assert.False(await db.IntegrationOutboxMessages.AnyAsync(envelope =>
            envelope.EventType == "AdvertisingAiWorkCompleted.v1" && envelope.PayloadJson.Contains(work.Id.ToString())));
    }

    [Fact]
    public async Task Missing_completion_expires_the_old_review_and_a_late_result_cannot_complete_its_replacement()
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = isolated.CreateContext();
        await db.Database.MigrateAsync();
        var projectId = Guid.NewGuid();
        var reviews = new AdvertisingDecisionAi(db);
        await reviews.ReviewActionAsync(projectId, "ResumeAd", "{}");
        var expired = await db.AdvertisingAiWorkItems.IgnoreQueryFilters().SingleAsync();
        expired.DeadlineUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        await reviews.ReviewActionAsync(projectId, "ResumeAd", "{}");
        await reviews.ReviewActionAsync(projectId, "ResumeAd", "{}");
        db.ChangeTracker.Clear();
        var requests = await db.AdvertisingAiWorkItems.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.Equal(AiWorkState.Expired, requests.Single(work => work.Id == expired.Id).State);
        var replacement = Assert.Single(requests, work => work.State == AiWorkState.Pending);
        Assert.True(replacement.DeadlineUtc > DateTime.UtcNow);
        var completions = new AdvertisingAiWorkResultConsumer(db, new AdvertisingAuditService(db));
        await completions.HandleAsync(Completion(expired, "{\"verdict\":\"APPROVE\",\"reasons\":[]}"));
        db.ChangeTracker.Clear();
        Assert.Equal(AiWorkState.Pending, (await db.AdvertisingAiWorkItems.IgnoreQueryFilters().SingleAsync(work => work.Id == replacement.Id)).State);
        await completions.HandleAsync(Completion(replacement, "{\"verdict\":\"APPROVE\",\"reasons\":[]}"));
        var nextReview = await reviews.ReviewActionAsync(projectId, "ResumeAd", "{}");
        Assert.Equal("AI_AUDITOR_PENDING", nextReview.Reason);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task Wrong_verdict_type_fails_the_review_and_allows_a_replacement(string verdictJson)
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = isolated.CreateContext();
        await db.Database.MigrateAsync();
        var projectId = Guid.NewGuid();
        var reviews = new AdvertisingDecisionAi(db);
        await reviews.ReviewActionAsync(projectId, "ResumeAd", "{}");
        var work = await db.AdvertisingAiWorkItems.IgnoreQueryFilters().SingleAsync();

        await new AdvertisingAiWorkResultConsumer(db, new AdvertisingAuditService(db))
            .HandleAsync(Completion(work, $"{{\"verdict\":{verdictJson},\"reasons\":[]}}"));

        db.ChangeTracker.Clear();
        var failed = await db.AdvertisingAiWorkItems.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(AiWorkState.Failed, failed.State);
        Assert.Equal("ADS_AI_RESULT_SCHEMA_INVALID", failed.FailureCode);
        Assert.NotNull(failed.CompletedAtUtc);
        await reviews.ReviewActionAsync(projectId, "ResumeAd", "{}");
        Assert.Single(await db.AdvertisingAiWorkItems.IgnoreQueryFilters().Where(row => row.State == AiWorkState.Pending).ToListAsync());
    }

    private static AdvertisingAiWorkCompleted Completion(AdvertisingAiWorkItem work, string json) => new()
    {
        ProjectId = work.ProjectId, RequestId = work.Id, OwnerId = work.OwnerId, OwnerVersion = work.OwnerVersion,
        InputHash = work.InputHash, StructuredResultJson = json, ModelVersion = "test-model"
    };

    private static async Task<AdvertisingAiWorkItem> SeedWorkAsync(Shared.Infrastructure.AppDbContext db)
    {
        var projectId = Guid.NewGuid();
        var work = new AdvertisingAiWorkItem { ProjectId = projectId, OwnerId = Guid.NewGuid(), OwnerVersion = 1,
            Purpose = "Strategist", InputHash = "test-input", InputJson = "{\"evidence\":true}", DeadlineUtc = DateTime.UtcNow.AddMinutes(5) };
        db.AddRange(new Project { Id = projectId, Name = "AI billing test" },
            new ProjectSettings { ProjectId = projectId, GeminiApiKey = "test-key" }, work);
        await db.SaveChangesAsync();
        return work;
    }

    private static AdvertisingAiWorkRequested Request(AdvertisingAiWorkItem work) => new()
    {
        ProjectId = work.ProjectId, RequestId = work.Id, OwnerId = work.OwnerId, OwnerVersion = work.OwnerVersion,
        InputHash = work.InputHash, SourcedInputJson = work.InputJson, Purpose = work.Purpose,
        DeadlineUtc = work.DeadlineUtc, SourceVersion = 1
    };

    private sealed class TestSecrets : IProjectSecretVault
    {
        public bool IsProtected(string? secret) => false;
        public string Protect(Guid projectId, string secret) => secret;
        public string? Unprotect(Guid projectId, string? secret) => secret;
    }

    private sealed class CountingGemini : IGeminiClient
    {
        private int calls;
        public int CallCount => calls;
        public Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult("{\"verdict\":\"APPROVE\",\"reasons\":[]}");
        }
        public Task<string> GenerateReplyAsync(string messageContent, byte[] bytes, string mimeType, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string apiKeyOverride = null!) => throw new NotSupportedException();
    }
}
