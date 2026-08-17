using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Modules.QuranChallenge.Domain;
using Shared.Infrastructure;
using StackExchange.Redis;

namespace Modules.QuranChallenge.Services;

public sealed class TikTokConnectionService
{
    private readonly AppDbContext _dbContext;
    private readonly IDatabase _redis;
    private readonly TikTokApiClient _apiClient;
    private readonly TikTokTokenVault _tokenVault;
    private readonly string _frontendUrl;

    public TikTokConnectionService(
        AppDbContext dbContext,
        IConnectionMultiplexer redis,
        TikTokApiClient apiClient,
        TikTokTokenVault tokenVault,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _redis = redis.GetDatabase();
        _apiClient = apiClient;
        _tokenVault = tokenVault;
        _frontendUrl = (configuration["FRONTEND_URL"] ?? "http://localhost:3000").TrimEnd('/');
    }

    public bool IsConfigured => _apiClient.IsConfigured;

    public async Task<string> AuthorizationUrlAsync(Guid projectId)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await _redis.StringSetAsync($"tiktok_oauth:{state}", projectId.ToString(), TimeSpan.FromMinutes(10));
        return _apiClient.AuthorizationUrl(state);
    }

    public async Task<TikTokConnection> CompleteAsync(
        string code,
        string state,
        CancellationToken cancellationToken)
    {
        var projectValue = await _redis.StringGetDeleteAsync($"tiktok_oauth:{state}");
        if (!Guid.TryParse(projectValue, out var projectId))
        {
            throw new InvalidOperationException("انتهت صلاحية محاولة ربط TikTok.");
        }
        var tokens = await _apiClient.ExchangeCodeAsync(code, cancellationToken);
        if (!tokens.Scope.Split(',', StringSplitOptions.TrimEntries).Contains("video.publish"))
        {
            throw new InvalidOperationException("لم تُمنح صلاحية نشر الفيديو إلى TikTok.");
        }
        var user = await _apiClient.UserAsync(tokens.AccessToken, cancellationToken);
        return new TikTokConnection(projectId, user, tokens);
    }

    public async Task<string> AccessTokenAsync(
        QuranTikTokSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.ProtectedAccessToken is null || settings.ProtectedRefreshToken is null)
        {
            throw new InvalidOperationException("اربط حساب TikTok أولاً.");
        }
        if (settings.AccessTokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
        {
            return _tokenVault.Unprotect(settings.ProtectedAccessToken);
        }
        var refreshed = await _apiClient.RefreshAsync(
            _tokenVault.Unprotect(settings.ProtectedRefreshToken),
            cancellationToken);
        ApplyTokens(settings, refreshed);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return refreshed.AccessToken;
    }

    public void ApplyConnection(QuranTikTokSettings settings, TikTokConnection connection)
    {
        settings.OpenId = connection.User.OpenId;
        settings.DisplayName = connection.User.DisplayName;
        ApplyTokens(settings, connection.Tokens);
        settings.LastError = null;
        settings.UpdatedAt = DateTime.UtcNow;
    }

    public async Task DisconnectAsync(
        QuranTikTokSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.ProtectedAccessToken is not null)
        {
            await _apiClient.RevokeAsync(
                _tokenVault.Unprotect(settings.ProtectedAccessToken),
                cancellationToken);
        }
        settings.OpenId = null;
        settings.DisplayName = null;
        settings.ProtectedAccessToken = null;
        settings.ProtectedRefreshToken = null;
        settings.AccessTokenExpiresAtUtc = null;
        settings.RefreshTokenExpiresAtUtc = null;
        settings.GrantedScopes = null;
        settings.UpdatedAt = DateTime.UtcNow;
    }

    public Task<QuranTikTokSettings?> SettingsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        return _dbContext.QuranTikTokSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);
    }

    public string ChallengeUrl(string query) => $"{_frontendUrl}/quran-challenge?{query}";

    private void ApplyTokens(QuranTikTokSettings settings, TikTokTokens tokens)
    {
        var now = DateTime.UtcNow;
        settings.OpenId = tokens.OpenId;
        settings.ProtectedAccessToken = _tokenVault.Protect(tokens.AccessToken);
        settings.ProtectedRefreshToken = _tokenVault.Protect(tokens.RefreshToken);
        settings.AccessTokenExpiresAtUtc = now.AddSeconds(tokens.ExpiresIn);
        settings.RefreshTokenExpiresAtUtc = now.AddSeconds(tokens.RefreshExpiresIn);
        settings.GrantedScopes = tokens.Scope;
        settings.UpdatedAt = now;
    }
}

public sealed record TikTokConnection(Guid ProjectId, TikTokUser User, TikTokTokens Tokens);
