using System.Net;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Modules.Advertising.Infrastructure.Facebook;

/// <summary>Development/test transport. It is never registered outside Development.</summary>
public sealed class FakeMetaAdsHandler : HttpMessageHandler
{
    private static readonly ConcurrentDictionary<string, byte> CreatedAds = new(StringComparer.Ordinal);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath.Trim('/') ?? string.Empty;
        var payload = request.Method == HttpMethod.Get ? GetPayload(path) : PostPayload(path);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            RequestMessage = request
        });
    }

    private static string GetPayload(string path)
    {
        if (path.EndsWith("me/adaccounts", StringComparison.OrdinalIgnoreCase))
            return "{\"data\":[{\"id\":\"act_mock_1\",\"name\":\"Mock Ad Account\",\"currency\":\"EGP\",\"timezone_name\":\"Africa/Cairo\",\"account_status\":1}]}";
        if (path.EndsWith("me/accounts", StringComparison.OrdinalIgnoreCase))
            return "{\"data\":[{\"id\":\"page_mock_1\",\"name\":\"Mock Facebook Page\"}]}";
        if (path.Contains("adspixels", StringComparison.OrdinalIgnoreCase))
            return "{\"data\":[{\"id\":\"dataset_mock_1\",\"name\":\"Mock Dataset\"}]}";
        if (path.Contains("/posts", StringComparison.OrdinalIgnoreCase))
            return "{\"data\":[{\"id\":\"page_mock_1_101\",\"message\":\"فيديو تعريفي بالخدمة\",\"created_time\":\"2026-08-15T09:00:00Z\",\"attachments\":{\"data\":[{\"media_type\":\"video\",\"url\":\"https://example.invalid/video\"}]}},{\"id\":\"page_mock_1_102\",\"message\":\"صورة العرض الحالية\",\"created_time\":\"2026-08-16T09:00:00Z\",\"attachments\":{\"data\":[{\"media_type\":\"photo\",\"url\":\"https://example.invalid/image\"}]}}]}";
        if (path.EndsWith("/ads", StringComparison.OrdinalIgnoreCase))
            return "{\"data\":[{\"id\":\"mock_existing_ad_1\",\"name\":\"حملة موجودة - فيديو\",\"status\":\"ACTIVE\",\"effective_status\":\"ACTIVE\",\"adset\":{\"id\":\"mock_existing_adset_1\",\"name\":\"جمهور مصر\",\"daily_budget\":\"10000\",\"targeting\":{\"publisher_platforms\":[\"facebook\"],\"facebook_positions\":[\"feed\",\"facebook_reels\"]},\"campaign\":{\"id\":\"mock_existing_campaign_1\",\"name\":\"الحملة الحالية\",\"objective\":\"OUTCOME_SALES\",\"daily_budget\":\"0\"}},\"creative\":{\"id\":\"mock_existing_creative_1\",\"object_story_id\":\"page_mock_1_101\"}}]}";
        if (path.Contains("/insights", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { data = CreatedAds.Keys.Append("mock_existing_ad_1").Distinct().Select(id => new { ad_id = id, date_start = "2026-08-17", date_stop = "2026-08-17", spend = "42.50", impressions = "4200", clicks = "110", frequency = "1.8", actions = new[] { new { action_type = "purchase", value = "2" } }, action_values = new[] { new { action_type = "purchase", value = "180.00" } } }) });
        if (path.Contains("mock_existing_ad_", StringComparison.OrdinalIgnoreCase))
            return "{\"id\":\"mock_existing_ad_1\",\"status\":\"ACTIVE\",\"effective_status\":\"ACTIVE\",\"daily_budget\":\"10000\"}";
        if (path.Contains("mock_ad_", StringComparison.OrdinalIgnoreCase))
        {
            var id = path.Split('/').Last(segment => segment.StartsWith("mock_ad_", StringComparison.OrdinalIgnoreCase));
            return JsonSerializer.Serialize(new { id, status = "ACTIVE", effective_status = "ACTIVE", daily_budget = "10000" });
        }
        return "{\"data\":[]}";
    }

    private static string PostPayload(string path)
    {
        if (path.EndsWith("/events", StringComparison.OrdinalIgnoreCase)) return "{\"events_received\":1}";
        var resource = path.EndsWith("/ads", StringComparison.OrdinalIgnoreCase) ? "ad" : path.EndsWith("/adsets", StringComparison.OrdinalIgnoreCase) ? "adset" : "campaign";
        var id = $"mock_{resource}_{Guid.NewGuid():N}";
        if (resource == "ad") CreatedAds.TryAdd(id, 0);
        return JsonSerializer.Serialize(new { id });
    }
}
