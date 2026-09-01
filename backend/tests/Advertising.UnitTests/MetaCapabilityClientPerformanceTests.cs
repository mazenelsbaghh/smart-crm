using System.Net;
using System.Text;
using Modules.Advertising.Infrastructure.Facebook;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MetaCapabilityClientPerformanceTests
{
    private static readonly Uri GraphBaseAddress = new("https://graph.facebook.com/v26.0/");

    [Fact]
    public async Task Discovery_starts_accounts_pages_and_permissions_concurrently()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var handler = new ConcurrentRequestBarrierHandler(
            participantCount: 3,
            isParticipant: request => Path(request) is
                "/v26.0/me/adaccounts" or
                "/v26.0/me/accounts" or
                "/v26.0/me/permissions",
            response: DiscoveryResponse);
        using var httpClient = new HttpClient(handler) { BaseAddress = GraphBaseAddress };
        var client = new MetaCapabilityClient(new MetaGraphClient(httpClient));

        var catalog = await client.DiscoverAsync("access-token", null, timeout.Token);

        Assert.Equal(3, handler.MaximumConcurrency);
        Assert.Equal("act_1", Assert.Single(catalog.AdAccounts).Id);
        Assert.Equal("page_1", Assert.Single(catalog.Pages).Id);
        Assert.Empty(catalog.Datasets);
        Assert.Empty(catalog.Wabas);
        Assert.Equal(["ads_management"], catalog.GrantedPermissions);
    }

    [Fact]
    public async Task Runtime_probe_starts_account_page_and_phone_requests_concurrently()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var handler = new ConcurrentRequestBarrierHandler(
            participantCount: 3,
            isParticipant: request => Path(request) is
                "/v26.0/act_1" or
                "/v26.0/page_1" or
                "/v26.0/phone_1",
            response: ProbeResponse);
        using var httpClient = new HttpClient(handler) { BaseAddress = GraphBaseAddress };
        var client = new MetaCapabilityClient(new MetaGraphClient(httpClient));

        var probe = await client.ProbeAsync("access-token", "act_1", "page_1", "phone_1", timeout.Token);

        Assert.Equal(3, handler.MaximumConcurrency);
        Assert.True(probe.Supported);
        Assert.Empty(probe.FailureCode);
    }

    [Fact]
    public async Task Pagination_cycle_stops_before_a_third_request_and_reports_invalid_pagination()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var handler = new PaginationCycleHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = GraphBaseAddress };
        var client = new MetaGraphClient(httpClient);

        var error = await Assert.ThrowsAsync<MetaGraphException>(() => client.GetAllAsync(
            "https://graph.facebook.com/v26.0/items?limit=1",
            "access-token",
            timeout.Token));

        Assert.Equal("ADS_META_PAGINATION_INVALID", error.Code);
        Assert.Equal(2, handler.RequestCount);
    }

    private static HttpResponseMessage DiscoveryResponse(HttpRequestMessage request) => Path(request) switch
    {
        "/v26.0/me/adaccounts" => JsonResponse("""
            {"data":[{"id":"act_1","name":"Primary account","currency":"EGP","timezone_name":"Africa/Cairo","account_status":1}]}
            """),
        "/v26.0/me/accounts" => JsonResponse("""
            {"data":[{"id":"page_1","name":"Primary page"}]}
            """),
        "/v26.0/me/permissions" => JsonResponse("""
            {"data":[{"permission":"ads_management","status":"granted"}]}
            """),
        "/v26.0/act_1/adspixels" => JsonResponse("""
            {"data":[]}
            """),
        _ => throw new InvalidOperationException($"Unexpected discovery request: {request.RequestUri}")
    };

    private static HttpResponseMessage ProbeResponse(HttpRequestMessage request) => Path(request) switch
    {
        "/v26.0/act_1" => JsonResponse("""
            {"id":"act_1","account_status":1,"currency":"EGP","timezone_name":"Africa/Cairo"}
            """),
        "/v26.0/page_1" => JsonResponse("""
            {"id":"page_1","name":"Primary page"}
            """),
        "/v26.0/phone_1" => JsonResponse("""
            {"id":"phone_1","quality_rating":"GREEN"}
            """),
        _ => throw new InvalidOperationException($"Unexpected probe request: {request.RequestUri}")
    };

    private static string Path(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath ?? throw new InvalidOperationException("The request URI must be absolute.");

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class ConcurrentRequestBarrierHandler(
        int participantCount,
        Func<HttpRequestMessage, bool> isParticipant,
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _allParticipantsStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeParticipants;
        private int _arrivedParticipants;
        private int _maximumConcurrency;

        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!isParticipant(request))
                return response(request);

            var active = Interlocked.Increment(ref _activeParticipants);
            RecordMaximum(active);
            if (Interlocked.Increment(ref _arrivedParticipants) == participantCount)
                _allParticipantsStarted.TrySetResult(true);

            try
            {
                await _allParticipantsStarted.Task.WaitAsync(cancellationToken);
                return response(request);
            }
            finally
            {
                Interlocked.Decrement(ref _activeParticipants);
            }
        }

        private void RecordMaximum(int candidate)
        {
            var observed = Volatile.Read(ref _maximumConcurrency);
            while (candidate > observed)
            {
                var original = Interlocked.CompareExchange(ref _maximumConcurrency, candidate, observed);
                if (original == observed)
                    return;
                observed = original;
            }
        }
    }

    private sealed class PaginationCycleHandler : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestNumber = Interlocked.Increment(ref _requestCount);
            var response = requestNumber switch
            {
                1 => JsonResponse("""
                    {"data":[{"id":"first"}],"paging":{"next":"https://graph.facebook.com/v26.0/items?after=cursor-1"}}
                    """),
                2 => JsonResponse("""
                    {"data":[{"id":"second"}],"paging":{"next":"https://graph.facebook.com/v26.0/items?limit=1"}}
                    """),
                _ => throw new InvalidOperationException($"Pagination made an unexpected request: {request.RequestUri}")
            };
            return Task.FromResult(response);
        }
    }
}
