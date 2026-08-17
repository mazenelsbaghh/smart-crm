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
[Route("api/quran/facebook")]
public sealed class QuranFacebookController : ControllerBase
{
    private const int MaxCaptionLength = 5000;
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ITenantContext _tenantContext;

    public QuranFacebookController(
        AppDbContext dbContext,
        IConfiguration configuration,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        var settings = await SettingsForProjectAsync(projectId, cancellationToken);
        var pages = await ActivePagesAsync(projectId, cancellationToken);
        return Ok(new
        {
            appConfigured = AppConfigured(),
            connectedPages = pages,
            facebookPageId = settings?.FacebookPageId,
            pageName = settings?.PageName,
            isEnabled = settings?.IsEnabled ?? false,
            intervalHours = settings?.IntervalHours ?? 4,
            captionTemplate = settings?.CaptionTemplate ?? QuranFacebookSettings.DefaultCaption,
            nextPublishAtUtc = settings?.NextPublishAtUtc,
            lastPublishedAtUtc = settings?.LastPublishedAtUtc,
            lastReelId = settings?.LastReelId,
            lastError = settings?.LastError
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        UpdateFacebookSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null) return BadRequest(new { error = validationError });
        var projectId = ActiveProjectId();
        var page = await ActivePageAsync(projectId, request.FacebookPageId, cancellationToken);
        if (request.IsEnabled && page is null) return BadRequest(new { error = "اختر صفحة Facebook متصلة أولاً." });
        var settings = await GetOrCreateSettingsAsync(projectId, cancellationToken);
        ApplySettings(settings, request, page?.PageName);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await Get(cancellationToken);
    }

    [HttpPost("publish-now")]
    public async Task<IActionResult> PublishNow(
        PublishCurrentVerseRequest request,
        CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        var settings = await SettingsForProjectAsync(projectId, cancellationToken);
        if (settings?.FacebookPageId is null) return BadRequest(new { error = "اختر صفحة Facebook أولاً." });
        var page = await ActivePageAsync(projectId, settings.FacebookPageId, cancellationToken);
        if (page is null) return BadRequest(new { error = "صفحة Facebook غير متصلة." });
        var selection = new QuranVerseSelection(request.SurahNumber, request.AyahNumber, request.HiddenWordIndex);
        BackgroundJob.Enqueue<QuranFacebookScheduler>(scheduler =>
            scheduler.PublishCurrentVerseAsync(projectId, selection));
        return Accepted(new { message = "تمت إضافة الآية إلى طابور Facebook Reels." });
    }

    private Guid ActiveProjectId()
    {
        return _tenantContext.ProjectId != Guid.Empty
            ? _tenantContext.ProjectId
            : throw new UnauthorizedAccessException("الطلب لا يحتوي على مشروع صالح.");
    }

    private bool AppConfigured()
    {
        return !string.IsNullOrWhiteSpace(_configuration["FACEBOOK_APP_ID"])
            && !string.IsNullOrWhiteSpace(_configuration["FACEBOOK_APP_SECRET"]);
    }

    private Task<QuranFacebookSettings?> SettingsForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        return _dbContext.QuranFacebookSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);
    }

    private Task<List<FacebookPageOption>> ActivePagesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        return _dbContext.ConnectedPages.IgnoreQueryFilters()
            .Where(page => page.ProjectId == projectId && page.IsActive)
            .OrderBy(page => page.PageName)
            .Select(page => new FacebookPageOption(page.FacebookPageId, page.PageName))
            .ToListAsync(cancellationToken);
    }

    private Task<Modules.Facebook.Domain.ConnectedPage?> ActivePageAsync(
        Guid projectId,
        string? pageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pageId)) return Task.FromResult<Modules.Facebook.Domain.ConnectedPage?>(null);
        return _dbContext.ConnectedPages.IgnoreQueryFilters()
            .SingleOrDefaultAsync(page => page.ProjectId == projectId
                && page.FacebookPageId == pageId
                && page.IsActive, cancellationToken);
    }

    private async Task<QuranFacebookSettings> GetOrCreateSettingsAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var settings = await SettingsForProjectAsync(projectId, cancellationToken);
        if (settings is not null) return settings;
        settings = new QuranFacebookSettings { ProjectId = projectId };
        _dbContext.QuranFacebookSettings.Add(settings);
        return settings;
    }

    private static string? Validate(UpdateFacebookSettingsRequest request)
    {
        if (request.IntervalHours is < 1 or > 168) return "الفترة يجب أن تكون من ساعة إلى 168 ساعة.";
        if (string.IsNullOrWhiteSpace(request.CaptionTemplate) || request.CaptionTemplate.Length > MaxCaptionLength)
            return "الوصف مطلوب وبحد أقصى 5000 حرف.";
        return null;
    }

    private static void ApplySettings(
        QuranFacebookSettings settings,
        UpdateFacebookSettingsRequest request,
        string? pageName)
    {
        var now = DateTime.UtcNow;
        var enabling = request.IsEnabled && !settings.IsEnabled;
        settings.FacebookPageId = request.FacebookPageId;
        settings.PageName = pageName;
        settings.IntervalHours = request.IntervalHours;
        settings.CaptionTemplate = request.CaptionTemplate.Trim();
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
}

public sealed record UpdateFacebookSettingsRequest(
    bool IsEnabled,
    int IntervalHours,
    string CaptionTemplate,
    string? FacebookPageId);
public sealed record FacebookPageOption(string PageId, string PageName);
