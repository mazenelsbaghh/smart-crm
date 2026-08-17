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
[Route("api/quran/youtube")]
public sealed class QuranYouTubeController : ControllerBase
{
    private const int MaxCaptionLength = 5000;
    private readonly AppDbContext _dbContext;
    private readonly YouTubeConnectionService _connectionService;
    private readonly ITenantContext _tenantContext;

    public QuranYouTubeController(
        AppDbContext dbContext,
        YouTubeConnectionService connectionService,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _connectionService = connectionService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        var settings = await SettingsForProjectAsync(projectId, cancellationToken);
        return Ok(new
        {
            oauthConfigured = _connectionService.IsConfigured,
            connected = settings?.ProtectedRefreshToken is not null,
            channelId = settings?.ChannelId,
            channelTitle = settings?.ChannelTitle,
            isEnabled = settings?.IsEnabled ?? false,
            intervalHours = settings?.IntervalHours ?? 4,
            privacyStatus = settings?.PrivacyStatus ?? "public",
            captionTemplate = settings?.CaptionTemplate ?? QuranYouTubeSettings.DefaultCaption,
            nextPublishAtUtc = settings?.NextPublishAtUtc,
            lastPublishedAtUtc = settings?.LastPublishedAtUtc,
            lastVideoId = settings?.LastVideoId,
            lastError = settings?.LastError
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateYouTubeSettingsRequest request, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        var validationError = Validate(request);
        if (validationError is not null) return BadRequest(new { error = validationError });
        var settings = await GetOrCreateSettingsAsync(projectId, cancellationToken);
        if (request.IsEnabled && settings.ProtectedRefreshToken is null)
        {
            return BadRequest(new { error = "اربط قناة YouTube قبل تشغيل النشر التلقائي." });
        }
        ApplySettings(settings, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await Get(cancellationToken);
    }

    [HttpGet("connect")]
    public async Task<IActionResult> Connect()
    {
        var projectId = ActiveProjectId();
        if (!_connectionService.IsConfigured) return BadRequest(new { error = "بيانات Google OAuth غير مُعدّة على الخادم." });
        return Ok(new { authorizationUrl = await _connectionService.AuthorizationUrlAsync(projectId) });
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error)) return RedirectToChallenge("youtube=denied");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return RedirectToChallenge("youtube=invalid");
        var connection = await _connectionService.CompleteAsync(code, state, cancellationToken);
        var settings = await SettingsForProjectAsync(connection.ProjectId, cancellationToken) ?? NewSettings(connection.ProjectId);
        settings.ChannelId = connection.ChannelId;
        settings.ChannelTitle = connection.ChannelTitle;
        settings.ProtectedRefreshToken = connection.ProtectedRefreshToken;
        settings.LastError = null;
        settings.UpdatedAt = DateTime.UtcNow;
        if (_dbContext.Entry(settings).State == EntityState.Detached) _dbContext.QuranYouTubeSettings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return RedirectToChallenge("youtube=connected");
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        var settings = await SettingsForProjectAsync(projectId, cancellationToken);
        if (settings?.ProtectedRefreshToken is null) return NoContent();
        await _connectionService.RevokeAsync(settings.ProtectedRefreshToken, cancellationToken);
        settings.ProtectedRefreshToken = null;
        settings.ChannelId = null;
        settings.ChannelTitle = null;
        settings.IsEnabled = false;
        settings.NextPublishAtUtc = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("publish-now")]
    public async Task<IActionResult> PublishNow(PublishCurrentVerseRequest request, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        var settings = await SettingsForProjectAsync(projectId, cancellationToken);
        if (settings?.ProtectedRefreshToken is null) return BadRequest(new { error = "اربط قناة YouTube أولاً." });
        var selection = new QuranVerseSelection(request.SurahNumber, request.AyahNumber, request.HiddenWordIndex);
        BackgroundJob.Enqueue<QuranYouTubeScheduler>(scheduler => scheduler.PublishCurrentVerseAsync(projectId, selection));
        return Accepted(new { message = "تمت إضافة الآية إلى طابور النشر." });
    }

    private Guid ActiveProjectId()
    {
        return _tenantContext.ProjectId != Guid.Empty
            ? _tenantContext.ProjectId
            : throw new UnauthorizedAccessException("الطلب لا يحتوي على مشروع صالح.");
    }

    private Task<QuranYouTubeSettings?> SettingsForProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        return _dbContext.QuranYouTubeSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);
    }

    private async Task<QuranYouTubeSettings> GetOrCreateSettingsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var settings = await SettingsForProjectAsync(projectId, cancellationToken);
        if (settings is not null) return settings;
        settings = NewSettings(projectId);
        _dbContext.QuranYouTubeSettings.Add(settings);
        return settings;
    }

    private static QuranYouTubeSettings NewSettings(Guid projectId) => new() { ProjectId = projectId };

    private static string? Validate(UpdateYouTubeSettingsRequest request)
    {
        if (request.IntervalHours is < 1 or > 168) return "الفترة يجب أن تكون من ساعة إلى 168 ساعة.";
        if (string.IsNullOrWhiteSpace(request.CaptionTemplate) || request.CaptionTemplate.Length > MaxCaptionLength) return "الوصف مطلوب وبحد أقصى 5000 حرف.";
        if (request.PrivacyStatus is not ("public" or "unlisted" or "private")) return "حالة الخصوصية غير صحيحة.";
        return null;
    }

    private static void ApplySettings(QuranYouTubeSettings settings, UpdateYouTubeSettingsRequest request)
    {
        var now = DateTime.UtcNow;
        var enabling = request.IsEnabled && !settings.IsEnabled;
        settings.IntervalHours = request.IntervalHours;
        settings.CaptionTemplate = request.CaptionTemplate.Trim();
        settings.PrivacyStatus = request.PrivacyStatus;
        settings.IsEnabled = request.IsEnabled;
        settings.UpdatedAt = now;
        settings.NextPublishAtUtc = request.IsEnabled
            ? enabling
                ? now.AddHours(request.IntervalHours)
                : Earliest(settings.NextPublishAtUtc, now.AddHours(request.IntervalHours))
            : null;
    }

    private static DateTime Earliest(DateTime? current, DateTime candidate)
    {
        return current is null || current > candidate ? candidate : current.Value;
    }

    private RedirectResult RedirectToChallenge(string query) => Redirect(_connectionService.ChallengeUrl(query));
}

public sealed record UpdateYouTubeSettingsRequest(bool IsEnabled, int IntervalHours, string PrivacyStatus, string CaptionTemplate);
public sealed record PublishCurrentVerseRequest(int SurahNumber, int AyahNumber, int HiddenWordIndex);
