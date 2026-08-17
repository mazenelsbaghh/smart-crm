using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.QuranChallenge.Domain;
using Modules.QuranChallenge.Jobs;
using Modules.QuranChallenge.Services;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.QuranChallenge.API;

[ApiController]
[Authorize]
[Route("api/quran/tiktok")]
public sealed class QuranTikTokController : ControllerBase
{
    private const int MaxTitleLength = 2200;
    private readonly AppDbContext _dbContext;
    private readonly TikTokApiClient _apiClient;
    private readonly TikTokConnectionService _connectionService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<QuranTikTokController> _logger;

    public QuranTikTokController(
        AppDbContext dbContext,
        TikTokApiClient apiClient,
        TikTokConnectionService connectionService,
        ITenantContext tenantContext,
        ILogger<QuranTikTokController> logger)
    {
        _dbContext = dbContext;
        _apiClient = apiClient;
        _connectionService = connectionService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        return Ok(new
        {
            appConfigured = _connectionService.IsConfigured,
            connected = settings?.ProtectedRefreshToken is not null,
            displayName = settings?.DisplayName,
            lastPublishedAtUtc = settings?.LastPublishedAtUtc,
            lastPublishId = settings?.LastPublishId,
            lastPublishStatus = settings?.LastPublishStatus,
            lastError = settings?.LastError
        });
    }

    [HttpGet("connect")]
    public async Task<IActionResult> Connect()
    {
        if (!_connectionService.IsConfigured)
        {
            return BadRequest(new { error = "بيانات TikTok App غير مُعدّة على الخادم." });
        }
        return Ok(new
        {
            authorizationUrl = await _connectionService.AuthorizationUrlAsync(ActiveProjectId())
        });
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        string? code,
        string? state,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error)) return RedirectToChallenge("tiktok=denied");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return RedirectToChallenge("tiktok=invalid");
        }
        try
        {
            var connection = await _connectionService.CompleteAsync(code, state, cancellationToken);
            var settings = await _connectionService.SettingsAsync(connection.ProjectId, cancellationToken)
                ?? new QuranTikTokSettings { ProjectId = connection.ProjectId };
            _connectionService.ApplyConnection(settings, connection);
            if (_dbContext.Entry(settings).State == EntityState.Detached)
            {
                _dbContext.QuranTikTokSettings.Add(settings);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            return RedirectToChallenge("tiktok=connected");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or HttpRequestException)
        {
            _logger.LogWarning(exception, "TikTok OAuth callback failed");
            return RedirectToChallenge("tiktok=failed");
        }
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        if (settings is null) return NoContent();
        await _connectionService.DisconnectAsync(settings, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("creator-info")]
    public async Task<IActionResult> CreatorInfo(CancellationToken cancellationToken)
    {
        var settings = await RequiredSettingsAsync(cancellationToken);
        var accessToken = await _connectionService.AccessTokenAsync(settings, cancellationToken);
        var creator = await _apiClient.CreatorInfoAsync(accessToken, cancellationToken);
        return Ok(creator);
    }

    [HttpPost("publish-now")]
    public async Task<IActionResult> PublishNow(
        PublishTikTokRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null) return BadRequest(new { error = validationError });
        var settings = await SettingsAsync(cancellationToken);
        if (settings?.ProtectedRefreshToken is null)
        {
            return BadRequest(new { error = "اربط حساب TikTok أولاً." });
        }
        var selection = new QuranVerseSelection(
            request.SurahNumber,
            request.AyahNumber,
            request.HiddenWordIndex);
        var post = new TikTokPostRequest(
            request.Title.Trim(),
            request.PrivacyLevel,
            request.AllowComment,
            request.AllowDuet,
            request.AllowStitch);
        BackgroundJob.Enqueue<QuranTikTokPublishJob>(
            job => job.PublishAsync(ActiveProjectId(), selection, post));
        return Accepted(new { message = "بدأ تجهيز فيديو الآية وإرساله إلى TikTok." });
    }

    [HttpPost("refresh-status")]
    public async Task<IActionResult> RefreshStatus(CancellationToken cancellationToken)
    {
        var settings = await RequiredSettingsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.LastPublishId))
        {
            return BadRequest(new { error = "لا توجد عملية نشر لمتابعتها." });
        }
        var accessToken = await _connectionService.AccessTokenAsync(settings, cancellationToken);
        var status = await _apiClient.PublishStatusAsync(
            accessToken,
            settings.LastPublishId,
            cancellationToken);
        settings.LastPublishStatus = status.Status;
        settings.LastError = status.FailReason;
        settings.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new
        {
            status = status.Status,
            failReason = status.FailReason,
            postIds = status.PubliclyAvailablePostIds
        });
    }

    private Guid ActiveProjectId() => _tenantContext.ProjectId != Guid.Empty
        ? _tenantContext.ProjectId
        : throw new UnauthorizedAccessException("الطلب لا يحتوي على مشروع صالح.");

    private Task<QuranTikTokSettings?> SettingsAsync(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        return _dbContext.QuranTikTokSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);
    }

    private async Task<QuranTikTokSettings> RequiredSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        return settings?.ProtectedRefreshToken is not null
            ? settings
            : throw new InvalidOperationException("اربط حساب TikTok أولاً.");
    }

    private static string? Validate(PublishTikTokRequest request)
    {
        if (!request.Consent) return "يجب الموافقة على تأكيد استخدام الموسيقى قبل النشر.";
        if (request.SurahNumber is < 1 or > 114 || request.AyahNumber < 1)
        {
            return "رقم السورة أو الآية غير صحيح.";
        }
        if (request.HiddenWordIndex < 1) return "الكلمة المخفية غير صحيحة.";
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > MaxTitleLength)
        {
            return "وصف TikTok مطلوب وبحد أقصى 2200 حرف.";
        }
        if (request.PrivacyLevel is not ("PUBLIC_TO_EVERYONE"
            or "MUTUAL_FOLLOW_FRIENDS"
            or "FOLLOWER_OF_CREATOR"
            or "SELF_ONLY"))
        {
            return "اختر مستوى خصوصية صالحًا.";
        }
        return null;
    }

    private RedirectResult RedirectToChallenge(string query) =>
        Redirect(_connectionService.ChallengeUrl(query));
}

public sealed record PublishTikTokRequest(
    int SurahNumber,
    int AyahNumber,
    int HiddenWordIndex,
    string Title,
    string PrivacyLevel,
    bool AllowComment,
    bool AllowDuet,
    bool AllowStitch,
    bool Consent);
