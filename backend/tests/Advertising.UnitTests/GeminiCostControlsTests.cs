using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class GeminiCostControlsTests
{
    [Theory]
    [InlineData("gemini-3.5-flash", false, null)]
    [InlineData("gemini-3.1-flash-lite", true, "cachedContents/test")]
    [InlineData("gemini-2.5-flash-lite", false, "cachedContents/test")]
    public async Task Generation_reduces_thinking_without_imposing_a_truncating_output_cap(
        string model, bool media, string? cache)
    {
        using var handler = new ResponseHandler("STOP");
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        var reply = media
            ? await client.GenerateReplyAsync("request", new byte[] { 1 }, "image/png", "test-key", model, cache)
            : await client.GenerateReplyAsync("request", "test-key", model, cache);

        Assert.Equal("{\"reply\":\"ok\"}", reply);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        var config = request.RootElement.GetProperty("generationConfig");
        Assert.False(config.TryGetProperty("maxOutputTokens", out _));
        var thinking = config.GetProperty("thinkingConfig");
        if (model.StartsWith("gemini-3."))
            Assert.Equal(model.EndsWith("-lite") ? "minimal" : "low", thinking.GetProperty("thinkingLevel").GetString());
        else
            Assert.Equal(0, thinking.GetProperty("thinkingBudget").GetInt32());
        if (cache is not null) Assert.Equal(cache, request.RootElement.GetProperty("cachedContent").GetString());
    }

    [Theory]
    [InlineData("MAX_TOKENS")]
    [InlineData("SAFETY")]
    public async Task Incomplete_generation_is_never_returned_as_actionable_json(string finishReason)
    {
        using var handler = new ResponseHandler(finishReason);
        using var http = new HttpClient(handler);
        var reply = await CreateClient(http).GenerateReplyAsync("request", "test-key");
        Assert.StartsWith("[AI_ERROR]", reply);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cachedContents/existing")]
    public async Task New_lessons_reach_generation_even_with_existing_cached_instructions(string? cache)
    {
        using var handler = new ResponseHandler("STOP", "{\"replyContent\":\"ok\"}");
        using var http = new HttpClient(handler);
        var brain = new AIMarketingBrain(CreateClient(http), null!, null!, new AIBehaviorSettingsService());
        var lesson = ReplyLearningService.Lessons["ReuseKnownDetails"];
        var reply = await brain.AnalyzeAndGenerateReplyAsync("السؤال", "test-key",
            customerReply: new CustomerReplyRuntime("Gemini", "gemini-3.5-flash", cache, lesson),
            systemPromptOverride: "Existing project instruction");
        Assert.Equal("ok", reply.ReplyContent);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        var prompt = request.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains(lesson, prompt);
        if (cache is null) Assert.Contains("Existing project instruction", prompt);
        else Assert.Equal(cache, request.RootElement.GetProperty("cachedContent").GetString());
    }

    private static GeminiClient CreateClient(HttpClient http) => new(
        new ConfigurationBuilder().Build(),
        new GeminiMockHandler(new HostingEnvironment { EnvironmentName = "Production" }),
        NullLogger<GeminiClient>.Instance, http);

    private sealed class ResponseHandler(string finishReason, string responseText = "{\"reply\":\"ok\"}") : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    candidates = new[] { new { finishReason, content = new { parts = new object[]
                    {
                        new { thought = true, text = "internal reasoning" },
                        new { text = responseText[..(responseText.Length / 2)] }, new { text = responseText[(responseText.Length / 2)..] }
                    } } } },
                    usageMetadata = new { promptTokenCount = 100, candidatesTokenCount = 10, thoughtsTokenCount = 5, totalTokenCount = 115 }
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
