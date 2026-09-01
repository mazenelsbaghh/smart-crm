using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace Modules.TalkTips.Services;

public sealed class TalkTipsTrialStatusClient
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TalkTipsTrialStatusClient> _logger;

    public TalkTipsTrialStatusClient(
        HttpClient httpClient,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<TalkTipsTrialStatusClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> HasTriedAsync(string? phone, CancellationToken cancellationToken = default)
    {
        var normalizedPhone = NormalizeEgyptianPhone(phone);
        if (normalizedPhone is null)
        {
            return false;
        }

        var cacheKey = $"talktips:trial-status:{normalizedPhone}";
        if (_cache.TryGetValue(cacheKey, out bool hasTried))
        {
            return hasTried;
        }

        var integrationKey = _configuration["TALKTIPS_TRIAL_STATUS_API_KEY"];
        if (string.IsNullOrWhiteSpace(integrationKey))
        {
            _logger.LogError("TalkTips trial gate is enabled but TALKTIPS_TRIAL_STATUS_API_KEY is not configured.");
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "enrollment/integrations/trial-status")
            {
                Content = JsonContent.Create(new { phone = normalizedPhone })
            };
            request.Headers.Add("x-integration-key", integrationKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TalkTips trial-status request returned HTTP {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<TrialStatusResponse>(cancellationToken: cancellationToken);
            hasTried = result?.HasTried == true;
            if (hasTried)
            {
                _cache.Set(cacheKey, true, CacheDuration);
            }
            return hasTried;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "TalkTips trial-status request failed.");
            return false;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "TalkTips trial-status request timed out.");
            return false;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "TalkTips trial-status response could not be read.");
            return false;
        }
    }

    private static string? NormalizeEgyptianPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith("01", StringComparison.Ordinal)) return digits;
        if (digits.Length == 12 && digits.StartsWith("201", StringComparison.Ordinal)) return $"0{digits[2..]}";
        return null;
    }

    private sealed record TrialStatusResponse([property: JsonPropertyName("has_tried")] bool HasTried);
}
