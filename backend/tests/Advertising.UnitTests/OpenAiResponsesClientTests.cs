using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class OpenAiResponsesClientTests
{
    [Fact]
    public async Task Text_reply_uses_the_selected_GPT_5_6_model_and_JSON_output()
    {
        string? requestJson = null;
        string? authorization = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            authorization = request.Headers.Authorization?.ToString();
            return JsonResponse("""{"output_text":"{\"replyContent\":\"أهلاً بحضرتك\"}"}""");
        });
        var client = CreateClient(handler);

        var result = await client.GenerateReplyAsync(new OpenAiCustomerReplyRequest(
            "Customer message",
            "sk-secret",
            "gpt-5.6"));

        Assert.Equal("{\"replyContent\":\"أهلاً بحضرتك\"}", result);
        Assert.Equal("Bearer sk-secret", authorization);
        using var body = JsonDocument.Parse(requestJson!);
        Assert.Equal("gpt-5.6", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("low", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(
            "json_object",
            body.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task Image_reply_sends_the_image_inline_and_reads_nested_output_text()
    {
        string? requestJson = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""
                {
                  "output": [
                    {
                      "content": [
                        { "type": "output_text", "text": "{\"replyContent\":\"تم فحص الصورة\"}" }
                      ]
                    }
                  ]
                }
                """);
        });
        var client = CreateClient(handler);

        var result = await client.GenerateReplyAsync(new OpenAiCustomerReplyRequest(
            "Inspect attachment",
            "sk-secret",
            "gpt-5.6-luna",
            new CustomerReplyAttachment([1, 2, 3], "image/png")));

        Assert.Equal("{\"replyContent\":\"تم فحص الصورة\"}", result);
        using var body = JsonDocument.Parse(requestJson!);
        var content = body.RootElement.GetProperty("input")[0].GetProperty("content");
        Assert.Equal("input_text", content[0].GetProperty("type").GetString());
        Assert.Equal("input_image", content[1].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,AQID", content[1].GetProperty("image_url").GetString());
    }

    [Fact]
    public async Task Voice_note_is_transcribed_before_the_chat_model_generates_the_reply()
    {
        var requests = new List<(string Path, string Body)>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            requests.Add((request.RequestUri!.AbsolutePath, body));
            return request.RequestUri.AbsolutePath.EndsWith("/audio/transcriptions", StringComparison.Ordinal)
                ? JsonResponse("""{"text":"عايز أعرف السعر"}""")
                : JsonResponse("""{"output_text":"{\"replyContent\":\"السعر موضح لحضرتك\",\"transcription\":\"عايز أعرف السعر\"}"}""");
        });
        var client = CreateClient(handler);

        var result = await client.GenerateReplyAsync(new OpenAiCustomerReplyRequest(
            "Process voice note",
            "sk-secret",
            "gpt-5.6",
            new CustomerReplyAttachment([4, 5, 6], "audio/ogg")));

        Assert.Contains("عايز أعرف السعر", result);
        Assert.Equal(2, requests.Count);
        Assert.Equal("/v1/audio/transcriptions", requests[0].Path);
        Assert.Equal("/v1/responses", requests[1].Path);
        using var responseRequest = JsonDocument.Parse(requests[1].Body);
        Assert.Contains("عايز أعرف السعر", responseRequest.RootElement.GetProperty("input").GetString());
    }

    private static OpenAiResponsesClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") },
        NullLogger<OpenAiResponsesClient>.Instance);

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
}
