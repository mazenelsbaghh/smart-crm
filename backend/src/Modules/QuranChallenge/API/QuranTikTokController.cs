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
    private const int MaxCaptionLength = 2200;
    private readonly AppDbContext _dbContext;
    private readonly TikTokApiClient _apiClient;
    private readonly TikTokConnectionService _connectionService;
    private readonly IProjectAuthorizationService _authorization;

    public QuranTikTokController(
        AppDbContext dbContext,
        TikTokApiClient apiClient,
        TikTokConnectionService connectionService,
        IProjectAuthorizationService authorization)
    {
        _dbContext = dbContext;
        _apiClient = apiClient;
        _connectionService = connectionService;
        _authorization = authorization;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanRead(User, projectId)) return Forbid();
        var settings = await SettingsAsync(projectId, cancellationToken);
        var connected = _connectionService.IsVerified(settings);
        return Ok(new
        {
            appConfigured = _connectionService.IsConfigured,
            connected,
            displayName = connected ? settings?.DisplayName : null,
            isEnabled = settings?.IsEnabled ?? false,
            intervalHours = settings?.IntervalHours ?? 4,
            privacyLevel = settings?.PrivacyLevel ?? "PUBLIC_TO_EVERYONE",
            allowComment = settings?.AllowComment ?? true,
            allowDuet = settings?.AllowDuet ?? false,
            allowStitch = settings?.AllowStitch ?? false,
            captionTemplate = settings?.CaptionTemplate ?? QuranTikTokSettings.DefaultCaption,
            nextPublishAtUtc = settings?.NextPublishAtUtc,
            lastPublishedAtUtc = settings?.LastPublishedAtUtc,
            lastPublishId = settings?.LastPublishId,
            lastPublishStatus = settings?.LastPublishStatus,
            lastError = settings?.LastError
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        UpdateTikTokSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        var validationError = ValidateSettings(request);
        if (validationError is not null) return BadRequest(new { error = validationError });
        if (request.IsEnabled && !_connectionService.IsConfigured)
        {
            return BadRequest(new { error = "أكمل إعداد Zernio قبل تشغيل النشر التلقائي." });
        }
        if (_connectionService.IsConfigured)
        {
            var creator = await _apiClient.CreatorInfoAsync(_connectionService.AccountId, cancellationToken);
            validationError = ValidateCreatorSettings(request, creator);
            if (validationError is not null) return BadRequest(new { error = validationError });
            await _connectionService.MarkVerifiedAsync(projectId, creator, cancellationToken);
        }
        var settings = await _connectionService.EnsureSettingsAsync(projectId, cancellationToken);
        if (request.IsEnabled && !_connectionService.IsVerified(settings))
            return BadRequest(new { error = "تحقق من ربط حساب TikTok قبل تشغيل النشر التلقائي." });
        ApplySettings(settings, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await Get(cancellationToken);
    }

    [HttpGet("connect")]
    public IActionResult Connect()
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        if (!_connectionService.IsConfigured)
            return BadRequest(new { error = "إعدادات Zernio غير مكتملة على الخادم." });
        return Ok(new
        {
            authorizationUrl = _connectionService.AuthorizationUrl()
        });
    }

    [HttpGet("creator-info")]
    public async Task<IActionResult> CreatorInfo(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanRead(User, projectId)) return Forbid();
        if (!_connectionService.IsConfigured)
            return BadRequest(new { error = "إعدادات Zernio غير مكتملة على الخادم." });
        var settings = await SettingsAsync(projectId, cancellationToken);
        if (settings is null || !_connectionService.IsVerified(settings))
            return Conflict(new { error = "تحقق من ربط حساب TikTok أولاً." });
        var creator = await _apiClient.CreatorInfoAsync(_connectionService.AccountId, cancellationToken);
        return Ok(creator);
    }

    [HttpPost("verify-connection")]
    public async Task<IActionResult> VerifyConnection(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        if (!_connectionService.IsConfigured)
            return BadRequest(new { error = "إعدادات Zernio غير مكتملة على الخادم." });
        var creator = await _apiClient.CreatorInfoAsync(_connectionService.AccountId, cancellationToken);
        await _connectionService.MarkVerifiedAsync(projectId, creator, cancellationToken);
        return Ok(creator);
    }

    [HttpPost("publish-now")]
    public async Task<IActionResult> PublishNow(
        PublishCurrentVerseRequest request,
        CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        if (!_connectionService.IsConfigured) return BadRequest(new { error = "أكمل إعداد Zernio أولاً." });
        var settings = await SettingsAsync(projectId, cancellationToken);
        if (settings is null || !_connectionService.IsVerified(settings))
            return BadRequest(new { error = "تحقق من ربط حساب TikTok أولاً." });
        var selection = new QuranVerseSelection(
            request.SurahNumber,
            request.AyahNumber,
            request.HiddenWordIndex);
        BackgroundJob.Enqueue<QuranTikTokPublishJob>(
            job => job.PublishCurrentVerseAsync(projectId, selection));
        return Accepted(new { message = "بدأ تجهيز فيديو الآية وإرساله إلى TikTok." });
    }

    [HttpPost("refresh-status")]
    public async Task<IActionResult> RefreshStatus(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        if (!_connectionService.IsConfigured)
            return BadRequest(new { error = "إعدادات Zernio غير مكتملة على الخادم." });
        var settings = await SettingsAsync(projectId, cancellationToken);
        if (settings is null || !_connectionService.IsVerified(settings))
            return BadRequest(new { error = "تحقق من ربط حساب TikTok أولاً." });
        if (string.IsNullOrWhiteSpace(settings.LastPublishId))
        {
            return BadRequest(new { error = "لا توجد عملية نشر لمتابعتها." });
        }
        var status = await _apiClient.PublishStatusAsync(settings.LastPublishId, cancellationToken);
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

    private Guid ActiveProjectId() => _authorization.GetProjectId(User) is { } projectId && projectId != Guid.Empty
        ? projectId
        : throw new UnauthorizedAccessException("الطلب لا يحتوي على مشروع صالح.");

    private Task<QuranTikTokSettings?> SettingsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        return _dbContext.QuranTikTokSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);
    }

    private static string? ValidateSettings(UpdateTikTokSettingsRequest request)
    {
        if (request.IntervalHours is < 1 or > 168) return "الفترة يجب أن تكون من ساعة إلى 168 ساعة.";
        if (string.IsNullOrWhiteSpace(request.CaptionTemplate) || request.CaptionTemplate.Length > MaxCaptionLength)
            return "الوصف مطلوب وبحد أقصى 2200 حرف.";
        if (request.PrivacyLevel is not ("PUBLIC_TO_EVERYONE"
            or "MUTUAL_FOLLOW_FRIENDS"
            or "FOLLOWER_OF_CREATOR"
            or "SELF_ONLY"))
        {
            return "اختر مستوى خصوصية صالحًا.";
        }
        return null;
    }

    private static void ApplySettings(QuranTikTokSettings settings, UpdateTikTokSettingsRequest request)
    {
        var now = DateTime.UtcNow;
        var enabling = request.IsEnabled && !settings.IsEnabled;
        settings.IsEnabled = request.IsEnabled;
        settings.IntervalHours = request.IntervalHours;
        settings.PrivacyLevel = request.PrivacyLevel;
        settings.AllowComment = request.AllowComment;
        settings.AllowDuet = request.AllowDuet;
        settings.AllowStitch = request.AllowStitch;
        settings.CaptionTemplate = request.CaptionTemplate.Trim();
        settings.UpdatedAt = now;
        settings.NextPublishAtUtc = request.IsEnabled
            ? enabling
                ? now.AddHours(request.IntervalHours)
                : Earliest(settings.NextPublishAtUtc, now.AddHours(request.IntervalHours))
            : null;
    }

    private static string? ValidateCreatorSettings(
        UpdateTikTokSettingsRequest request,
        TikTokCreatorInfo creator)
    {
        if (!creator.PrivacyLevelOptions.Contains(request.PrivacyLevel))
            return "مستوى الخصوصية غير متاح لهذا الحساب.";
        if (request.AllowComment && creator.CommentDisabled) return "التعليقات معطلة في حساب TikTok.";
        if (request.AllowDuet && creator.DuetDisabled) return "Duet معطل في حساب TikTok.";
        if (request.AllowStitch && creator.StitchDisabled) return "Stitch معطل في حساب TikTok.";
        return null;
    }

    private static DateTime Earliest(DateTime? current, DateTime candidate) =>
        current is null || current > candidate ? candidate : current.Value;

}

public sealed record UpdateTikTokSettingsRequest(
    bool IsEnabled,
    int IntervalHours,
    string PrivacyLevel,
    bool AllowComment,
    bool AllowDuet,
    bool AllowStitch,
    string CaptionTemplate);
