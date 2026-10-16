using System.Net;
using System.Text;
using System.Text.Json;
using Modules.Content.Domain;
using Modules.Content.Services;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class OpenAiImageClientTests
{
    [Theory]
    [InlineData(false, GeminiImageClient.SquareAspectRatio, "2880x2880", OpenAiImageClient.HighestQualityModel, null)]
    [InlineData(true, GeminiImageClient.SquareAspectRatio, "2880x2880", OpenAiImageClient.HighestQualityModel, null)]
    [InlineData(true, GeminiImageClient.PortraitAspectRatio, "2448x3264", OpenAiImageClient.FastModel, null)]
    [InlineData(false, GeminiImageClient.SquareAspectRatio, "1024x1024", OpenAiImageClient.FastModel, GeminiImageClient.PreviewImageSize)]
    [InlineData(true, GeminiImageClient.PortraitAspectRatio, "768x1024", OpenAiImageClient.HighestQualityModel, GeminiImageClient.PreviewImageSize)]
    public async Task Highest_quality_image_preserves_reference_inputs_and_returns_generated_bytes(bool useReferences, string aspectRatio, string dimensions, string model, string? previewSize)
    {
        using var handler = new ImageResponseHandler(HttpStatusCode.OK, "{\"data\":[{\"b64_json\":\"AQID\"}]}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.invalid/") };
        var references = useReferences ? new[] { new GeminiReferenceImage([9, 8, 7], "image/png") } : [];
        var client = new OpenAiImageClient(http);

        var image = await client.GenerateAsync(new("Preserve the supplied logo", "openai-test-key", references, aspectRatio, model, previewSize), CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, image.Bytes);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(model, image.Model);
        Assert.Equal(dimensions, image.Size);
        Assert.Equal(useReferences ? "/v1/images/edits" : "/v1/images/generations", handler.Path);
        Assert.Equal("Bearer openai-test-key", handler.Authorization);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal(image.Model, payload.RootElement.GetProperty("model").GetString());
        Assert.Equal(previewSize is null ? "max" : "high", payload.RootElement.GetProperty("quality").GetString());
        Assert.Equal(image.Size, payload.RootElement.GetProperty("size").GetString());
        if (useReferences)
            Assert.Equal("data:image/png;base64,CQgH", payload.RootElement.GetProperty("images")[0].GetProperty("image_url").GetString());
        else Assert.False(payload.RootElement.TryGetProperty("images", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Openai_failure_is_reported_without_disclosing_provider_content_or_falling_back(HttpStatusCode status)
    {
        using var handler = new ImageResponseHandler(status, "{\"error\":{\"message\":\"private-provider-marker\"}}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.invalid/") };
        var vault = new ProjectSecretVault(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        var client = new ContentImageClient(new GeminiImageClient(http), new OpenAiImageClient(http), vault);
        var settings = new ContentAutomationSettings { ProjectId = Guid.NewGuid(), ImageProvider = "OpenAI" };
        settings.OpenAiImageApiKey = vault.Protect(settings.ProjectId, "openai-test-key");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAsync(settings,
            new("A new image", "gemini-test-key", null, GeminiImageClient.SquareAspectRatio), CancellationToken.None));

        Assert.Contains("OpenAI", exception.Message);
        Assert.DoesNotContain("private-provider-marker", exception.Message);
        Assert.Equal("Bearer openai-test-key", handler.Authorization);
    }

    [Fact]
    public async Task Empty_success_response_is_a_generation_failure()
    {
        using var handler = new ImageResponseHandler(HttpStatusCode.OK, "{\"data\":[]}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.invalid/") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAiImageClient(http)
            .GenerateAsync(new("An image", "test-key", []), CancellationToken.None));
    }

    [Fact]
    public async Task Gemini_selection_keeps_the_existing_image_contract()
    {
        using var handler = new ImageResponseHandler(HttpStatusCode.OK,
            "{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"AQID\"}}]}}]}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://gemini.invalid/") };
        var client = new ContentImageClient(new GeminiImageClient(http), new OpenAiImageClient(http),
            new ProjectSecretVault(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));

        var image = await client.GenerateAsync(new ContentAutomationSettings(),
            new("An image", "gemini-test-key", null, GeminiImageClient.SquareAspectRatio), CancellationToken.None);

        Assert.Equal("gemini-3-pro-image", image.Model);
        Assert.Equal("4K", image.Size);
        Assert.Null(handler.Authorization);
    }

    private sealed class ImageResponseHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }

}
