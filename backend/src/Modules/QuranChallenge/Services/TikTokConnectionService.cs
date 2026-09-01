using Microsoft.EntityFrameworkCore;
using Modules.QuranChallenge.Domain;
using Shared.Infrastructure;

namespace Modules.QuranChallenge.Services;

public sealed class TikTokConnectionService
{
    private const string VerifiedConnectionMarker = "zernio:creator-info-verified";
    private readonly AppDbContext _dbContext;
    private readonly TikTokApiClient _apiClient;
    private readonly string? _accountId;
    private readonly string? _profileId;
    private readonly string _dashboardUrl;

    public TikTokConnectionService(AppDbContext dbContext, TikTokApiClient apiClient, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _apiClient = apiClient;
        _accountId = configuration["ZERNIO_TIKTOK_ACCOUNT_ID"];
        _profileId = configuration["ZERNIO_PROFILE_ID"];
        _dashboardUrl = configuration["ZERNIO_DASHBOARD_URL"] ?? "https://zernio.com/dashboard/connections";
    }

    public bool IsConfigured => _apiClient.IsConfigured
        && !string.IsNullOrWhiteSpace(_accountId)
        && !string.IsNullOrWhiteSpace(_profileId);

    public string AccountId => !string.IsNullOrWhiteSpace(_accountId)
        ? _accountId!
        : throw new InvalidOperationException("إعدادات حساب TikTok في Zernio غير مكتملة.");

    public bool IsVerified(QuranTikTokSettings? settings) =>
        settings is not null
        && IsConfigured
        && string.Equals(settings.OpenId, _accountId, StringComparison.Ordinal)
        && string.Equals(settings.GrantedScopes, VerifiedConnectionMarker, StringComparison.Ordinal);

    public string AuthorizationUrl()
    {
        var query = QueryString.Create(new Dictionary<string, string?>
        {
            ["profile"] = _profileId,
            ["group"] = "profile",
            ["profileId"] = _profileId,
            ["accountId"] = _accountId
        });
        return $"{_dashboardUrl}{query}";
    }

    public Task<QuranTikTokSettings?> SettingsAsync(Guid projectId, CancellationToken cancellationToken) =>
        _dbContext.QuranTikTokSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);

    public async Task<QuranTikTokSettings> EnsureSettingsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(projectId, cancellationToken);
        if (settings is not null) return settings;
        settings = new QuranTikTokSettings { ProjectId = projectId };
        _dbContext.QuranTikTokSettings.Add(settings);
        return settings;
    }

    public async Task<QuranTikTokSettings> MarkVerifiedAsync(
        Guid projectId,
        TikTokCreatorInfo creator,
        CancellationToken cancellationToken)
    {
        var settings = await EnsureSettingsAsync(projectId, cancellationToken);
        settings.OpenId = AccountId;
        settings.DisplayName = creator.CreatorNickname ?? creator.CreatorUsername;
        settings.GrantedScopes = VerifiedConnectionMarker;
        settings.LastError = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
