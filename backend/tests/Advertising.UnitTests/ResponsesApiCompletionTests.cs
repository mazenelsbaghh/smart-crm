using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ResponsesApiCompletionTests
{
    public static TheoryData<string, string> RejectedResponses()
    {
        var cases = new TheoryData<string, string>();
        foreach (var provider in new[] { "OpenAI", "xAI" })
        {
            cases.Add(provider, """{"status":"incomplete","output_text":"{\"replyContent\":\"partial answer\"}"}""");
            cases.Add(provider, """{"status":"failed","output_text":"{\"replyContent\":\"failed answer\"}"}""");
            cases.Add(provider, """
                {"status":"completed","output":[{"type":"message","status":"incomplete",
                  "content":[{"type":"output_text","text":"{\"replyContent\":\"partial answer\"}"}]}]}
                """);
            cases.Add(provider, """
                {"status":"completed","output":[{"type":"message","status":"completed",
                  "content":[{"type":"output_text","text":"partial answer"},{"type":"refusal","refusal":"declined"}]}]}
                """);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(RejectedResponses))]
    public async Task Unfinished_or_refused_provider_output_is_not_delivered_as_a_customer_reply(string provider, string response)
    {
        var reply = await GenerateAsync(provider, response);

        Assert.StartsWith("[AI_ERROR]", reply);
        Assert.DoesNotContain("answer", reply);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("xAI")]
    public async Task Complete_reply_preserves_all_text_parts_and_ignores_reasoning_output(string provider)
    {
        var reply = await GenerateAsync(provider, """
            {
              "status":"completed",
              "output":[
                {"type":"reasoning","content":[{"type":"output_text","text":"private reasoning"}]},
                {"type":"message","status":"completed","content":[
                  {"type":"output_text","text":"{\"replyContent\":\"أهلاً "},
                  {"type":"output_text","text":"بحضرتك\"}"}]}
              ],
              "usage":{"input_tokens":120,"output_tokens":30,"total_tokens":150,
                "input_tokens_details":{"cached_tokens":80},"output_tokens_details":{"reasoning_tokens":10}}
            }
            """);

        Assert.Equal("{\"replyContent\":\"أهلاً بحضرتك\"}", reply);
    }

    private static Task<string> GenerateAsync(string provider, string responseJson)
    {
        var http = new HttpClient(new ProviderResponseHandler(responseJson))
        {
            BaseAddress = new Uri(provider == "OpenAI" ? "https://api.openai.com/" : "https://api.x.ai/")
        };
        return provider == "OpenAI"
            ? new OpenAiResponsesClient(http, NullLogger<OpenAiResponsesClient>.Instance)
                .GenerateReplyAsync(new OpenAiCustomerReplyRequest("customer message", "fake-key", "gpt-5.6"))
            : new XaiResponsesClient(http, NullLogger<XaiResponsesClient>.Instance)
                .GenerateReplyAsync(new XaiCustomerReplyRequest("customer message", "fake-key", "grok-4.6"));
    }

    private sealed class ProviderResponseHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
    }
}
