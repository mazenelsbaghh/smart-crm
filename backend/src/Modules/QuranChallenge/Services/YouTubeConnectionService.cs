using System.Security.Cryptography;
using StackExchange.Redis;

namespace Modules.QuranChallenge.Services;

public sealed class YouTubeConnectionService
{
    private readonly IDatabase _redis;
    private readonly YouTubeOAuthClient _oauthClient;
    private readonly YouTubeTokenVault _tokenVault;
    private readonly string _frontendUrl;

    public YouTubeConnectionService(
        IConnectionMultiplexer redis,
        YouTubeOAuthClient oauthClient,
        YouTubeTokenVault tokenVault,
        IConfiguration configuration)
    {
        _redis = redis.GetDatabase();
        _oauthClient = oauthClient;
        _tokenVault = tokenVault;
        _frontendUrl = (configuration["FRONTEND_URL"] ?? "http://localhost:3000").TrimEnd('/');
    }

    public bool IsConfigured => _oauthClient.IsConfigured;

    public async Task<string> AuthorizationUrlAsync(Guid projectId)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await _redis.StringSetAsync($"youtube_oauth:{state}", projectId.ToString(), TimeSpan.FromMinutes(10));
        return _oauthClient.AuthorizationUrl(state);
    }

    public async Task<YouTubeConnection> CompleteAsync(string code, string state, CancellationToken cancellationToken)
    {
        var projectValue = await _redis.StringGetDeleteAsync($"youtube_oauth:{state}");
        if (!Guid.TryParse(projectValue, out var projectId)) throw new InvalidOperationException("انتهت صلاحية محاولة الربط.");
        var tokens = await _oauthClient.ExchangeCodeAsync(code, cancellationToken);
        var refreshToken = tokens.RefreshToken ?? throw new InvalidOperationException("Google لم يُرجع رمز تحديث دائم. أعد الربط ووافق على الصلاحيات.");
        var channel = await _oauthClient.ChannelAsync(tokens.AccessToken, cancellationToken);
        return new YouTubeConnection(projectId, channel.Id, channel.Title, _tokenVault.Protect(refreshToken));
    }

    public Task RevokeAsync(string protectedRefreshToken, CancellationToken cancellationToken)
    {
        return _oauthClient.RevokeAsync(_tokenVault.Unprotect(protectedRefreshToken), cancellationToken);
    }

    public string ChallengeUrl(string query) => $"{_frontendUrl}/quran-challenge?{query}";
}

public sealed record YouTubeConnection(Guid ProjectId, string ChannelId, string ChannelTitle, string ProtectedRefreshToken);
