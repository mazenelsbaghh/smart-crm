using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;

namespace Modules.Advertising.Infrastructure.Facebook;

public sealed record MetaResource(string Id, string Name, string? Currency = null, string? Timezone = null, int? Status = null);
public sealed record MetaResourceCatalog(IReadOnlyList<MetaResource> AdAccounts, IReadOnlyList<MetaResource> Pages, IReadOnlyList<MetaResource> Datasets);
public sealed record MetaPagePost(string Id, string? Message, string MediaType, string? MediaUrl, DateTime? CreatedAtUtc);
public sealed record MetaExistingAd(string AdId, string AdName, string Status, string EffectiveStatus, string AdSetId,
    string AdSetName, string CampaignId, string CampaignName, string Objective, string? ObjectStoryId,
    decimal DailyBudget, string BudgetOwnerId, string BudgetOwnerType,
    IReadOnlyList<string> PublisherPlatforms, IReadOnlyList<string> FacebookPositions,
    IReadOnlyList<string> InstagramPositions, IReadOnlyList<string> MessengerPositions,
    IReadOnlyList<string> AudienceNetworkPositions, string? Destination)
{
    // Imported WhatsApp ads keep their existing Meta placements. This manager only changes delivery state or budget, never placements.
    public bool IsFacebookOnly => IsFacebookPlacementOnly || Destination == "WhatsApp";

    private bool IsFacebookPlacementOnly => PublisherPlatforms.Count == 1
        && PublisherPlatforms[0].Equals("facebook", StringComparison.OrdinalIgnoreCase)
        && FacebookPlacementPolicy.IsAllowed("facebook", FacebookPositions);
}
public sealed record MetaAdSetRequest(string AdAccountId, string CampaignId, string Name, decimal DailyBudget, string OptimizationGoal, IReadOnlyCollection<string> Countries, IReadOnlyCollection<string> Positions, string? DatasetId, string? CustomEventType);
public sealed record MetaExistingPostAdRequest(string AdAccountId, string AdSetId, string ObjectStoryId, string Name);
public sealed record MetaConversionRequest(string DatasetId, CanonicalConversion Conversion, IReadOnlyDictionary<string, string>? MatchData);

public sealed class MetaAdsClient(HttpClient httpClient, IOptions<AdvertisingOptions> options)
{
    private readonly MetaOptions _options = options.Value.Meta;

    public async Task<string> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        EnsureRealConfiguration();
        var uri = $"oauth/access_token?client_id={Uri.EscapeDataString(_options.AppId)}&client_secret={Uri.EscapeDataString(_options.AppSecret)}&redirect_uri={Uri.EscapeDataString(_options.OAuthRedirectUri)}&code={Uri.EscapeDataString(code)}";
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Meta did not return an access token.");
    }

    public async Task<MetaResourceCatalog> DiscoverAsync(string accessToken, string? adAccountId, CancellationToken cancellationToken)
    {
        var accounts = await GetList("me/adaccounts?fields=id,name,currency,timezone_name,account_status&limit=100", accessToken, cancellationToken,
            x => new MetaResource(x.GetProperty("id").GetString()!, x.GetProperty("name").GetString() ?? "Ad Account", GetString(x, "currency"), GetString(x, "timezone_name"), GetInt(x, "account_status")));
        var pages = await GetList("me/accounts?fields=id,name&limit=100", accessToken, cancellationToken,
            x => new MetaResource(x.GetProperty("id").GetString()!, x.GetProperty("name").GetString() ?? "Page"));
        var selected = adAccountId ?? accounts.FirstOrDefault()?.Id;
        var datasets = selected is null ? [] : await GetList($"{selected}/adspixels?fields=id,name&limit=100", accessToken, cancellationToken,
            x => new MetaResource(x.GetProperty("id").GetString()!, x.GetProperty("name").GetString() ?? "Dataset"));
        return new(accounts, pages, datasets);
    }

    public async Task<string> CreateCampaignPausedAsync(string accessToken, string adAccountId, string name, string objective, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["name"] = name, ["objective"] = objective, ["status"] = "PAUSED", ["special_ad_categories"] = "[]", ["access_token"] = accessToken
        };
        using var response = await httpClient.PostAsync($"{adAccountId}/campaigns", new FormUrlEncodedContent(fields), cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("id").GetString()!;
    }

    public async Task<string> CreateAdSetPausedAsync(string accessToken, MetaAdSetRequest request, CancellationToken cancellationToken)
    {
        if (!FacebookPlacementPolicy.IsAllowed("facebook", request.Positions)) throw new InvalidOperationException("Forbidden Meta placement.");
        var targeting = JsonSerializer.Serialize(new { geo_locations = new { countries = request.Countries }, publisher_platforms = new[] { "facebook" }, facebook_positions = request.Positions });
        var fields = new Dictionary<string, string>
        {
            ["campaign_id"] = request.CampaignId, ["name"] = request.Name, ["daily_budget"] = ToMinorUnits(request.DailyBudget).ToString(),
            ["billing_event"] = "IMPRESSIONS", ["optimization_goal"] = request.OptimizationGoal, ["bid_strategy"] = "LOWEST_COST_WITHOUT_CAP",
            ["targeting"] = targeting, ["status"] = "PAUSED"
        };
        if (request.DatasetId is not null && request.CustomEventType is not null)
            fields["promoted_object"] = JsonSerializer.Serialize(new { pixel_id = request.DatasetId, custom_event_type = request.CustomEventType });
        return await PostForId(request.AdAccountId + "/adsets", accessToken, fields, cancellationToken);
    }

    public async Task<(string CreativeId, string AdId)> CreateExistingPostAdPausedAsync(string accessToken, MetaExistingPostAdRequest request, CancellationToken cancellationToken)
    {
        var creativeId = await PostForId(request.AdAccountId + "/adcreatives", accessToken, new Dictionary<string, string> { ["name"] = request.Name, ["object_story_id"] = request.ObjectStoryId }, cancellationToken);
        var adId = await PostForId(request.AdAccountId + "/ads", accessToken, new Dictionary<string, string> { ["name"] = request.Name, ["adset_id"] = request.AdSetId, ["creative"] = JsonSerializer.Serialize(new { creative_id = creativeId }), ["status"] = "PAUSED" }, cancellationToken);
        return (creativeId, adId);
    }

    public async Task SetAdStatusAsync(string accessToken, string adId, string status, CancellationToken cancellationToken)
    {
        if (status is not ("ACTIVE" or "PAUSED")) throw new InvalidOperationException("Unsupported ad status.");
        var fields = new Dictionary<string, string> { ["status"] = status, ["access_token"] = accessToken };
        using var response = await httpClient.PostAsync(adId, new FormUrlEncodedContent(fields), cancellationToken);
        await EnsureSuccess(response, cancellationToken);
    }

    public async Task SetDailyBudgetAsync(string accessToken, string budgetOwnerId, decimal dailyBudget, CancellationToken cancellationToken)
    {
        if (dailyBudget <= 0) throw new ArgumentOutOfRangeException(nameof(dailyBudget));
        var fields = new Dictionary<string, string> { ["daily_budget"] = ToMinorUnits(dailyBudget).ToString(), ["access_token"] = accessToken };
        using var response = await httpClient.PostAsync(budgetOwnerId, new FormUrlEncodedContent(fields), cancellationToken);
        await EnsureSuccess(response, cancellationToken);
    }

    public async Task SendConversionAsync(string accessToken, MetaConversionRequest request, CancellationToken cancellationToken)
    {
        var userData = new Dictionary<string, object?>();
        if (request.MatchData is not null)
        {
            if (request.MatchData.TryGetValue("Email", out var email)) userData["em"] = new[] { Sha256(email.Trim().ToLowerInvariant()) };
            if (request.MatchData.TryGetValue("Phone", out var phone)) userData["ph"] = new[] { Sha256(new string(phone.Where(char.IsDigit).ToArray())) };
        }
        var conversion = request.Conversion;
        var eventPayload = new
        {
            event_name = MetaEventName(conversion.EventType), event_time = new DateTimeOffset(conversion.OccurredAtUtc).ToUnixTimeSeconds(),
            event_id = conversion.CanonicalKey, action_source = "system_generated", user_data = userData,
            custom_data = new { value = conversion.CurrentValue, currency = conversion.Currency }
        };
        var fields = new Dictionary<string, string> { ["data"] = JsonSerializer.Serialize(new[] { eventPayload }), ["access_token"] = accessToken };
        using var response = await httpClient.PostAsync(request.DatasetId + "/events", new FormUrlEncodedContent(fields), cancellationToken);
        await EnsureSuccess(response, cancellationToken);
    }

    public async Task<IReadOnlyList<MetaPagePost>> GetPagePostsAsync(string accessToken, string pageId, CancellationToken cancellationToken)
    {
        return await GetList($"{pageId}/feed?fields=id,message,created_time,attachments{{media_type,media,url}}&limit=100", accessToken, cancellationToken, ParsePagePost);
    }

    private static MetaPagePost ParsePagePost(JsonElement x)
    {
        var type = "Image"; string? url = null;
        if (x.TryGetProperty("attachments", out var attachments) && attachments.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
        {
            var attachment = data[0]; type = GetString(attachment, "media_type")?.Contains("video", StringComparison.OrdinalIgnoreCase) == true ? "Video" : "Image";
            url = GetString(attachment, "url");
        }
        return new MetaPagePost(x.GetProperty("id").GetString()!, GetString(x, "message"), type, url,
            DateTime.TryParse(GetString(x, "created_time"), out var created) ? created.ToUniversalTime() : null);
    }

    public async Task<IReadOnlyList<MetaExistingAd>> GetExistingAdsAsync(string accessToken, string adAccountId, CancellationToken cancellationToken)
    {
        const string fields = "id,name,status,effective_status,adset{id,name,daily_budget,targeting,promoted_object,campaign{id,name,objective,daily_budget}},creative{id,object_story_id}";
        return await GetList($"{adAccountId}/ads?fields={fields}&limit=200", accessToken, cancellationToken, ParseExistingAd);
    }

    private static MetaExistingAd ParseExistingAd(JsonElement ad)
    {
        var adSet = ad.GetProperty("adset");
        var campaign = adSet.GetProperty("campaign");
        var targeting = adSet.TryGetProperty("targeting", out var targetingElement) ? targetingElement : default;
        var publishers = GetStringList(targeting, "publisher_platforms");
        var facebookPositions = GetStringList(targeting, "facebook_positions");
        var instagramPositions = GetStringList(targeting, "instagram_positions");
        var messengerPositions = GetStringList(targeting, "messenger_positions");
        var audienceNetworkPositions = GetStringList(targeting, "audience_network_positions");
        var adSetBudget = GetMoney(adSet, "daily_budget");
        var campaignBudget = GetMoney(campaign, "daily_budget");
        string? storyId = null;
        if (ad.TryGetProperty("creative", out var creative)) storyId = GetString(creative, "object_story_id") ?? GetString(creative, "id");
        return new(
            ad.GetProperty("id").GetString()!, GetString(ad, "name") ?? "Facebook Ad", GetString(ad, "status") ?? "PAUSED",
            GetString(ad, "effective_status") ?? "UNKNOWN", adSet.GetProperty("id").GetString()!, GetString(adSet, "name") ?? "Facebook Ad Set",
            campaign.GetProperty("id").GetString()!, GetString(campaign, "name") ?? "Facebook Campaign", GetString(campaign, "objective") ?? "UNKNOWN",
            storyId, adSetBudget > 0 ? adSetBudget : campaignBudget,
            adSetBudget > 0 ? adSet.GetProperty("id").GetString()! : campaign.GetProperty("id").GetString()!,
            adSetBudget > 0 ? "AdSet" : "Campaign", publishers, facebookPositions, instagramPositions, messengerPositions,
            audienceNetworkPositions, GetDestination(adSet));
    }

    private static IReadOnlyList<string> GetStringList(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(entry => entry.GetString()).Where(entry => !string.IsNullOrWhiteSpace(entry)).Select(entry => entry!).ToArray()
            : [];

    private static string? GetDestination(JsonElement adSet)
    {
        if (!adSet.TryGetProperty("promoted_object", out var promotedObject) || promotedObject.ValueKind != JsonValueKind.Object) return null;
        if (promotedObject.TryGetProperty("whatsapp_phone_number", out var whatsapp) && !string.IsNullOrWhiteSpace(whatsapp.ToString())) return "WhatsApp";
        if (promotedObject.TryGetProperty("page_id", out var page) && !string.IsNullOrWhiteSpace(page.ToString())) return "Facebook Page";
        return null;
    }

    private static decimal GetMoney(JsonElement element, string name) =>
        decimal.TryParse(GetString(element, name), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var minorUnits)
            ? minorUnits / 100m
            : 0m;

    private async Task<List<T>> GetList<T>(string path, string token, CancellationToken cancellationToken, Func<JsonElement, T> map)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("data").EnumerateArray().Select(map).ToList();
    }

    private async Task<string> PostForId(string path, string accessToken, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        fields["access_token"] = accessToken;
        using var response = await httpClient.PostAsync(path, new FormUrlEncodedContent(fields), cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("Meta did not return an ID.");
    }

    private static long ToMinorUnits(decimal amount) => checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    private static string Sha256(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string MetaEventName(string eventType) => eventType switch
    {
        "Purchase" or "SubscriptionStarted" or "SubscriptionRenewed" or "EnrollmentPaid" => "Purchase",
        "Signup" => "CompleteRegistration", "Lead" or "QualifiedLead" => "Lead", "TrialStarted" => "StartTrial",
        "BookingConfirmed" => "Schedule", _ => eventType
    };

    private void EnsureRealConfiguration()
    {
        if (_options.UseMock) throw new InvalidOperationException("Code exchange is unavailable in mock mode.");
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppSecret) || string.IsNullOrWhiteSpace(_options.OAuthRedirectUri))
            throw new AdvertisingException("ADS_META_OAUTH_NOT_CONFIGURED", "Meta OAuth is not configured.", 503);
    }

    private static string? GetString(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ToString() : null;
    private static int? GetInt(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Meta API request failed with {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}", null, response.StatusCode);
    }
}
