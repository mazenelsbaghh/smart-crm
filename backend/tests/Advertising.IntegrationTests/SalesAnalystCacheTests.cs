using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Modules.Conversations.Domain;
using Modules.GroupAppointments.Domain;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using StackExchange.Redis;
using System.Text.Json;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class SalesAnalystCacheTests(PostgresFixture postgres) : IAsyncLifetime
{
    private IContainer? container;
    private IConnectionMultiplexer redis = null!;
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ADVERTISING_TEST_REDIS_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            container = new ContainerBuilder("redis:7-alpine").WithPortBinding(6379, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
            await container.StartAsync();
            connection = $"{container.Hostname}:{container.GetMappedPublicPort(6379)}";
        }
        redis = await ConnectionMultiplexer.ConnectAsync(connection);
    }

    public async Task DisposeAsync()
    {
        redis.Dispose();
        if (container is not null) await container.DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_and_repeated_questions_reuse_all_paid_work_without_losing_analysis_coverage()
    {
        await using var firstDb = postgres.CreateContext();
        var question = await SeedAsync(firstDb);
        await using var secondDb = postgres.CreateContext();
        var gemini = new AnalystGemini();

        var answers = await Task.WhenAll(Service(firstDb, gemini).AskAsync(question, CancellationToken.None),
            Service(secondDb, gemini).AskAsync(question, CancellationToken.None));
        var repeated = await Service(secondDb, gemini).AskAsync(question, CancellationToken.None);

        Assert.All(answers, answer => Assert.Equal(repeated.Answer, answer.Answer));
        Assert.Equal(1, repeated.DetailedAnalysesReviewed);
        Assert.Equal(100m, repeated.AnalysisCoverage);
        Assert.Equal(2, gemini.CallCount);
    }

    [Theory]
    [InlineData("Evidence", 4)]
    [InlineData("Payment", 3)]
    [InlineData("Question", 4)]
    [InlineData("Window", 4)]
    [InlineData("Credential", 4)]
    [InlineData("Model", 4)]
    [InlineData("Tenant", 4)]
    public async Task Changed_sources_metrics_or_request_configuration_never_reuse_a_stale_answer(string change, int expectedCalls)
    {
        await using var db = postgres.CreateContext();
        var question = await SeedAsync(db);
        var gemini = new AnalystGemini();
        var original = await Service(db, gemini).AskAsync(question, CancellationToken.None);
        var analysis = await db.ConversationSalesAnalyses.IgnoreQueryFilters().SingleAsync(row => row.ProjectId == question.ProjectId);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync(row => row.ProjectId == question.ProjectId);
        if (change == "Evidence") analysis.Summary = "New customer evidence";
        if (change == "Payment")
        {
            var appointment = new GroupAppointment { ProjectId = question.ProjectId, Name = "Test appointment", DateTime = DateTime.UtcNow.AddDays(1) };
            db.AddRange(appointment, new GroupAppointmentBooking
            {
                ProjectId = question.ProjectId, CustomerId = analysis.CustomerId, GroupAppointmentId = appointment.Id,
                CustomerName = "Test customer", CustomerPhone = "201000000001", IsPaid = true
            });
        }
        if (change == "Credential") settings.GeminiApiKey = "rotated-test-key";
        if (change == "Model") settings.GeminiModel = "gemini-3.5-flash-lite";
        if (change == "Question") question = question with { Question = "What changed in payments?" };
        if (change == "Window") question = question with { FromUtc = question.FromUtc.AddHours(-1) };
        await db.SaveChangesAsync();
        if (change == "Tenant") question = await SeedAsync(db);

        var updated = await Service(db, gemini).AskAsync(question, CancellationToken.None);

        Assert.NotEqual(original.Answer, updated.Answer);
        Assert.Equal(expectedCalls, gemini.CallCount);
        Assert.Equal(1, updated.DetailedAnalysesReviewed);
    }

    [Fact]
    public async Task Malformed_provider_answers_are_not_reused_on_the_next_attempt()
    {
        await using var db = postgres.CreateContext();
        var question = await SeedAsync(db);
        var gemini = new AnalystGemini { FailFirst = true };
        var service = Service(db, gemini);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AskAsync(question, CancellationToken.None));
        var recovered = await service.AskAsync(question, CancellationToken.None);
        var repeated = await service.AskAsync(question, CancellationToken.None);

        Assert.Equal(recovered.Answer, repeated.Answer);
        Assert.Equal(3, gemini.CallCount);
    }

    private SalesIntelligenceService Service(AppDbContext db, AnalystGemini gemini) => new(db, gemini,
        new TestSecrets(), new AiGenerationCache(redis, protection, NullLogger<AiGenerationCache>.Instance));

    private static async Task<SalesAnalystQuestion> SeedAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();
        var projectId = Guid.NewGuid();
        var customer = new Customer { ProjectId = projectId, Name = "Test customer", PhoneNumber = "201000000001", City = "Cairo" };
        var conversation = new Conversation { ProjectId = projectId, CustomerId = customer.Id };
        db.AddRange(new Project { Id = projectId, Name = "Analyst cache test" }, customer, conversation,
            new ProjectSettings { ProjectId = projectId, GeminiApiKey = "test-key" },
            new ConversationSalesAnalysis
            {
                ProjectId = projectId, CustomerId = customer.Id, ConversationId = conversation.Id,
                Summary = "Customer asked about a course", Recommendation = "Answer the question", Confidence = 0.9m,
                AiStage = SalesConversationStage.Qualified, VerifiedStage = SalesConversationStage.Qualified,
                Outcome = SalesConversationOutcome.Active, LastMessageAtUtc = DateTime.UtcNow,
                AnalyzedAtUtc = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        return new(projectId, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1), "What should we improve?");
    }

    private sealed class TestSecrets : IProjectSecretVault
    {
        public bool IsProtected(string? secret) => false;
        public string Protect(Guid projectId, string secret) => secret;
        public string? Unprotect(Guid projectId, string? secret) => secret;
    }

    private sealed class AnalystGemini : IGeminiClient
    {
        private int calls;
        public int CallCount => calls;
        public bool FailFirst { get; init; }
        public Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!)
        {
            var generation = Interlocked.Increment(ref calls);
            return Task.FromResult(FailFirst && generation == 1 ? "invalid answer" :
                JsonSerializer.Serialize(new { answer = $"Validated analysis {generation}", conversationIds = Array.Empty<Guid>() }));
        }
        public Task<string> GenerateReplyAsync(string messageContent, byte[] bytes, string mimeType, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string apiKeyOverride = null!) => throw new NotSupportedException();
    }
}
