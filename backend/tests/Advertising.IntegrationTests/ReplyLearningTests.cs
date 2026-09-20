using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Modules.CRM.API;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Modules.Analytics.Application.Services;
using Modules.Conversations.Domain;
using Modules.Projects.Domain;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ReplyLearningTests(PostgresFixture postgres)
{
    private readonly ProjectSecretVault vault = new(new EphemeralDataProtectionProvider());

    [Fact]
    public async Task Repeated_evidenced_mistakes_become_persistent_project_lessons_and_can_be_disabled()
    {
        var project = Guid.NewGuid();
        var first = await SeedAsync(project);
        await AnalyzeAsync(project, first, Payload(first.Id));
        await AnalyzeAsync(project, first, Payload(first.Id));
        await using var db = postgres.CreateContext();
        var learning = new ReplyLearningService(db);
        Assert.Empty(await learning.InstructionsAsync(project, "Messenger"));
        var second = await SeedAsync(project);
        await AnalyzeAsync(project, second, Payload(second.Id));
        var instructions = await learning.InstructionsAsync(project, "Messenger");
        Assert.Contains(ReplyLearningService.Lessons["AnswerLatestQuestion"], instructions);
        Assert.DoesNotContain(first.Content, instructions);
        Assert.Empty(await learning.InstructionsAsync(Guid.NewGuid(), "Messenger"));
        Assert.Empty(await learning.InstructionsAsync(project, "WhatsApp"));
        var lesson = await db.ReplyLessons.IgnoreQueryFilters().SingleAsync(l => l.ProjectId == project);
        Assert.Equal(2, await db.ReplyLessonEvidence.IgnoreQueryFilters().CountAsync(e => e.LessonId == lesson.Id));
        var controller = new ReplyLearningController(db, new ProjectAuthorizationService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("ProjectId", project.ToString()), new Claim(ClaimTypes.Role, "Agent")
        }, "test"));
        Assert.IsType<ForbidResult>(await controller.Update(project, lesson.Id, new(false), default));
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("ProjectId", project.ToString()), new Claim(ClaimTypes.Role, "Owner")
        }, "test"));
        Assert.IsType<ForbidResult>(await controller.Get(Guid.NewGuid(), default));
        Assert.IsType<NoContentResult>(await controller.Update(project, lesson.Id, new(false), default));
        var third = await SeedAsync(project);
        await AnalyzeAsync(project, third, Payload(third.Id));
        Assert.Empty(await learning.InstructionsAsync(project, "Messenger"));
    }

    [Theory]
    [InlineData("Outgoing", "InventDiscount", "الاشتراك ألف جنيه", 0.95)]
    [InlineData("Incoming", "AnswerLatestQuestion", "الاشتراك ألف جنيه", 0.95)]
    [InlineData("Outgoing", "AnswerLatestQuestion", "اقتباس غير موجود", 0.95)]
    [InlineData("Outgoing", "AnswerLatestQuestion", "الاشتراك ألف جنيه", 0.5)]
    public async Task Unsupported_or_unevidenced_learning_is_not_persisted(string direction, string code, string quote, double confidence)
    {
        var project = Guid.NewGuid();
        var message = await SeedAsync(project, direction);
        await AnalyzeAsync(project, message, Payload(message.Id, code, quote, confidence));
        await using var db = postgres.CreateContext();
        Assert.False(await db.ReplyLessonEvidence.IgnoreQueryFilters().AnyAsync(e => e.ProjectId == project));
    }

    [Theory]
    [InlineData(90, true)]
    [InlineData(30, false)]
    public async Task Good_or_resolved_replies_do_not_activate_failure_lessons(int quality, bool unresolved)
    {
        var project = Guid.NewGuid();
        var message = await SeedAsync(project);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(Payload(message.Id))!;
        payload["replyQualityScore"] = quality;
        payload["hasUnresolvedReplyIssue"] = unresolved;
        await AnalyzeAsync(project, message, payload.ToJsonString());
        await using var db = postgres.CreateContext();
        Assert.False(await db.ReplyLessonEvidence.IgnoreQueryFilters().AnyAsync(e => e.ProjectId == project));
    }

    private async Task<Message> SeedAsync(Guid project, string direction = "Outgoing")
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var conversation = DailyReplyReviewTests.SeedConversation(db, project);
        var message = DailyReplyReviewTests.Message(conversation, conversation.LastMessageTimestamp, direction);
        message.Content = "الاشتراك ألف جنيه";
        db.Messages.Add(message);
        if (!await db.ProjectSettings.IgnoreQueryFilters().AnyAsync(s => s.ProjectId == project))
            db.ProjectSettings.Add(new ProjectSettings { ProjectId = project, GeminiApiKey = "test-key" });
        await db.SaveChangesAsync();
        return message;
    }

    private async Task AnalyzeAsync(Guid project, Message message, string response)
    {
        await using var db = postgres.CreateContext();
        await new ConversationSalesAnalyzer(db, new LearningGemini(response), vault)
            .AnalyzeAsync(project, message.ConversationId, default);
    }

    private static string Payload(Guid messageId, string code = "AnswerLatestQuestion", string quote = "الاشتراك ألف جنيه", double confidence = 0.95) =>
        JsonSerializer.Serialize(new { stage = "Engaged", outcome = "Active", primaryReason = "Unknown",
            summary = "الرد عن السعر بدل سؤال الموعد", recommendation = "أجب عن الموعد", confidence,
            replyQualityScore = 30, hasUnresolvedReplyIssue = true,
            replyLessons = new[] { new { code, messageId, quote } } });

    private sealed class LearningGemini(string response) : IGeminiClient
    {
        public Task<string> GenerateReplyAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null) => Task.FromResult(response);
        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string? apiKeyOverride = null) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string text, string? apiKeyOverride = null, string? modelOverride = null) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string text, string model, int ttl, string? apiKeyOverride = null) => throw new NotSupportedException();
    }
}
