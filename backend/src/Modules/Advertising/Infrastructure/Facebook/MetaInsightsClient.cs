using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Modules.Advertising.Domain;

namespace Modules.Advertising.Infrastructure.Facebook;

public sealed record MetaInsightRow(string AdExternalId, DateTime StartUtc, DateTime EndUtc, decimal Spend, long Impressions,
    long Clicks, decimal Frequency, IReadOnlyDictionary<string, decimal> Actions, IReadOnlyDictionary<string, decimal> ActionValues);
public sealed record MetaAdState(string Id, string Status, string EffectiveStatus, decimal DailyBudget);

public static class MetaInsightRevisionPolicy
{
    public static string Fingerprint(MetaInsightRow row) => Canonical(row.Spend, row.Impressions, row.Clicks,
        row.Frequency, row.Actions, row.ActionValues);

    public static string Fingerprint(InsightsSnapshot snapshot) => Canonical(snapshot.Spend, snapshot.Impressions,
        snapshot.Clicks, snapshot.Frequency, Normalize(snapshot.ProviderActionsJson), Normalize(snapshot.ProviderActionValuesJson));

    private static IReadOnlyDictionary<string, decimal> Normalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return new Dictionary<string, decimal>();
        var nested = root.EnumerateObject().FirstOrDefault().Value;
        return nested.ValueKind == JsonValueKind.Object
            ? nested.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetDecimal(), StringComparer.Ordinal)
            : new Dictionary<string, decimal>();
    }

    private static string Canonical(decimal spend, long impressions, long clicks, decimal frequency,
        IReadOnlyDictionary<string, decimal> actions, IReadOnlyDictionary<string, decimal> values) => Hash(JsonSerializer.Serialize(new
        {
            spend, impressions, clicks, frequency,
            actions = actions.OrderBy(pair => pair.Key).Select(pair => new { pair.Key, pair.Value }),
            values = values.OrderBy(pair => pair.Key).Select(pair => new { pair.Key, pair.Value })
        }));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class MetaInsightsClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<MetaInsightRow>> GetAdInsightsAsync(string token, string adAccountId, DateOnly since, DateOnly until, CancellationToken cancellationToken)
    {
        var fields = "ad_id,date_start,date_stop,spend,impressions,clicks,frequency,actions,action_values";
        var path = $"{adAccountId}/insights?fields={fields}&level=ad&time_increment=1&time_range={{\"since\":\"{since:yyyy-MM-dd}\",\"until\":\"{until:yyyy-MM-dd}\"}}&limit=500";
        var rows = new List<MetaInsightRow>();
        while (!string.IsNullOrWhiteSpace(path))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccess(response, cancellationToken);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            foreach (var item in json.RootElement.GetProperty("data").EnumerateArray())
                rows.Add(new(Get(item, "ad_id"), ParseDate(item, "date_start"), ParseDate(item, "date_stop").AddDays(1), ParseDecimal(item, "spend"),
                    ParseLong(item, "impressions"), ParseLong(item, "clicks"), ParseDecimal(item, "frequency"), ParseBreakdown(item, "actions"), ParseBreakdown(item, "action_values")));
            path = json.RootElement.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var next) ? next.GetString() ?? string.Empty : string.Empty;
        }
        return rows;
    }

    public async Task<MetaAdState> GetAdStateAsync(string token, string adId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{adId}?fields=id,status,effective_status,adset{{id,daily_budget}},campaign{{id,daily_budget}}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        var budget = NestedBudget(root, "adset") ?? NestedBudget(root, "campaign") ?? 0m;
        return new(Get(root, "id"), Get(root, "status"), Get(root, "effective_status"), budget / 100m);
    }

    private static IReadOnlyDictionary<string, decimal> ParseBreakdown(JsonElement element, string property) =>
        element.TryGetProperty(property, out var values) ? values.EnumerateArray().Where(x => x.TryGetProperty("action_type", out _))
            .GroupBy(x => Get(x, "action_type")).ToDictionary(x => x.Key, x => x.Sum(ParseValue), StringComparer.OrdinalIgnoreCase) : new Dictionary<string, decimal>();
    private static decimal ParseValue(JsonElement item) => decimal.TryParse(Get(item, "value"), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    private static decimal ParseDecimal(JsonElement item, string property) => decimal.TryParse(Get(item, property), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    private static long ParseLong(JsonElement item, string property) => long.TryParse(Get(item, property), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static decimal? NestedBudget(JsonElement item, string property) => item.TryGetProperty(property, out var nested) && nested.ValueKind == JsonValueKind.Object && nested.TryGetProperty("daily_budget", out var budget)
        && decimal.TryParse(budget.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static DateTime ParseDate(JsonElement item, string property) => DateTime.SpecifyKind(DateTime.ParseExact(Get(item, property), "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc);
    private static string Get(JsonElement item, string property) => item.TryGetProperty(property, out var value) ? value.ToString() : string.Empty;
    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException("Meta Insights request failed.", null, response.StatusCode);
    }
}
