using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Advertising.Services;
using Modules.AI.Services;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Jobs;
using Modules.Conversations.Domain;
using Modules.CRM.API;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class SalesIntelligenceRecencyTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(-1200, true)]
    [InlineData(-1201, false)]
    [InlineData(1, false)]
    public void Recent_activity_2026_08_31_regression_has_closed_twenty_minute_boundaries(
        int offsetSeconds,
        bool expected)
    {
        var nowUtc = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);

        var allowed = SalesAnalysisRecencyPolicy.Allows(
            nowUtc.AddSeconds(offsetSeconds), nowUtc);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task Recent_job_2026_08_31_regression_analyzes_new_activity_without_historical_backfill()
    {
        var nowUtc = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);
        var projectId = Guid.NewGuid();
        await using var db = CreateDatabase(projectId);
        var historicalConversation = AddConversation(
            db, projectId, nowUtc.AddDays(-30), nowUtc.AddDays(-1));
        var recentlyActiveConversation = AddConversation(
            db, projectId, nowUtc.AddDays(-30), nowUtc.AddMinutes(-5));
        await db.SaveChangesAsync();
        var gemini = new StaticGeminiClient();
        var analyzer = CreateAnalyzer(db, gemini);
        var job = new SalesIntelligenceJob(
            db,
            analyzer,
            GatewaySession("Connected", "201000000000"),
            NullLogger<SalesIntelligenceJob>.Instance);

        await job.AnalyzeRecentAsync(nowUtc, CancellationToken.None);

        var analysis = await db.ConversationSalesAnalyses.SingleAsync();
        Assert.Equal(recentlyActiveConversation.Id, analysis.ConversationId);
        Assert.NotEqual(historicalConversation.Id, analysis.ConversationId);
    }

    [Fact]
    public async Task Recent_job_2026_08_31_regression_skips_disconnected_WhatsApp_without_blocking_Messenger()
    {
        var nowUtc = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);
        var projectId = Guid.NewGuid();
        await using var db = CreateDatabase(projectId);
        var whatsAppConversation = AddConversation(
            db, projectId, nowUtc.AddHours(-1), nowUtc.AddMinutes(-5));
        var messengerConversation = AddConversation(
            db, projectId, nowUtc.AddHours(-1), nowUtc.AddMinutes(-4), "Messenger");
        await db.SaveChangesAsync();
        var gemini = new StaticGeminiClient();
        var job = new SalesIntelligenceJob(
            db,
            CreateAnalyzer(db, gemini),
            GatewaySession("Disconnected", null),
            NullLogger<SalesIntelligenceJob>.Instance);

        await job.AnalyzeRecentAsync(nowUtc, CancellationToken.None);

        var analysis = await db.ConversationSalesAnalyses.SingleAsync();
        Assert.Equal(messengerConversation.Id, analysis.ConversationId);
        Assert.NotEqual(whatsAppConversation.Id, analysis.ConversationId);
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("analyze-all")]
    public void Bulk_analysis_2026_08_31_regression_returns_gone_without_ai_requests(string endpoint)
    {
        var projectId = Guid.NewGuid();
        using var db = CreateDatabase(projectId);
        var gemini = new RejectingGeminiClient();
        var controller = CreateController(db, projectId, gemini);

        var response = endpoint switch
        {
            "refresh" => controller.RefreshSalesIntelligence(projectId),
            "analyze-all" => controller.AnalyzeAllSalesConversations(projectId),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
        };

        AssertHistoricalAnalysisDisabled(response);
    }

    [Fact]
    public async Task Manual_analysis_2026_08_31_regression_rejects_an_old_conversation_without_ai_requests()
    {
        var projectId = Guid.NewGuid();
        await using var db = CreateDatabase(projectId);
        var conversation = AddConversation(
            db,
            projectId,
            new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 1, 8, 5, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();
        var gemini = new RejectingGeminiClient();
        var controller = CreateController(db, projectId, gemini);

        var response = await controller.AnalyzeConversation(
            projectId, conversation.Id, CancellationToken.None);

        AssertHistoricalAnalysisDisabled(response);
        Assert.Empty(db.ConversationSalesAnalyses);
    }

    private static ReportsController CreateController(
        AppDbContext db,
        Guid projectId,
        IGeminiClient gemini)
    {
        var vault = new ProjectSecretVault(new EphemeralDataProtectionProvider());
        var analyzer = new ConversationSalesAnalyzer(db, gemini, vault);
        var intelligence = new SalesIntelligenceService(db, gemini, vault);
        return new(db, intelligence, analyzer, new ProjectAuthorizationService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Owner(projectId) }
            }
        };
    }

    private static ConversationSalesAnalyzer CreateAnalyzer(
        AppDbContext db,
        IGeminiClient gemini) => new(
            db,
            gemini,
            new ProjectSecretVault(new EphemeralDataProtectionProvider()));

    private static AppDbContext CreateDatabase(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
        db.ProjectSettings.Add(new ProjectSettings
        {
            ProjectId = projectId,
            GeminiApiKey = "test-project-key",
            GeminiModel = "gemini-3.5-flash"
        });
        return db;
    }

    private static Conversation AddConversation(
        AppDbContext db,
        Guid projectId,
        DateTime startedAtUtc,
        DateTime lastMessageAtUtc,
        string channel = "WhatsApp")
    {
        var customer = new Customer
        {
            ProjectId = projectId,
            Name = "عميل",
            PhoneNumber = $"010{Guid.NewGuid():N}"[..11],
            City = "القاهرة"
        };
        var conversation = new Conversation
        {
            ProjectId = projectId,
            CustomerId = customer.Id,
            Channel = channel,
            Status = "Open",
            CreatedAt = startedAtUtc,
            LastMessageTimestamp = lastMessageAtUtc
        };
        db.Customers.Add(customer);
        db.Conversations.Add(conversation);
        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            ExternalMessageId = Guid.NewGuid().ToString("N"),
            Direction = "Incoming",
            Content = "محتاج أعرف السعر",
            MessageType = "Text",
            Timestamp = lastMessageAtUtc
        });
        return conversation;
    }

    private static WhatsAppGatewaySessionClient GatewaySession(string status, string? phoneNumber)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsAppGateway:Url"] = "http://gateway.test"
            })
            .Build();
        return new WhatsAppGatewaySessionClient(
            new HttpClient(new GatewayStatusHandler(status, phoneNumber)),
            configuration);
    }

    private static ClaimsPrincipal Owner(Guid projectId) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, "Owner"),
        new Claim("ProjectId", projectId.ToString())
    ], "test"));

    private static void AssertHistoricalAnalysisDisabled(IActionResult action)
    {
        var response = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status410Gone, response.StatusCode);
        var body = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal("SALES_HISTORICAL_ANALYSIS_DISABLED", body.GetProperty("code").GetString());
    }

    private sealed class StaticGeminiClient : IGeminiClient
    {
        public Task<string> GenerateReplyAsync(
            string messageContent,
            string? apiKeyOverride = null,
            string? modelOverride = null,
            string? cachedContentId = null)
        {
            return Task.FromResult("""
                {"stage":"Engaged","outcome":"Active","primaryReason":"None","secondaryReasons":[],
                "summary":"العميل يسأل عن السعر.","recommendation":"أرسل تفاصيل السعر.","evidence":[],
                "lastCustomerIntent":"معرفة السعر","confidence":0.9,"replyQualityScore":80,
                "followUpPriority":70,"needsFollowUp":true,"missedOpportunity":false}
                """);
        }

        public Task<string> GenerateReplyAsync(
            string messageContent,
            byte[] fileBytes,
            string mimeType,
            string? apiKeyOverride = null,
            string? modelOverride = null,
            string? cachedContentId = null) => throw new NotSupportedException();

        public Task<float[]> GenerateEmbeddingAsync(
            string text,
            string? apiKeyOverride = null) => throw new NotSupportedException();

        public Task<int> CountTokensAsync(
            string messageContent,
            string? apiKeyOverride = null,
            string? modelOverride = null) => throw new NotSupportedException();

        public Task<string> CreateContextCacheAsync(
            string staticContent,
            string model,
            int ttlSeconds,
            string? apiKeyOverride = null) => throw new NotSupportedException();
    }

    private sealed class RejectingGeminiClient : IGeminiClient
    {
        public Task<string> GenerateReplyAsync(
            string messageContent,
            string? apiKeyOverride = null,
            string? modelOverride = null,
            string? cachedContentId = null) => throw UnexpectedRequest();

        public Task<string> GenerateReplyAsync(
            string messageContent,
            byte[] fileBytes,
            string mimeType,
            string? apiKeyOverride = null,
            string? modelOverride = null,
            string? cachedContentId = null) => throw UnexpectedRequest();

        public Task<float[]> GenerateEmbeddingAsync(
            string text,
            string? apiKeyOverride = null) => throw UnexpectedRequest();

        public Task<int> CountTokensAsync(
            string messageContent,
            string? apiKeyOverride = null,
            string? modelOverride = null) => throw UnexpectedRequest();

        public Task<string> CreateContextCacheAsync(
            string staticContent,
            string model,
            int ttlSeconds,
            string? apiKeyOverride = null) => throw UnexpectedRequest();

        private static InvalidOperationException UnexpectedRequest() =>
            new("Historical sales analysis must not reach the AI boundary.");
    }

    private sealed class GatewayStatusHandler(string status, string? phoneNumber) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var responseJson = JsonSerializer.Serialize(new
            {
                status,
                phoneNumber,
                error = (string?)null
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
