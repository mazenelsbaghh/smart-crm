using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Campaigns.Application.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class GeneratedCustomerTextTests
{
    [Theory]
    [InlineData(false, "أهلاً يا فندم، موعد حضرتك يوم الجمعة.")]
    [InlineData(true, "أهلاً يا فندم، موعد حضرتك يوم الجمعة.")]
    [InlineData(true, "{{CustomerName}}، أهلاً بحضرتك")]
    [InlineData(false, "[تذكير] موعد حضرتك يوم الجمعة.")]
    public async Task Json_generation_returns_only_the_customer_message(bool campaign, string message)
    {
        using var http = new HttpClient(new GeminiResponse(JsonSerializer.Serialize(new { message })));
        var client = Client(http);

        var text = campaign
            ? await new CampaignAIService(client, null!, null!).GenerateCampaignCopyAsync("متابعة", "موعدك", "الجمعة")
            : await new AIMarketingBrain(client, null!, null!, new AIBehaviorSettingsService())
                .RewriteFollowUpNotesAsync("أحمد", "ذكره بالموعد", false);

        Assert.Equal(message, text);
    }

    [Theory]
    [InlineData("{\"message\":42}")]
    [InlineData("{\"reasoning\":\"internal analysis\"}")]
    [InlineData("{\"message\":\"{\\\"replyContent\\\":\\\"nested output\\\"}\"}")]
    public async Task Malformed_followup_output_never_reaches_the_customer(string response)
    {
        using var http = new HttpClient(new GeminiResponse(response));
        var brain = new AIMarketingBrain(Client(http), null!, null!, new AIBehaviorSettingsService());

        var text = await brain.RewriteFollowUpNotesAsync("أحمد", "متابعة", false);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain('{', text);
        Assert.DoesNotContain("internal analysis", text);
        Assert.DoesNotContain("nested output", text);
    }

    [Fact]
    public async Task Campaign_output_with_no_message_fails_instead_of_becoming_sendable_copy()
    {
        using var http = new HttpClient(new GeminiResponse("{\"reasoning\":\"internal analysis\"}"));
        var campaigns = new CampaignAIService(Client(http), null!, null!);

        await Assert.ThrowsAsync<DeferredAiReplyException>(() =>
            campaigns.GenerateCampaignCopyAsync("عرض", "مرحباً", "اهتمام العميل"));
    }

    private static GeminiClient Client(HttpClient http) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Gemini:ApiKey"] = "test-provider-key" }).Build(),
        new GeminiMockHandler(new HostingEnvironment { EnvironmentName = "Production" }),
        NullLogger<GeminiClient>.Instance, http);

    private sealed class GeminiResponse(string text) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text } } } } }
                }))
            });
    }
}
