using Modules.Advertising.Infrastructure.Facebook;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MetaGraphClientSecurityTests
{
    [Fact]
    public async Task Request_outside_the_configured_graph_origin_is_rejected_before_sending_the_token()
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.facebook.com/v26.0/")
        };
        var client = new MetaGraphClient(httpClient);

        var error = await Assert.ThrowsAsync<MetaGraphException>(() => client.GetAsync(
            "https://attacker.example/collect",
            "sensitive-access-token"));

        Assert.Equal("ADS_META_REQUEST_URI_INVALID", error.Code);
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestCount);
            throw new InvalidOperationException($"Unexpected outbound request: {request.RequestUri}");
        }
    }
}
