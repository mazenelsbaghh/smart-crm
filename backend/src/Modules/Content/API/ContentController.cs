using System.Globalization;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Content.Domain;
using Modules.Content.Jobs;
using Modules.Content.Services;
using Shared.Infrastructure;
using Shared.Security;
using Shared.Storage;

namespace Modules.Content.API;

[ApiController]
[Authorize]
[Route("api/content")]
public sealed class ContentController : ControllerBase
{
    private const long MaxLogoBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedLogoTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" };

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IProjectAuthorizationService _authorization;
    private readonly IObjectStorage _objectStorage;
    private readonly LogoBrandingService _logoBranding;
    private readonly ContentImagePreviewService _imagePreviews;
    private readonly ContentWeeklyPlanService _weeklyPlans;
    private readonly IBackgroundJobClient _backgroundJobs;

    public ContentController(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IProjectAuthorizationService authorization,
        IObjectStorage objectStorage,
        LogoBrandingService logoBranding,
        ContentImagePreviewService imagePreviews,
        ContentWeeklyPlanService weeklyPlans,
        IBackgroundJobClient backgroundJobs)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _authorization = authorization;
        _objectStorage = objectStorage;
        _logoBranding = logoBranding;
        _imagePreviews = imagePreviews;
        _weeklyPlans = weeklyPlans;
        _backgroundJobs = backgroundJobs;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanRead(User, projectId)) return Forbid();
        var settings = await SettingsAsync(projectId, cancellationToken);
        var projectSettings = await _dbContext.ProjectSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId, cancellationToken);
        var pages = await _dbContext.ConnectedPages.IgnoreQueryFilters()
            .Where(page => page.ProjectId == projectId && page.IsActive)
            .OrderBy(page => page.PageName)
            .Select(page => new { pageId = page.FacebookPageId, pageName = page.PageName })
            .ToListAsync(cancellationToken);
        var posts = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .Where(post => post.ProjectId == projectId)
            .OrderByDescending(post => post.CreatedAt)
            .Take(40)
            .ToListAsync(cancellationToken);
        var postResponses = posts
            .Select(post => PostResponse(
                post,
                string.IsNullOrWhiteSpace(post.ImageObjectKey)
                    ? null
                    : ContentAssetRoutes.PostImage(post.Id)))
            .ToList();
        var weeklyPlans = await WeekPlanResponsesAsync(projectId, cancellationToken);

        var colors = DeserializeColors(settings?.BrandColorsJson);
        var logoUrl = string.IsNullOrWhiteSpace(settings?.LogoObjectKey)
            ? null
            : ContentAssetRoutes.Logo(settings.UpdatedAt);

        return Ok(new
        {
            imageModel = GeminiImageClient.HighestQualityModel,
            imageSize = GeminiImageClient.OutputSize,
            aspectRatio = GeminiImageClient.AspectRatio,
            aiConfigured = !string.IsNullOrWhiteSpace(projectSettings?.GeminiApiKey),
            knowledgeDocumentCount = await _dbContext.KnowledgeDocuments.IgnoreQueryFilters()
                .ReadyForGeneration(projectId)
                .CountAsync(cancellationToken),
            connectedPages = pages,
            settings = new
            {
                facebookPageId = settings?.FacebookPageId,
                facebookPageName = settings?.FacebookPageName,
                isEnabled = settings?.IsEnabled ?? false,
                hasApprovedStyle = settings?.HasApprovedStyle ?? false,
                dailyPublishTimeLocal = FormatTime(settings?.DailyPublishTimeLocal ?? new TimeSpan(10, 0, 0)),
                timezone = settings?.Timezone ?? projectSettings?.Timezone ?? "Africa/Cairo",
                nextPublishAtUtc = settings?.NextPublishAtUtc,
                lastPublishedAtUtc = settings?.LastPublishedAtUtc,
                lastError = settings?.LastError,
                stylePrompt = settings?.StylePrompt ?? ContentAutomationSettings.DefaultStylePrompt,
                logoFileName = settings?.LogoFileName,
                logoUrl,
                brandColors = colors,
                approvedSamplePostId = settings?.ApprovedSamplePostId
            },
            weeklyPlan = weeklyPlans.FirstOrDefault(),
            weeklyPlans,
            posts = postResponses
        });
    }

    [HttpGet("logo/file")]
    public async Task<IActionResult> GetLogoFile(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanRead(User, projectId)) return Forbid();
        var logo = await _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .Where(settings => settings.ProjectId == projectId && settings.LogoObjectKey != null)
            .Select(settings => new { settings.LogoObjectKey, settings.LogoMimeType })
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(logo?.LogoObjectKey)) return NotFound();

        var stream = await _objectStorage.DownloadAsync(logo.LogoObjectKey, cancellationToken);
        return File(stream, logo.LogoMimeType ?? "application/octet-stream", enableRangeProcessing: true);
    }

    [HttpGet("posts/{postId:guid}/image")]
    public async Task<IActionResult> GetPostImage(Guid postId, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanRead(User, projectId)) return Forbid();
        var post = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId
                && candidate.Id == postId,
                cancellationToken);
        if (string.IsNullOrWhiteSpace(post?.ImageObjectKey)) return NotFound();

        var stream = await _imagePreviews.GetOrCreateAsync(post, cancellationToken);
        Response.Headers.CacheControl = "private, max-age=604800, immutable";
        return File(stream, "image/jpeg", enableRangeProcessing: true);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(
        UpdateContentSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        if (!TimeSpan.TryParseExact(request.DailyPublishTimeLocal, "hh\\:mm", CultureInfo.InvariantCulture, out var publishTime)
            || publishTime < TimeSpan.Zero
            || publishTime >= TimeSpan.FromDays(1))
        {
            return BadRequest(new { error = "اكتب وقت النشر بصيغة HH:mm." });
        }
        if (string.IsNullOrWhiteSpace(request.StylePrompt) || request.StylePrompt.Length > 1500)
            return BadRequest(new { error = "وصف الهوية البصرية مطلوب وبحد أقصى 1500 حرف." });

        var page = string.IsNullOrWhiteSpace(request.FacebookPageId)
            ? null
            : await _dbContext.ConnectedPages.IgnoreQueryFilters()
                .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId
                    && candidate.FacebookPageId == request.FacebookPageId
                    && candidate.IsActive, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.FacebookPageId) && page is null)
            return BadRequest(new { error = "صفحة Facebook المختارة غير متصلة بالمشروع." });

        var projectTimezone = await _dbContext.ProjectSettings.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId)
            .Select(candidate => candidate.Timezone)
            .SingleOrDefaultAsync(cancellationToken);
        var timezone = ValidTimezone(projectTimezone) ? projectTimezone! : "Africa/Cairo";
        var settings = await GetOrCreateSettingsAsync(projectId, cancellationToken);
        var normalizedStylePrompt = request.StylePrompt.Trim();
        var styleChanged = !string.Equals(
            settings.StylePrompt,
            normalizedStylePrompt,
            StringComparison.Ordinal);
        var scheduleChanged = settings.DailyPublishTimeLocal != publishTime || settings.Timezone != timezone;
        if ((styleChanged || scheduleChanged)
            && await GenerationInProgressAsync(projectId, cancellationToken))
        {
            return Conflict(new { error = "استنى اكتمال التوليد الحالي قبل تغيير الشكل أو الموعد." });
        }
        if (request.IsEnabled && (styleChanged || !settings.HasApprovedStyle))
            return BadRequest(new { error = "اعتمد أول تصميم قبل تشغيل النشر اليومي." });
        if (request.IsEnabled && (page is null || string.IsNullOrWhiteSpace(settings.LogoObjectKey)))
            return BadRequest(new { error = "اللوجو وصفحة Facebook مطلوبان لتشغيل النشر." });
        if (request.IsEnabled && !await ApprovedSampleWasPublishedAsync(settings, cancellationToken))
            return BadRequest(new { error = "لا يبدأ الجدول إلا بعد نجاح نشر أول تصميم معتمد." });
        var rescheduledPlanNext = scheduleChanged && !styleChanged
            ? await _weeklyPlans.RescheduleActivePlanAsync(
                new ContentPlanScheduleChange(projectId, publishTime, timezone, DateTime.UtcNow),
                cancellationToken)
            : null;
        var approvedPlanNext = request.IsEnabled
            ? rescheduledPlanNext
                ?? await _weeklyPlans.NextApprovedPublishAtAsync(projectId, cancellationToken)
            : null;
        if (request.IsEnabled && approvedPlanNext is null)
            return BadRequest(new { error = "جهّز واعتمد خطة الأسبوع قبل تشغيل النشر اليومي." });

        settings.FacebookPageId = page?.FacebookPageId;
        settings.FacebookPageName = page?.PageName;
        settings.DailyPublishTimeLocal = publishTime;
        settings.Timezone = timezone;
        settings.StylePrompt = normalizedStylePrompt;
        settings.IsEnabled = request.IsEnabled;
        settings.NextPublishAtUtc = approvedPlanNext;
        if (styleChanged && settings.HasApprovedStyle)
        {
            settings.HasApprovedStyle = false;
            settings.ApprovedSamplePostId = null;
            settings.IsEnabled = false;
            settings.NextPublishAtUtc = null;
        }
        if (styleChanged)
        {
            await RejectObsoleteSamplesAsync(
                projectId,
                "تم تغيير شكل التصميم؛ هذه المعاينة لم تعد صالحة للاعتماد.",
                cancellationToken);
        }
        if (styleChanged)
        {
            settings.IsEnabled = false;
            settings.NextPublishAtUtc = null;
            await _weeklyPlans.MarkActivePlansRejectedAsync(
                projectId,
                "تم تغيير شكل التصميم؛ جهّز خطة أسبوع جديدة.",
                cancellationToken);
        }
        settings.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await Get(cancellationToken);
    }

    [HttpPost("logo")]
    [RequestSizeLimit(MaxLogoBytes)]
    public async Task<IActionResult> UploadLogo(IFormFile logo, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        if (logo is null || logo.Length == 0 || logo.Length > MaxLogoBytes)
            return BadRequest(new { error = "ارفع ملف لوجو حجمه لا يزيد عن 10MB." });
        if (!AllowedLogoTypes.Contains(logo.ContentType))
            return BadRequest(new { error = "صيغة اللوجو يجب أن تكون PNG أو JPG أو WebP." });

        if (await GenerationInProgressAsync(projectId, cancellationToken))
            return Conflict(new { error = "استنى اكتمال توليد الصورة الحالية قبل تغيير اللوجو." });

        await using var buffer = new MemoryStream();
        await logo.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        IReadOnlyList<string> palette;
        try
        {
            palette = await _logoBranding.ExtractPaletteAsync(buffer, cancellationToken);
        }
        catch (Exception exception) when (exception is SixLabors.ImageSharp.UnknownImageFormatException
            or SixLabors.ImageSharp.InvalidImageContentException)
        {
            return BadRequest(new { error = "ملف اللوجو ليس صورة صالحة." });
        }

        var extension = logo.ContentType.ToLowerInvariant() switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "jpg"
        };
        var objectKey = $"content/{projectId:N}/brand/logo-{Guid.NewGuid():N}.{extension}";
        buffer.Position = 0;
        await _objectStorage.UploadAsync(objectKey, buffer, logo.ContentType, cancellationToken);

        var settings = await GetOrCreateSettingsAsync(projectId, cancellationToken);
        settings.LogoObjectKey = objectKey;
        settings.LogoMimeType = logo.ContentType;
        settings.LogoFileName = Path.GetFileName(logo.FileName);
        settings.BrandColorsJson = JsonSerializer.Serialize(palette);
        settings.HasApprovedStyle = false;
        settings.ApprovedSamplePostId = null;
        settings.IsEnabled = false;
        settings.NextPublishAtUtc = null;
        settings.LastError = null;
        settings.UpdatedAt = DateTime.UtcNow;

        await RejectObsoleteSamplesAsync(
            projectId,
            "تم استبدال اللوجو؛ هذه المعاينة لم تعد صالحة للاعتماد.",
            cancellationToken);
        await _weeklyPlans.MarkActivePlansRejectedAsync(
            projectId,
            "تم استبدال اللوجو؛ جهّز خطة أسبوع جديدة بالهوية الحالية.",
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await Get(cancellationToken);
    }

    [HttpPost("sample")]
    public async Task<IActionResult> GenerateSample(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        var settings = await SettingsAsync(projectId, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings?.LogoObjectKey))
            return BadRequest(new { error = "ارفع اللوجو أولاً حتى نستخدم ألوانه وندمجه من غير خلفية." });
        var pending = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .AnyAsync(post => post.ProjectId == projectId
                && post.IsStyleSample
                && (post.Status == ContentPostStatus.Generating
                    || post.Status == ContentPostStatus.AwaitingApproval),
                cancellationToken);
        if (pending) return Conflict(new { error = "يوجد تصميم أول قيد التوليد أو بانتظار رأيك." });

        _backgroundJobs.Enqueue<ContentAutomationJob>(job => job.GenerateSampleAsync(projectId));
        return Accepted(new { message = "بدأ توليد أول تصميم بألوان اللوجو. سيظهر هنا بعد اكتماله." });
    }

    [HttpPost("posts/{postId:guid}/regenerate")]
    public async Task<IActionResult> Regenerate(Guid postId, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        var exists = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .AnyAsync(post => post.ProjectId == projectId
                && post.Id == postId
                && post.IsStyleSample
                && (post.Status == ContentPostStatus.AwaitingApproval
                    || post.Status == ContentPostStatus.GenerationFailed), cancellationToken);
        if (!exists) return BadRequest(new { error = "العينة غير جاهزة لإعادة التوليد." });
        _backgroundJobs.Enqueue<ContentAutomationJob>(job => job.RegenerateSampleAsync(projectId, postId));
        return Accepted(new { message = "سيتم إنشاء اقتراح جديد بنفس اللوجو والألوان." });
    }

    [HttpPost("weekly-plan")]
    public async Task<IActionResult> GenerateWeeklyPlan(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        var settings = await SettingsAsync(projectId, cancellationToken);
        if (settings is null || !await ApprovedSampleWasPublishedAsync(settings, cancellationToken))
            return BadRequest(new { error = "اعتمد وانشر أول تصميم قبل تجهيز خطة الأسبوع." });
        var unresolvedPlan = await HasUnresolvedWeekPlanAsync(projectId, cancellationToken);
        if (unresolvedPlan) return Conflict(new { error = "راجع أو استبدل الأسبوع الجاري قبل إضافة أسبوع جديد." });
        _backgroundJobs.Enqueue<ContentAutomationJob>(job => job.GenerateWeeklyPlanAsync(projectId));
        return Accepted(new { message = "بدأ تجهيز أسبوع إضافي من 7 أفكار جديدة من غير تكرار المحتوى القديم." });
    }

    [HttpPost("weekly-plans/{planId:guid}/approve")]
    public async Task<IActionResult> ApproveWeeklyPlan(Guid planId, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        try
        {
            await _weeklyPlans.ApproveAsync(projectId, planId, cancellationToken);
            return Ok(new { message = "تم اعتماد خطة الأسبوع، وسيُنشر كل بوست في يومه المحدد." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("weekly-plans/{planId:guid}/items/{itemId:guid}/approve")]
    public async Task<IActionResult> ApproveWeeklyPlanItem(
        Guid planId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        try
        {
            await _weeklyPlans.ApproveItemAsync(projectId, planId, itemId, cancellationToken);
            return Ok(new { message = "تمت الموافقة على صورة اليوم." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("weekly-plans/{planId:guid}/items/{itemId:guid}/regenerate")]
    public async Task<IActionResult> RegenerateWeeklyPlanItem(
        Guid planId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        try
        {
            await _weeklyPlans.RegenerableItemAsync(projectId, planId, itemId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        _backgroundJobs.Enqueue<ContentAutomationJob>(job =>
            job.RegenerateWeeklyPlanItemAsync(projectId, planId, itemId));
        return Accepted(new { message = "بدأ تجهيز صورة بديلة لنفس الفكرة والكابشن." });
    }

    [HttpPost("weekly-plans/{planId:guid}/regenerate")]
    public async Task<IActionResult> RegenerateWeeklyPlan(Guid planId, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        try
        {
            await _weeklyPlans.RejectAsync(
                projectId,
                planId,
                "طلب المستخدم خطة أسبوع بديلة.",
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        _backgroundJobs.Enqueue<ContentAutomationJob>(job => job.GenerateWeeklyPlanAsync(projectId));
        return Accepted(new { message = "سيتم تجهيز خطة أسبوع جديدة للمراجعة." });
    }

    [HttpPost("posts/{postId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid postId, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        var settings = await SettingsAsync(projectId, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings?.FacebookPageId))
            return BadRequest(new { error = "اختر صفحة Facebook واحفظ الإعدادات قبل الاعتماد." });
        var exists = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .AnyAsync(post => post.ProjectId == projectId
                && post.Id == postId
                && post.IsStyleSample
                && post.BrandLogoObjectKey == settings.LogoObjectKey
                && post.BrandStylePrompt == settings.StylePrompt
                && post.Status == ContentPostStatus.AwaitingApproval, cancellationToken);
        if (!exists) return BadRequest(new { error = "العينة غير جاهزة للاعتماد." });
        _backgroundJobs.Enqueue<ContentAutomationJob>(job => job.ApproveSampleAndStartAsync(projectId, postId));
        return Accepted(new { message = "تم إرسال العينة للنشر، وبعد نجاحها سنجهز خطة الأسبوع لموافقتك." });
    }

    [HttpPost("posts/{postId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid postId, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!_authorization.CanManageProject(User, projectId)) return Forbid();
        var exists = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .AnyAsync(post => post.ProjectId == projectId
                && post.Id == postId
                && (post.Status == ContentPostStatus.Approved
                    || post.Status == ContentPostStatus.PublishFailed),
                cancellationToken);
        if (!exists) return BadRequest(new { error = "المنشور غير جاهز للنشر." });
        _backgroundJobs.Enqueue<ContentAutomationJob>(job => job.PublishPostAsync(projectId, postId));
        return Accepted(new { message = "تمت إضافة المنشور إلى طابور Facebook." });
    }

    private Guid ActiveProjectId() => _tenantContext.ProjectId != Guid.Empty
        ? _tenantContext.ProjectId
        : throw new UnauthorizedAccessException("الطلب لا يحتوي على مشروع صالح.");

    private Task<ContentAutomationSettings?> SettingsAsync(Guid projectId, CancellationToken cancellationToken) =>
        _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId, cancellationToken);

    private async Task<ContentAutomationSettings> GetOrCreateSettingsAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(projectId, cancellationToken);
        if (settings is not null) return settings;
        settings = new ContentAutomationSettings { ProjectId = projectId };
        _dbContext.ContentAutomationSettings.Add(settings);
        return settings;
    }

    private async Task<bool> ApprovedSampleWasPublishedAsync(
        ContentAutomationSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.ApprovedSamplePostId is not Guid approvedSamplePostId) return false;
        return await _dbContext.ContentPosts.IgnoreQueryFilters()
            .AnyAsync(post => post.ProjectId == settings.ProjectId
                && post.Id == approvedSamplePostId
                && post.Status == ContentPostStatus.Published,
                cancellationToken);
    }

    private async Task RejectObsoleteSamplesAsync(
        Guid projectId,
        string reason,
        CancellationToken cancellationToken)
    {
        var obsoleteSamples = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .Where(post => post.ProjectId == projectId
                && post.IsStyleSample
                && post.Status == ContentPostStatus.AwaitingApproval)
            .ToListAsync(cancellationToken);
        foreach (var obsoleteSample in obsoleteSamples)
        {
            obsoleteSample.Status = ContentPostStatus.Rejected;
            obsoleteSample.Error = reason;
            obsoleteSample.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task<bool> GenerationInProgressAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var imageGenerating = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .AnyAsync(post => post.ProjectId == projectId
                && post.Status == ContentPostStatus.Generating, cancellationToken);
        if (imageGenerating) return true;
        return await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .AnyAsync(plan => plan.ProjectId == projectId
                && plan.Status == ContentWeekPlanStatus.Generating, cancellationToken);
    }

    private async Task<bool> HasUnresolvedWeekPlanAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var activeDraftExists = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .AnyAsync(plan => plan.ProjectId == projectId
                && (plan.Status == ContentWeekPlanStatus.Generating
                    || plan.Status == ContentWeekPlanStatus.AwaitingApproval), cancellationToken);
        if (activeDraftExists) return true;
        var latestStatus = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .Where(plan => plan.ProjectId == projectId)
            .OrderByDescending(plan => plan.CreatedAt)
            .Select(plan => (ContentWeekPlanStatus?)plan.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return latestStatus == ContentWeekPlanStatus.GenerationFailed;
    }

    private async Task<object[]> WeekPlanResponsesAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var visiblePlans = await VisibleWeekPlansAsync(projectId, cancellationToken);
        var visiblePlanIds = visiblePlans.Select(plan => plan.Id).ToArray();
        var planItems = await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
            .Where(planItem => planItem.ProjectId == projectId && visiblePlanIds.Contains(planItem.PlanId))
            .OrderBy(planItem => planItem.ScheduledForUtc)
            .ToListAsync(cancellationToken);
        var linkedPostIds = planItems.Where(planItem => planItem.ContentPostId.HasValue)
            .Select(planItem => planItem.ContentPostId!.Value)
            .Distinct()
            .ToArray();
        var linkedPosts = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .Where(post => post.ProjectId == projectId && linkedPostIds.Contains(post.Id))
            .ToListAsync(cancellationToken);
        return visiblePlans.Select(plan => WeekPlanResponse(
                plan,
                planItems.Where(planItem => planItem.PlanId == plan.Id)
                    .OrderBy(planItem => planItem.DayIndex)
                    .ToArray(),
                linkedPosts))
            .ToArray();
    }

    private async Task<List<ContentWeekPlan>> VisibleWeekPlansAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var allPlans = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .Where(plan => plan.ProjectId == projectId)
            .OrderBy(plan => plan.CreatedAt)
            .ToListAsync(cancellationToken);
        var visiblePlans = allPlans.Where(plan => plan.Status is ContentWeekPlanStatus.Generating
                or ContentWeekPlanStatus.AwaitingApproval
                or ContentWeekPlanStatus.Approved)
            .ToList();
        IncludeLatestFailedOrFallbackPlan(visiblePlans, allPlans.MaxBy(plan => plan.CreatedAt));
        return visiblePlans.OrderBy(plan => plan.StartDateLocal)
            .ThenBy(plan => plan.CreatedAt)
            .ToList();
    }

    private static void IncludeLatestFailedOrFallbackPlan(
        ICollection<ContentWeekPlan> visiblePlans,
        ContentWeekPlan? latestPlan)
    {
        if (latestPlan is null) return;
        if (visiblePlans.Count == 0
            || (latestPlan.Status == ContentWeekPlanStatus.GenerationFailed
                && visiblePlans.All(plan => plan.Id != latestPlan.Id)))
        {
            visiblePlans.Add(latestPlan);
        }
    }

    private static object WeekPlanResponse(
        ContentWeekPlan plan,
        IReadOnlyList<ContentWeekPlanItem> items,
        IReadOnlyList<ContentPost> posts)
    {
        var postsById = posts.ToDictionary(post => post.Id);
        return new
        {
            plan.Id,
            status = plan.Status.ToString(),
            startDateLocal = plan.StartDateLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            dailyPublishTimeLocal = FormatTime(plan.DailyPublishTimeLocal),
            plan.Timezone,
            plan.KnowledgeDocumentCount,
            plan.GeneratedAtUtc,
            plan.ApprovedAtUtc,
            plan.CompletedAtUtc,
            plan.Error,
            items = items.Select(item =>
            {
                postsById.TryGetValue(item.ContentPostId ?? Guid.Empty, out var post);
                return new
                {
                    item.Id,
                    item.DayIndex,
                    item.ScheduledForUtc,
                    item.Topic,
                    item.VisualHeadline,
                    caption = ContentGenerationService.NormalizeCaptionTone(item.Caption),
                    item.ContentPostId,
                    postStatus = post?.Status.ToString(),
                    postPublishedAtUtc = post?.PublishedAtUtc,
                    postError = post?.Error,
                    imageSize = post?.ImageSize,
                    imageUrl = post is null || string.IsNullOrWhiteSpace(post.ImageObjectKey)
                        ? null
                        : ContentAssetRoutes.PostImage(post.Id)
                };
            }).ToArray()
        };
    }

    private static object PostResponse(ContentPost post, string? imageUrl) => new
    {
        post.Id,
        status = post.Status.ToString(),
        post.IsStyleSample,
        post.Topic,
        post.VisualHeadline,
        caption = ContentGenerationService.NormalizeCaptionTone(post.Caption),
        post.ImageModel,
        post.ImageSize,
        post.KnowledgeDocumentCount,
        post.ScheduledForUtc,
        post.GeneratedAtUtc,
        post.ApprovedAtUtc,
        post.PublishedAtUtc,
        post.FacebookPostId,
        post.Error,
        post.CreatedAt,
        imageUrl
    };

    private static string[] DeserializeColors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private static bool ValidTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone)) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timezone); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    private static string FormatTime(TimeSpan value) => $"{value.Hours:00}:{value.Minutes:00}";
}

public sealed record UpdateContentSettingsRequest(
    string? FacebookPageId,
    string DailyPublishTimeLocal,
    string StylePrompt,
    bool IsEnabled);

internal static class ContentAssetRoutes
{
    internal static string Logo(DateTime updatedAt) => $"/api/content/logo/file?v={updatedAt.Ticks}";

    internal static string PostImage(Guid postId) => $"/api/content/posts/{postId:D}/image";
}
