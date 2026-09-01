using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class XaiResponsesClientTests
{
    private const string FakeApiKey = "xai-test-key-never-use";
    private const string GenericFailure = "[AI_ERROR] Unable to reach xAI.";

    [Fact]
    public async Task Text_reply_uses_the_official_xai_responses_contract_without_exposing_the_key()
    {
        HttpMethod? requestMethod = null;
        Uri? requestUri = null;
        string? authorization = null;
        string? requestJson = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestMethod = request.Method;
            requestUri = request.RequestUri;
            authorization = request.Headers.Authorization?.ToString();
            requestJson = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""{"output_text":"{\"replyContent\":\"أهلاً بحضرتك\"}"}""");
        });
        var client = CreateClient(handler);

        var result = await client.GenerateReplyAsync(new XaiCustomerReplyRequest(
            "Customer message",
            FakeApiKey,
            "grok-4.6"));

        Assert.Equal("{\"replyContent\":\"أهلاً بحضرتك\"}", result);
        Assert.Equal(HttpMethod.Post, requestMethod);
        Assert.Equal(new Uri("https://api.x.ai/v1/responses"), requestUri);
        Assert.Equal($"Bearer {FakeApiKey}", authorization);
        Assert.DoesNotContain(FakeApiKey, requestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeApiKey, requestJson!, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(requestJson!);
        Assert.Equal("grok-4.6", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            "json_object",
            body.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task Nested_output_text_is_returned_as_the_structured_reply()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(JsonResponse("""
            {
              "output": [
                {
                  "content": [
                    { "type": "output_text", "text": "{\"replyContent\":\"تم الرد من جروك\"}" }
                  ]
                }
              ]
            }
            """)));
        var client = CreateClient(handler);

        var result = await client.GenerateReplyAsync(new XaiCustomerReplyRequest(
            "Customer message",
            FakeApiKey,
            "grok-4.6"));

        Assert.Equal("{\"replyContent\":\"تم الرد من جروك\"}", result);
    }

    [Fact]
    public async Task Voice_note_is_transcribed_by_xai_before_reply_generation()
    {
        var requestPaths = new List<string>();
        string? responseRequestJson = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestPaths.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal($"Bearer {FakeApiKey}", request.Headers.Authorization?.ToString());
            if (request.RequestUri.AbsolutePath == "/v1/stt")
            {
                Assert.StartsWith("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
                return JsonResponse("""{"text":"عايز أعرف التفاصيل"}""");
            }

            responseRequestJson = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""{"output_text":"{\"replyContent\":\"أكيد، دي التفاصيل\"}"}""");
        });
        var client = CreateClient(handler);

        var result = await client.GenerateReplyAsync(new XaiCustomerReplyRequest(
            "Customer sent a voice note",
            FakeApiKey,
            "grok-4.6",
            new CustomerReplyAttachment("voice"u8.ToArray(), "audio/ogg")));

        Assert.Equal("{\"replyContent\":\"أكيد، دي التفاصيل\"}", result);
        Assert.Equal(new[] { "/v1/stt", "/v1/responses" }, requestPaths);
        using var responseBody = JsonDocument.Parse(responseRequestJson!);
        Assert.Contains(
            "عايز أعرف التفاصيل",
            responseBody.RootElement.GetProperty("input").GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(FakeApiKey, responseRequestJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_key_returns_a_generic_failure_without_contacting_the_provider()
    {
        var client = CreateClient(new RejectingHttpMessageHandler());

        var result = await client.GenerateReplyAsync(new XaiCustomerReplyRequest(
            "Customer message",
            null,
            "grok-4.6"));

        Assert.Equal(GenericFailure, result);
    }

    [Fact]
    public async Task Provider_error_returns_a_generic_failure_without_leaking_provider_details()
    {
        var providerError = $"credential {FakeApiKey} was rejected";
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(providerError, Encoding.UTF8, "text/plain")
        }));
        var client = CreateClient(handler);

        var result = await client.GenerateReplyAsync(new XaiCustomerReplyRequest(
            "Customer message",
            FakeApiKey,
            "grok-4.6"));

        Assert.Equal(GenericFailure, result);
        Assert.DoesNotContain(FakeApiKey, result, StringComparison.Ordinal);
        Assert.DoesNotContain(providerError, result, StringComparison.Ordinal);
    }

    private static XaiResponsesClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://api.x.ai/") },
        NullLogger<XaiResponsesClient>.Instance);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }

    private sealed class RejectingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The provider must not be contacted without a key.");
    }
}
