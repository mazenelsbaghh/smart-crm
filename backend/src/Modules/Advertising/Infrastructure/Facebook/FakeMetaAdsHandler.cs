using System.Net;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Modules.Advertising.Infrastructure.Facebook;

/// <summary>Development/test transport. It is never registered outside Development.</summary>
public sealed class FakeMetaAdsHandler : HttpMessageHandler
{
    public enum FailureScenario { None, RejectAdSet, TimeoutAfterCampaign, DriftAdSet }

    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _createdObjects = new(StringComparer.Ordinal);
    private int _campaignCreates;
    public FailureScenario Scenario { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath.Trim('/') ?? string.Empty;
        if (request.Method == HttpMethod.Get)
            return Response(HttpStatusCode.OK, GetPayload(path, MockSuffix(request)));

        var fields = request.Content is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ParseForm(await request.Content.ReadAsStringAsync(cancellationToken));
        if (Scenario == FailureScenario.TimeoutAfterCampaign && Volatile.Read(ref _campaignCreates) > 0 && path.EndsWith("/adsets", StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException("Simulated ambiguous provider timeout.", null, HttpStatusCode.GatewayTimeout);
        if (Scenario == FailureScenario.RejectAdSet && path.EndsWith("/adsets", StringComparison.OrdinalIgnoreCase))
            return Response(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Unsupported optimization\",\"code\":100}}");
        return Response(HttpStatusCode.OK, PostPayload(path, fields));
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string payload) => new(status)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

    private string GetPayload(string path, string suffix)
    {
        var accountId = $"act_mock_{suffix}";
        var pageId = $"page_mock_{suffix}";
        var businessId = $"business_mock_{suffix}";
        var wabaId = $"waba_mock_{suffix}";
        var phoneId = $"phone_mock_{suffix}";
        var datasetId = $"dataset_mock_{suffix}";
        if (path.EndsWith("me/adaccounts", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { data = new[] { new { id = accountId, name = "Mock Ad Account", currency = "EGP", timezone_name = "Africa/Cairo", account_status = 1 } } });
        if (path.EndsWith("me/accounts", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { data = new[] { new { id = pageId, name = "Mock Facebook Page" } } });
        if (path.EndsWith("me/permissions", StringComparison.OrdinalIgnoreCase))
            return "{\"data\":[{\"permission\":\"ads_read\",\"status\":\"granted\"},{\"permission\":\"ads_management\",\"status\":\"granted\"},{\"permission\":\"business_management\",\"status\":\"granted\"},{\"permission\":\"pages_show_list\",\"status\":\"granted\"},{\"permission\":\"whatsapp_business_management\",\"status\":\"granted\"},{\"permission\":\"whatsapp_business_manage_events\",\"status\":\"granted\"}]}";
        if (path.EndsWith("me/businesses", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { data = new[] { new { id = businessId, name = "Mock Business" } } });
        if (path.Contains("owned_whatsapp_business_accounts", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { data = new[] { new { id = wabaId, name = "Mock WABA" } } });
        if (path.Contains("phone_numbers", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { data = new[] { new { id = phoneId, display_phone_number = $"+20{suffix[^Math.Min(10, suffix.Length)..]}", verified_name = "Mock WhatsApp", quality_rating = "GREEN" } } });
        if (path.Contains("adspixels", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { data = new[] { new { id = datasetId, name = "Mock Dataset" } } });
        if (path.Contains("/feed", StringComparison.OrdinalIgnoreCase))
            return $"{{\"data\":[{{\"id\":\"{pageId}_102\",\"message\":\"صورة العرض الحالية\",\"created_time\":\"2026-08-16T09:00:00Z\",\"attachments\":{{\"data\":[{{\"media_type\":\"photo\",\"url\":\"https://example.invalid/image\"}}]}}}}]}}";
        if (path.Contains("/videos", StringComparison.OrdinalIgnoreCase))
            return $"{{\"data\":[{{\"id\":\"video_mock_{suffix}_101\",\"post_id\":\"{pageId}_101\",\"description\":\"فيديو تعريفي بالخدمة\",\"created_time\":\"2026-08-15T09:00:00Z\",\"permalink_url\":\"https://example.invalid/video\"}}]}}";
        if (path.EndsWith("/ads", StringComparison.OrdinalIgnoreCase))
            return "{\"data\":[{\"id\":\"mock_existing_ad_1\",\"name\":\"حملة موجودة - فيديو\",\"status\":\"ACTIVE\",\"effective_status\":\"ACTIVE\",\"adset\":{\"id\":\"mock_existing_adset_1\",\"name\":\"جمهور مصر\",\"daily_budget\":\"10000\",\"optimization_goal\":\"CONVERSATIONS\",\"targeting\":{\"publisher_platforms\":[\"facebook\"],\"facebook_positions\":[\"feed\",\"facebook_reels\"]},\"promoted_object\":{\"page_id\":\"page_mock_1\",\"whatsapp_phone_number\":\"201000000000\"},\"campaign\":{\"id\":\"mock_existing_campaign_1\",\"name\":\"الحملة الحالية\",\"objective\":\"OUTCOME_ENGAGEMENT\",\"daily_budget\":\"0\"}},\"creative\":{\"id\":\"mock_existing_creative_1\",\"object_story_id\":\"page_mock_1_101\"}}]}";
        if (path.Contains("/insights", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { data = _createdObjects.Keys.Where(id => id.StartsWith("mock_ad_", StringComparison.Ordinal)).Append("mock_existing_ad_1").Distinct().Select(id => new { ad_id = id, date_start = "2026-08-17", date_stop = "2026-08-17", spend = "42.50", impressions = "4200", clicks = "110", frequency = "1.8", actions = new[] { new { action_type = "purchase", value = "2" } }, action_values = new[] { new { action_type = "purchase", value = "180.00" } } }) });
        if (path.EndsWith(accountId, StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { id = accountId, account_status = 1, currency = "EGP", timezone_name = "Africa/Cairo" });
        if (path.EndsWith(pageId, StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { id = pageId, name = "Mock Facebook Page" });
        if (path.EndsWith(phoneId, StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { id = phoneId, quality_rating = "GREEN" });
        if (path.Contains("mock_existing_ad_", StringComparison.OrdinalIgnoreCase))
            return "{\"id\":\"mock_existing_ad_1\",\"status\":\"ACTIVE\",\"effective_status\":\"ACTIVE\",\"daily_budget\":\"10000\"}";
        var objectId = path.Split('/').LastOrDefault(segment => segment.StartsWith("mock_", StringComparison.OrdinalIgnoreCase));
        if (objectId is not null && _createdObjects.TryGetValue(objectId, out var created))
        {
            var effective = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = objectId,
                ["status"] = created.GetValueOrDefault("status", "PAUSED"),
                ["effective_status"] = created.GetValueOrDefault("status", "PAUSED")
            };
            foreach (var (key, value) in created.Where(pair => pair.Key is not "access_token" and not "validate_only"))
                effective[key] = TryJson(value, out var json) ? json : value;
            if (Scenario == FailureScenario.DriftAdSet && objectId.StartsWith("mock_adset_", StringComparison.Ordinal))
                effective["destination_type"] = "WEBSITE";
            return JsonSerializer.Serialize(effective);
        }
        return "{\"data\":[]}";
    }

    private static string MockSuffix(HttpRequestMessage request)
    {
        var token = request.Headers.Authorization?.Parameter;
        if (string.IsNullOrWhiteSpace(token) && request.RequestUri is { } requestUri)
            token = requestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2 && part[0] == "access_token")
            .Select(part => Uri.UnescapeDataString(part[1]))
            .FirstOrDefault();
        return token?.StartsWith("mock:", StringComparison.Ordinal) == true ? token[5..] : "1";
    }

    private string PostPayload(string path, Dictionary<string, string> fields)
    {
        if (path.EndsWith("/events", StringComparison.OrdinalIgnoreCase)) return "{\"events_received\":1}";
        var existingId = path.Split('/').LastOrDefault(segment => segment.StartsWith("mock_", StringComparison.OrdinalIgnoreCase));
        if (existingId is not null && _createdObjects.TryGetValue(existingId, out var existing))
        {
            foreach (var field in fields.Where(field => field.Key != "access_token")) existing[field.Key] = field.Value;
            return "{\"success\":true}";
        }
        if (fields.GetValueOrDefault("validate_only") == "true") return "{\"success\":true,\"validation_only\":true}";
        var resource = path.EndsWith("/ads", StringComparison.OrdinalIgnoreCase) ? "ad"
            : path.EndsWith("/adsets", StringComparison.OrdinalIgnoreCase) ? "adset"
            : path.EndsWith("/adcreatives", StringComparison.OrdinalIgnoreCase) ? "creative"
            : "campaign";
        var id = $"mock_{resource}_{Guid.NewGuid():N}";
        _createdObjects[id] = fields.Where(field => field.Key != "access_token").ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);
        if (resource == "campaign") Interlocked.Increment(ref _campaignCreates);
        return JsonSerializer.Serialize(new { id });
    }

    private static Dictionary<string, string> ParseForm(string form) => form.Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(part => Uri.UnescapeDataString(part[0].Replace('+', ' ')), part => Uri.UnescapeDataString((part.Length > 1 ? part[1] : string.Empty).Replace('+', ' ')), StringComparer.Ordinal);

    private static bool TryJson(string value, out JsonElement json)
    {
        try { json = JsonSerializer.Deserialize<JsonElement>(value); return json.ValueKind is JsonValueKind.Object or JsonValueKind.Array; }
        catch (JsonException) { json = default; return false; }
    }
}
