using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class CustomerReplyRoutingTests
{
    [Fact]
    public async Task OpenAI_runtime_routes_customer_reply_away_from_Gemini()
    {
        var marketingBrain = CreateMarketingBrain(
            new JsonReplyHttpMessageHandler(),
            new RejectingHttpMessageHandler("xAI must not generate OpenAI customer replies."));

        var analysis = await marketingBrain.AnalyzeAndGenerateReplyAsync(
            messageContent: "مرحباً",
            apiKeyOverride: "openai-test-routing-key",
            customerReply: new CustomerReplyRuntime("OpenAI", "gpt-5.6"));

        Assert.Equal("أهلاً بحضرتك", analysis.ReplyContent);
        Assert.Equal("greeting", analysis.Intent);
    }

    [Fact]
    public async Task XAI_runtime_routes_customer_reply_away_from_other_providers()
    {
        var marketingBrain = CreateMarketingBrain(
            new RejectingHttpMessageHandler("OpenAI must not generate xAI customer replies."),
            new JsonReplyHttpMessageHandler());

        var analysis = await marketingBrain.AnalyzeAndGenerateReplyAsync(
            messageContent: "ممكن تفاصيل؟",
            apiKeyOverride: "xai-test-routing-key",
            customerReply: new CustomerReplyRuntime("xAI", "grok-4.6"));

        Assert.Equal("أهلاً بحضرتك", analysis.ReplyContent);
        Assert.Equal("greeting", analysis.Intent);
    }

    [Fact]
    public async Task XAI_incomplete_structured_reply_uses_the_configured_safe_fallback()
    {
        var marketingBrain = CreateMarketingBrain(
            new RejectingHttpMessageHandler("OpenAI must not generate xAI customer replies."),
            new JsonReplyHttpMessageHandler("{\"intent\":\"inquiry\"}"));
        var settings = new AIBehaviorSettings
        {
            Fallbacks = new FallbackMessageSettings
            {
                InvalidAiOutput = "هنراجع رسالتك ونرد عليك حالاً"
            }
        };

        var analysis = await marketingBrain.AnalyzeAndGenerateReplyAsync(
            messageContent: "ممكن تفاصيل؟",
            apiKeyOverride: "xai-test-routing-key",
            customerReply: new CustomerReplyRuntime("xAI", "grok-4.6"),
            aiBehaviorSettings: settings);

        Assert.Equal("هنراجع رسالتك ونرد عليك حالاً", analysis.ReplyContent);
    }

    [Fact]
    public async Task Unfiltered_brain_context_cannot_replace_the_provider_reply_with_a_price()
    {
        var marketingBrain = CreateMarketingBrain(
            new RejectingHttpMessageHandler("OpenAI must not generate xAI customer replies."),
            new JsonReplyHttpMessageHandler());

        var analysis = await marketingBrain.AnalyzeAndGenerateReplyAsync(
            messageContent: "السعر كام؟",
            apiKeyOverride: "xai-test-routing-key",
            brainContext: "معلومة غير معتمدة:\nالاشتراك الشهري: 999 جنيه",
            customerReply: new CustomerReplyRuntime("xAI", "grok-4.6"));

        Assert.Equal("أهلاً بحضرتك", analysis.ReplyContent);
    }

    [Fact]
    public async Task Provider_error_on_an_explicit_price_question_preserves_the_configured_error_reply()
    {
        const string configuredAiError = "حصل عطل مؤقت، وفريقنا هيراجع رسالتك";
        var marketingBrain = CreateMarketingBrain(
            new RejectingHttpMessageHandler("OpenAI must not generate xAI customer replies."),
            new StatusHttpMessageHandler(HttpStatusCode.ServiceUnavailable));
        var settings = new AIBehaviorSettings
        {
            Fallbacks = new FallbackMessageSettings
            {
                AiError = configuredAiError,
                InvalidAiOutput = "الرد غير مكتمل"
            }
        };

        var analysis = await marketingBrain.AnalyzeAndGenerateReplyAsync(
            messageContent: "السعر كام؟",
            apiKeyOverride: "xai-test-routing-key",
            brainContext: "الاشتراك الشهري: 1500 جنيه مصري شهرياً",
            customerReply: new CustomerReplyRuntime("xAI", "grok-4.6"),
            aiBehaviorSettings: settings);

        Assert.Equal(configuredAiError, analysis.ReplyContent);
    }

    private static AIMarketingBrain CreateMarketingBrain(
        HttpMessageHandler openAiHandler,
        HttpMessageHandler xaiHandler) => new(
        new RejectingGeminiClient(),
        new OpenAiResponsesClient(
            new HttpClient(openAiHandler) { BaseAddress = new Uri("https://api.openai.com/") },
            NullLogger<OpenAiResponsesClient>.Instance),
        new XaiResponsesClient(
            new HttpClient(xaiHandler) { BaseAddress = new Uri("https://api.x.ai/") },
            NullLogger<XaiResponsesClient>.Instance),
        new AIBehaviorSettingsService());

    private sealed class RejectingGeminiClient : IGeminiClient
    {
        public Task<string> GenerateReplyAsync(
            string messageContent,
            string apiKeyOverride = null!,
            string modelOverride = null!,
            string cachedContentId = null!) =>
            throw new InvalidOperationException("Gemini must not generate externally routed customer replies.");

        public Task<string> GenerateReplyAsync(
            string messageContent,
            byte[] fileBytes,
            string mimeType,
            string apiKeyOverride = null!,
            string modelOverride = null!,
            string cachedContentId = null!) =>
            throw new InvalidOperationException("Gemini must not generate externally routed customer replies.");

        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) =>
            throw new NotSupportedException();

        public Task<int> CountTokensAsync(
            string messageContent,
            string apiKeyOverride = null!,
            string modelOverride = null!) =>
            throw new NotSupportedException();

        public Task<string> CreateContextCacheAsync(
            string staticContent,
            string model,
            int ttlSeconds,
            string apiKeyOverride = null!) =>
            throw new NotSupportedException();
    }

    private sealed class JsonReplyHttpMessageHandler(
        string outputText = "{\"intent\":\"greeting\",\"sentiment\":\"neutral\",\"replyStyle\":\"Casual\",\"label\":\"ترحيب\",\"pipelineStage\":\"New\",\"replyContent\":\"أهلاً بحضرتك\",\"confidence\":0.95}") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new { output_text = outputText }),
                    Encoding.UTF8,
                    "application/json")
            });
    }

    private sealed class RejectingHttpMessageHandler(string error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(error);
    }

    private sealed class StatusHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
