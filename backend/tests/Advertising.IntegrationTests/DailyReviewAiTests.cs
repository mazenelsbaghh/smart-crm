using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Advertising.Services;
using Modules.AI.Services;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Jobs;
using Modules.Brain.Services;
using Modules.Conversations.Domain;
using Modules.Projects.Domain;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DailyReviewAiTests(PostgresFixture postgres)
{
    private readonly ProjectSecretVault _vault = new(new EphemeralDataProtectionProvider());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrective_draft_has_no_actions_and_rejects_a_result_overtaken_by_a_new_message(bool newMessage)
    {
        var project = Guid.NewGuid();
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var conversation = DailyReplyReviewTests.SeedConversation(db, project);
        conversation.HumanHandoffReplyId = Guid.NewGuid();
        conversation.Status = "Pending";
        var handoff = conversation.HumanHandoffReplyId;
        var last = DailyReplyReviewTests.Message(conversation, DateTime.UtcNow.AddMinutes(-10));
        db.AddRange(last, new ProjectSettings { ProjectId = project, CustomerReplyProvider = "Gemini", GeminiApiKey = "test-key" });
        await db.SaveChangesAsync();
        var gemini = new ReviewGemini("""
            {"replyContent":"الرقم عندنا بالفعل، هنراجع موعد السيشن قبل التأكيد.","requestHuman":true,
             "cancelGroupBooking":true,"suggestedGroupBookingId":"11111111-1111-1111-1111-111111111111",
             "suggestedFollowUp":{"needed":true,"notes":"لا ترسل هذه المتابعة"}}
            """, async () =>
            {
                if (!newMessage) return;
                await using var incomingDb = postgres.CreateContext();
                incomingDb.Messages.Add(DailyReplyReviewTests.Message(conversation, DateTime.UtcNow));
                await incomingDb.SaveChangesAsync();
            });
        var brain = new AIMarketingBrain(gemini, null!, null!, new AIBehaviorSettingsService());
        var drafts = new CorrectiveReplyDraftService(db, brain, _vault, new AIBehaviorSettingsService(), new AICompanyBrain(db, gemini));

        if (newMessage) await Assert.ThrowsAsync<StaleCorrectiveDraftException>(() => drafts.GenerateAsync(project, conversation.Id, default));
        else
        {
            var draft = await drafts.GenerateAsync(project, conversation.Id, default);
            Assert.Equal(last.Id, draft.BasedOnMessageId);
            Assert.Contains("الرقم عندنا بالفعل", draft.Content);
        }
        db.ChangeTracker.Clear();
        var persisted = await db.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal(handoff, persisted.HumanHandoffReplyId);
        Assert.Equal("Pending", persisted.Status);
        Assert.Equal(newMessage ? 2 : 1, await db.Messages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == conversation.Id));
        Assert.False(await db.FollowUps.IgnoreQueryFilters().AnyAsync(f => f.ProjectId == project));
        Assert.False(await db.GroupAppointmentBookings.IgnoreQueryFilters().AnyAsync(b => b.ProjectId == project));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => drafts.GenerateAsync(Guid.NewGuid(), conversation.Id, default));
    }

    [Fact]
    public async Task Daily_backlog_reviews_persisted_chats_beyond_twenty_minutes_even_when_whatsapp_is_disconnected()
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var project = Guid.NewGuid();
        var conversation = DailyReplyReviewTests.SeedConversation(db, project);
        conversation.Channel = "WhatsApp";
        conversation.LastMessageTimestamp = DateTime.UtcNow.AddHours(-8);
        var message = DailyReplyReviewTests.Message(conversation, conversation.LastMessageTimestamp);
        db.AddRange(message, new ProjectSettings { ProjectId = project, GeminiApiKey = "test-key" });
        await db.SaveChangesAsync();
        var gemini = new ReviewGemini("""
            {"stage":"Engaged","outcome":"Active","primaryReason":"Unknown","secondaryReasons":[],
             "summary":"العميل لم يحصل على إجابة السؤال.","recommendation":"أجب عن سؤال الموعد.","evidence":[],
             "lastCustomerIntent":"سؤال عن موعد","confidence":0.9,"replyQualityScore":20,
             "followUpPriority":80,"needsFollowUp":true,"missedOpportunity":true}
            """);
        using var http = new HttpClient(new NoGatewayNetwork());
        var analyzer = new ConversationSalesAnalyzer(db, gemini, _vault);
        var job = new SalesIntelligenceJob(db, analyzer, new WhatsAppGatewaySessionClient(http, new ConfigurationBuilder().Build()), NullLogger<SalesIntelligenceJob>.Instance);

        await job.AnalyzeDailyBacklogAsync(default);

        var analysis = await db.ConversationSalesAnalyses.IgnoreQueryFilters().SingleAsync(a => a.ConversationId == conversation.Id);
        Assert.Equal(20, analysis.ReplyQualityScore);
        Assert.Equal(message.Timestamp, analysis.AnalyzedThroughMessageAtUtc);
        Assert.Equal(ConversationSalesAnalyzer.CurrentAnalysisVersion, analysis.AnalysisVersion);
        Assert.False(await db.Messages.IgnoreQueryFilters().AnyAsync(m => m.ConversationId == conversation.Id && m.Direction == "Outgoing"));
    }

    [Theory]
    [InlineData(1, null, false)]
    [InlineData(3, 5, false)]
    [InlineData(3, 11, true)]
    [InlineData(3, null, true)]
    public async Task Automatic_analysis_waits_for_quiet_and_cooldown(int messageAgeMinutes, int? analysisAgeMinutes, bool expected)
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var project = Guid.NewGuid();
        var conversation = DailyReplyReviewTests.SeedConversation(db, project);
        conversation.LastMessageTimestamp = DateTime.UtcNow.AddMinutes(-messageAgeMinutes);
        db.AddRange(DailyReplyReviewTests.Message(conversation, conversation.LastMessageTimestamp),
            new ProjectSettings { ProjectId = project, GeminiApiKey = "test-key" });
        if (analysisAgeMinutes.HasValue)
            db.ConversationSalesAnalyses.Add(new Modules.Analytics.Domain.ConversationSalesAnalysis
            {
                ProjectId = project, ConversationId = conversation.Id, CustomerId = conversation.CustomerId,
                AnalyzedAtUtc = DateTime.UtcNow.AddMinutes(-analysisAgeMinutes.Value),
                AnalyzedThroughMessageAtUtc = conversation.LastMessageTimestamp.AddHours(-1),
                AnalysisVersion = ConversationSalesAnalyzer.CurrentAnalysisVersion
            });
        await db.SaveChangesAsync();
        using var http = new HttpClient(new NoGatewayNetwork());
        var analyzer = new ConversationSalesAnalyzer(db, new ReviewGemini(ValidAnalysis), _vault);
        var job = new SalesIntelligenceJob(db, analyzer,
            new WhatsAppGatewaySessionClient(http, new ConfigurationBuilder().Build()), NullLogger<SalesIntelligenceJob>.Instance);
        await job.AnalyzeDailyBacklogAsync(default);
        db.ChangeTracker.Clear();
        var persisted = await db.ConversationSalesAnalyses.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.ConversationId == conversation.Id);
        Assert.Equal(expected, persisted?.AnalyzedThroughMessageAtUtc >= conversation.LastMessageTimestamp);
    }

    [Fact]
    public async Task Concurrent_automatic_analysis_reuses_the_persisted_result_before_calling_Gemini_again()
    {
        await using var seedDb = postgres.CreateContext();
        await seedDb.Database.MigrateAsync();
        var project = Guid.NewGuid();
        var conversation = DailyReplyReviewTests.SeedConversation(seedDb, project);
        seedDb.AddRange(DailyReplyReviewTests.Message(conversation, conversation.LastMessageTimestamp),
            new ProjectSettings { ProjectId = project, GeminiApiKey = "test-key" });
        await seedDb.SaveChangesAsync();
        var generations = 0;
        var gemini = new ReviewGemini(ValidAnalysis, async () =>
        {
            Interlocked.Increment(ref generations);
            await Task.Delay(100);
        });
        await using var firstDb = postgres.CreateContext();
        await using var secondDb = postgres.CreateContext();
        var analyses = await Task.WhenAll(
            new ConversationSalesAnalyzer(firstDb, gemini, _vault).AnalyzeAsync(project, conversation.Id, default),
            new ConversationSalesAnalyzer(secondDb, gemini, _vault).AnalyzeAsync(project, conversation.Id, default));
        Assert.Equal(analyses[0].Id, analyses[1].Id);
        Assert.Equal(1, generations);
    }

    private const string ValidAnalysis = """
        {"stage":"Engaged","outcome":"Active","primaryReason":"Unknown","secondaryReasons":[],
         "summary":"استفسار","recommendation":"أجب عن السؤال","evidence":[],"confidence":0.9,
         "replyQualityScore":80,"followUpPriority":0,"needsFollowUp":false,"missedOpportunity":false}
        """;

    private sealed class NoGatewayNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Offline gateway: persisted review must not need a session.");
    }

    private sealed class ReviewGemini(string response, Func<Task>? duringGeneration = null) : IGeminiClient
    {
        public async Task<string> GenerateReplyAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null)
        { if (duringGeneration != null) await duringGeneration(); return response; }
        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string? apiKeyOverride = null) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string? apiKeyOverride = null) => throw new NotSupportedException();
    }
}
