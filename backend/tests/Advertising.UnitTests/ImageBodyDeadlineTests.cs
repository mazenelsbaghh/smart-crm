using System.Net;
using Modules.Content.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ImageBodyDeadlineTests
{
    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    public async Task Provider_that_sends_headers_but_stalls_the_image_body_is_cancelled(string provider)
    {
        using var http = new HttpClient(new StalledResponse())
            { BaseAddress = new Uri("https://images.invalid/"), Timeout = TimeSpan.FromMilliseconds(100) };
        var generating = provider == "OpenAI"
            ? new OpenAiImageClient(http).GenerateAsync(new("image", "test-key", []), CancellationToken.None)
            : new GeminiImageClient(http).GenerateAsync(new("image", "test-key", null, GeminiImageClient.SquareAspectRatio), CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generating.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    private sealed class StalledResponse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StalledBody() });
    }

    private sealed class StalledBody : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            throw new NotSupportedException();
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context,
            CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
