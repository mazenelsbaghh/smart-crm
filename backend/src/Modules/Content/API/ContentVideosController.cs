using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
[Route("api/content/videos")]
public sealed class ContentVideosController(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    IProjectAuthorizationService authorization,
    ContentVideoReadinessService readinessService,
    IObjectStorage objectStorage,
    ContentVideoDispatchService dispatch) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var readiness = await readinessService.GetAsync(projectId, cancellationToken);
        var videos = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(video => video.ProjectId == projectId)
            .OrderByDescending(video => video.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        var videoIds = videos.Select(video => video.Id).ToArray();
        var sceneCounts = await dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .Where(scene => scene.ProjectId == projectId && videoIds.Contains(scene.ContentVideoId))
            .GroupBy(scene => scene.ContentVideoId)
            .Select(group => new
            {
                VideoId = group.Key,
                Total = group.Count(),
                Completed = group.Count(scene => scene.Status == ContentVideoSceneStatus.Completed)
            })
            .ToDictionaryAsync(item => item.VideoId, cancellationToken);

        return Ok(new
        {
            readiness = ReadinessResponse(readiness),
            videos = videos.Select(video =>
            {
                sceneCounts.TryGetValue(video.Id, out var counts);
                return VideoResponse(video, counts?.Total ?? 0, counts?.Completed ?? 0);
            })
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var video = await VideoAsync(projectId, id, cancellationToken);
        if (video is null) return NotFound(new { error = "فكرة الفيديو غير موجودة." });
        var scenes = await ScenesAsync(projectId, id, cancellationToken);
        return Ok(VideoDetailResponse(video, scenes));
    }

    [HttpPost("plan")]
    public async Task<IActionResult> Plan(
        [FromBody] PlanContentVideoRequest request,
        CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        var validationError = ValidatePlanRequest(request);
        if (validationError is not null) return BadRequest(new { error = validationError });

        var readiness = await readinessService.GetAsync(projectId, cancellationToken);
        if (!readiness.Configured) return BadRequest(new { error = readiness.Reason });

        var video = new ContentVideo
        {
            ProjectId = projectId,
            Status = ContentVideoStatus.Planning,
            Brief = string.IsNullOrWhiteSpace(request.Brief) ? null : request.Brief.Trim(),
            AspectRatio = request.AspectRatio,
            Resolution = request.Resolution,
            RequestedSceneCount = request.SceneCount,
            RequestedSceneDurationSeconds = request.DurationSeconds,
            VideoModel = ContentVideoCapabilities.Model
        };
        dbContext.ContentVideos.Add(video);
        await dbContext.SaveChangesAsync(cancellationToken);
        dispatch.EnqueuePlan(projectId, video.Id);
        return Accepted(new
        {
            id = video.Id,
            status = video.Status.ToString(),
            message = "بدأ Gemini في تجهيز فكرة الفيديو ومشاهده."
        });
    }

    [HttpPost("{id:guid}/generate")]
    public async Task<IActionResult> Generate(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        var video = await VideoAsync(projectId, id, cancellationToken);
        if (video is null) return NotFound(new { error = "فكرة الفيديو غير موجودة." });
        if (video.Status != ContentVideoStatus.AwaitingApproval)
            return BadRequest(new { error = "الفكرة ليست جاهزة للاعتماد." });
        if (!await GenerationConfiguredAsync(projectId, true, cancellationToken))
            return BadRequest(new { error = "أكمل إعداد Google Cloud وبيانات اعتماد Gemini أولاً." });

        var scenes = await ScenesAsync(projectId, id, cancellationToken);
        if (scenes.Count != video.RequestedSceneCount
            || scenes.Any(scene => scene.Status != ContentVideoSceneStatus.Planned))
        {
            return BadRequest(new { error = "خطة المشاهد غير مكتملة." });
        }

        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var sceneClaimed = await dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .Where(scene => scene.ProjectId == projectId
                && scene.ContentVideoId == id
                && scene.Id == scenes[0].Id
                && scene.Status == ContentVideoSceneStatus.Planned
                && scene.ProviderInteractionId == null
                && scene.ProviderProjectId == null
                && scene.SubmissionClaimToken == null
                && scene.UpdatedAt == scenes[0].UpdatedAt
                && scene.TransientRetryCount == scenes[0].TransientRetryCount)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(scene => scene.Status, ContentVideoSceneStatus.Queued)
                .SetProperty(scene => scene.NextAttemptAtUtc, now)
                .SetProperty(scene => scene.Error, (string?)null)
                .SetProperty(scene => scene.UpdatedAt, now),
                cancellationToken);
        if (sceneClaimed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new { error = "تم بدء توليد هذا الفيديو من طلب آخر." });
        }

        var videoClaimed = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.Id == id
                && candidate.Status == ContentVideoStatus.AwaitingApproval)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, ContentVideoStatus.Generating)
                .SetProperty(candidate => candidate.ApprovedAtUtc, now)
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (videoClaimed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new { error = "تم تغيير حالة الفيديو من طلب آخر." });
        }
        await transaction.CommitAsync(cancellationToken);
        dispatch.EnqueueGeneration(projectId, id);
        return Accepted(new { id, status = ContentVideoStatus.Generating.ToString(), message = "بدأ توليد مشاهد الفيديو." });
    }

    [HttpPost("{id:guid}/scenes/{sceneId:guid}/retry")]
    public async Task<IActionResult> RetryScene(
        Guid id,
        Guid sceneId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RetryContentVideoSceneRequest? request,
        CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        var video = await VideoAsync(projectId, id, cancellationToken);
        var scene = await dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId
                && candidate.ContentVideoId == id
                && candidate.Id == sceneId, cancellationToken);
        if (video is null || scene is null) return NotFound(new { error = "المشهد غير موجود." });
        var submissionUncertain = scene.Status == ContentVideoSceneStatus.SubmissionUncertain;
        var recoveryRequired = scene.Status == ContentVideoSceneStatus.RecoveryRequired;
        if (video.Status != ContentVideoStatus.GenerationFailed
            || (scene.Status != ContentVideoSceneStatus.Failed
                && !submissionUncertain
                && !recoveryRequired))
        {
            return BadRequest(new { error = "يمكن إعادة محاولة المشهد الفاشل أو غير المؤكد أو المحتاج للاستكمال فقط." });
        }
        if (submissionUncertain && request?.ConfirmPossibleDuplicate != true)
            return BadRequest(new
            {
                error = "قد يكون Gemini استلم الطلب السابق. أرسل confirmPossibleDuplicate=true لتأكيد احتمال إنشاء نسخة مكررة."
            });
        if (recoveryRequired
            && (string.IsNullOrWhiteSpace(scene.ProviderInteractionId)
                || string.IsNullOrWhiteSpace(scene.ProviderProjectId)))
        {
            return BadRequest(new { error = "بيانات استكمال طلب Gemini غير مكتملة." });
        }
        if (!await GenerationConfiguredAsync(
                projectId,
                requireEnterpriseProjectId: !recoveryRequired,
                cancellationToken: cancellationToken))
            return BadRequest(new { error = "أكمل إعداد Google Cloud وبيانات اعتماد Gemini أولاً." });

        var now = DateTime.UtcNow;
        var nextSceneStatus = recoveryRequired
            ? ContentVideoSceneStatus.Submitted
            : ContentVideoSceneStatus.Queued;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var retryClaimed = recoveryRequired
            ? await ResumeSceneAsync(scene, nextSceneStatus, now, cancellationToken)
            : await ResetSceneForFreshSubmissionAsync(scene, nextSceneStatus, now, cancellationToken);
        if (retryClaimed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new { error = "تمت إعادة محاولة هذا المشهد من طلب آخر." });
        }

        var videoClaimed = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.Id == id
                && candidate.Status == ContentVideoStatus.GenerationFailed)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, ContentVideoStatus.Generating)
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (videoClaimed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new { error = "تم تغيير حالة الفيديو من طلب آخر." });
        }
        await transaction.CommitAsync(cancellationToken);
        dispatch.EnqueueGeneration(projectId, id);
        return Accepted(new
        {
            id,
            sceneId,
            status = nextSceneStatus.ToString(),
            message = recoveryRequired
                ? "تمت إعادة المشهد لاستكمال طلب Gemini الحالي."
                : "تمت إعادة المشهد إلى طابور التوليد."
        });
    }

    [HttpPost("{id:guid}/assembly/retry")]
    public async Task<IActionResult> RetryAssembly(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        var video = await VideoAsync(projectId, id, cancellationToken);
        if (video is null) return NotFound(new { error = "فكرة الفيديو غير موجودة." });
        if (video.Status != ContentVideoStatus.AssemblyFailed)
            return BadRequest(new { error = "الفيديو لا ينتظر إعادة التجميع." });
        var scenes = await ScenesAsync(projectId, id, cancellationToken);
        if (scenes.Count == 0 || scenes.Any(scene => scene.Status != ContentVideoSceneStatus.Completed))
            return BadRequest(new { error = "كل المشاهد يجب أن تكتمل قبل التجميع." });

        var now = DateTime.UtcNow;
        var claimed = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.Id == id
                && candidate.Status == ContentVideoStatus.AssemblyFailed)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, ContentVideoStatus.Assembling)
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (claimed != 1)
            return Conflict(new { error = "بدأت إعادة التجميع من طلب آخر." });
        dispatch.EnqueueAssembly(projectId, id);
        return Accepted(new { id, status = ContentVideoStatus.Assembling.ToString(), message = "بدأت إعادة تجميع الفيديو." });
    }

    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetFinalFile(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var video = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.Id == id
                && candidate.FinalVideoObjectKey != null)
            .Select(candidate => new { candidate.FinalVideoObjectKey, candidate.FinalVideoMimeType })
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(video?.FinalVideoObjectKey)) return NotFound();
        var stream = await objectStorage.DownloadAsync(video.FinalVideoObjectKey, cancellationToken);
        return File(stream, video.FinalVideoMimeType, enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/scenes/{sceneId:guid}/file")]
    public async Task<IActionResult> GetSceneFile(
        Guid id,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var scene = await dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.ContentVideoId == id
                && candidate.Id == sceneId
                && candidate.VideoObjectKey != null)
            .Select(candidate => new { candidate.VideoObjectKey, candidate.VideoMimeType })
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(scene?.VideoObjectKey)) return NotFound();
        var stream = await objectStorage.DownloadAsync(scene.VideoObjectKey, cancellationToken);
        return File(stream, scene.VideoMimeType, enableRangeProcessing: true);
    }

    private Guid ActiveProjectId() => tenantContext.ProjectId != Guid.Empty
        ? tenantContext.ProjectId
        : throw new UnauthorizedAccessException("الطلب لا يحتوي على مشروع صالح.");

    private Task<ContentVideo?> VideoAsync(Guid projectId, Guid id, CancellationToken cancellationToken) =>
        dbContext.ContentVideos.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(video => video.ProjectId == projectId && video.Id == id, cancellationToken);

    private Task<List<ContentVideoScene>> ScenesAsync(
        Guid projectId,
        Guid videoId,
        CancellationToken cancellationToken) =>
        dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(scene => scene.ProjectId == projectId && scene.ContentVideoId == videoId)
            .OrderBy(scene => scene.SceneIndex)
            .ToListAsync(cancellationToken);

    private async Task<bool> GenerationConfiguredAsync(
        Guid projectId,
        bool requireEnterpriseProjectId,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.ProjectSettings.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId)
            .Select(candidate => new
            {
                candidate.GeminiEnterpriseProjectId,
                candidate.GeminiAgentPlatformApiKey
            })
            .SingleOrDefaultAsync(cancellationToken);
        return (!requireEnterpriseProjectId
                || !string.IsNullOrWhiteSpace(settings?.GeminiEnterpriseProjectId))
            && !string.IsNullOrWhiteSpace(settings?.GeminiAgentPlatformApiKey);
    }

    private Task<int> ResumeSceneAsync(
        ContentVideoScene scene,
        ContentVideoSceneStatus nextStatus,
        DateTime now,
        CancellationToken cancellationToken) =>
        SceneCas(scene)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, nextStatus)
                .SetProperty(candidate => candidate.GenerationStartedAtUtc, now)
                .SetProperty(candidate => candidate.TransientRetryCount, 0)
                .SetProperty(candidate => candidate.SubmissionClaimToken, (Guid?)null)
                .SetProperty(candidate => candidate.NextAttemptAtUtc, now)
                .SetProperty(candidate => candidate.ProviderPolledAtUtc, (DateTime?)null)
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);

    private Task<int> ResetSceneForFreshSubmissionAsync(
        ContentVideoScene scene,
        ContentVideoSceneStatus nextStatus,
        DateTime now,
        CancellationToken cancellationToken) =>
        SceneCas(scene)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, nextStatus)
                .SetProperty(candidate => candidate.ProviderInteractionId, (string?)null)
                .SetProperty(candidate => candidate.ProviderProjectId, (string?)null)
                .SetProperty(candidate => candidate.GenerationStartedAtUtc, (DateTime?)null)
                .SetProperty(candidate => candidate.TransientRetryCount, 0)
                .SetProperty(candidate => candidate.SubmissionClaimToken, (Guid?)null)
                .SetProperty(candidate => candidate.NextAttemptAtUtc, now)
                .SetProperty(candidate => candidate.ProviderSubmittedAtUtc, (DateTime?)null)
                .SetProperty(candidate => candidate.ProviderPolledAtUtc, (DateTime?)null)
                .SetProperty(candidate => candidate.VideoObjectKey, (string?)null)
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);

    private IQueryable<ContentVideoScene> SceneCas(ContentVideoScene scene) =>
        dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == scene.ProjectId
                && candidate.ContentVideoId == scene.ContentVideoId
                && candidate.Id == scene.Id
                && candidate.Status == scene.Status
                && candidate.ProviderInteractionId == scene.ProviderInteractionId
                && candidate.ProviderProjectId == scene.ProviderProjectId
                && candidate.SubmissionClaimToken == scene.SubmissionClaimToken
                && candidate.NextAttemptAtUtc == scene.NextAttemptAtUtc
                && candidate.TransientRetryCount == scene.TransientRetryCount
                && candidate.UpdatedAt == scene.UpdatedAt);

    private static string? ValidatePlanRequest(PlanContentVideoRequest request)
    {
        if (request.Brief?.Length > 2_000) return "توجيه الفكرة لا يمكن أن يتجاوز 2000 حرف.";
        if (!ContentVideoCapabilities.AspectRatios.Contains(request.AspectRatio)) return "مقاس الفيديو غير مدعوم.";
        if (!ContentVideoCapabilities.Resolutions.Contains(request.Resolution)) return "دقة الفيديو غير مدعومة.";
        if (request.SceneCount is < ContentVideoCapabilities.MinimumSceneCount
            or > ContentVideoCapabilities.MaximumSceneCount)
            return "عدد المشاهد يجب أن يكون من 3 إلى 6.";
        if (request.DurationSeconds is < ContentVideoCapabilities.MinimumDurationSeconds
            or > ContentVideoCapabilities.MaximumDurationSeconds)
            return "مدة المشهد يجب أن تكون من 3 إلى 10 ثوانٍ.";
        return null;
    }

    private static object ReadinessResponse(ContentVideoReadiness readiness) => new
    {
        configured = readiness.Configured,
        enterpriseProjectId = readiness.EnterpriseProjectId,
        geminiApiKeyConfigured = readiness.GeminiApiKeyConfigured,
        geminiAgentPlatformApiKeyConfigured = readiness.GeminiAgentPlatformApiKeyConfigured,
        knowledgeDocumentCount = readiness.KnowledgeDocumentCount,
        model = ContentVideoCapabilities.Model,
        supportedAspectRatios = new[] { "9:16", "16:9" },
        supportedResolutions = new[] { "360p", "720p", "1080p" },
        minimumSceneCount = ContentVideoCapabilities.MinimumSceneCount,
        maximumSceneCount = ContentVideoCapabilities.MaximumSceneCount,
        minimumDurationSeconds = ContentVideoCapabilities.MinimumDurationSeconds,
        maximumDurationSeconds = ContentVideoCapabilities.MaximumDurationSeconds,
        reason = readiness.Reason
    };

    private static object VideoResponse(ContentVideo video, int sceneCount, int completedSceneCount) => new
    {
        id = video.Id,
        status = video.Status.ToString(),
        ideaTitle = video.IdeaTitle,
        hook = video.Hook,
        summary = video.Summary,
        caption = video.Caption,
        aspectRatio = video.AspectRatio,
        resolution = video.Resolution,
        sceneCount,
        requestedSceneCount = video.RequestedSceneCount,
        requestedSceneDurationSeconds = video.RequestedSceneDurationSeconds,
        completedSceneCount,
        knowledgeDocumentCount = video.KnowledgeDocumentCount,
        knowledgeWasTruncated = video.KnowledgeWasTruncated,
        error = video.Error,
        createdAt = video.CreatedAt,
        updatedAt = video.UpdatedAt,
        finalVideoUrl = string.IsNullOrWhiteSpace(video.FinalVideoObjectKey)
            ? null
            : $"/api/content/videos/{video.Id}/file"
    };

    private static object VideoDetailResponse(ContentVideo video, IReadOnlyList<ContentVideoScene> scenes) => new
    {
        id = video.Id,
        status = video.Status.ToString(),
        brief = video.Brief,
        ideaTitle = video.IdeaTitle,
        hook = video.Hook,
        summary = video.Summary,
        caption = video.Caption,
        aspectRatio = video.AspectRatio,
        resolution = video.Resolution,
        sceneCount = scenes.Count,
        requestedSceneCount = video.RequestedSceneCount,
        requestedSceneDurationSeconds = video.RequestedSceneDurationSeconds,
        completedSceneCount = scenes.Count(scene => scene.Status == ContentVideoSceneStatus.Completed),
        knowledgeDocumentCount = video.KnowledgeDocumentCount,
        knowledgeWasTruncated = video.KnowledgeWasTruncated,
        plannerModel = video.PlannerModel,
        videoModel = video.VideoModel,
        error = video.Error,
        createdAt = video.CreatedAt,
        updatedAt = video.UpdatedAt,
        finalVideoUrl = string.IsNullOrWhiteSpace(video.FinalVideoObjectKey)
            ? null
            : $"/api/content/videos/{video.Id}/file",
        scenes = scenes.Select(scene => new
        {
            id = scene.Id,
            sceneIndex = scene.SceneIndex,
            title = scene.Title,
            narrative = scene.Narrative,
            visualPrompt = scene.VisualPrompt,
            audioPrompt = scene.AudioPrompt,
            transitionPrompt = scene.TransitionPrompt,
            durationSeconds = scene.DurationSeconds,
            status = scene.Status.ToString(),
            error = scene.Error,
            videoUrl = string.IsNullOrWhiteSpace(scene.VideoObjectKey)
                ? null
                : $"/api/content/videos/{video.Id}/scenes/{scene.Id}/file"
        })
    };
}

public sealed class PlanContentVideoRequest
{
    public string? Brief { get; set; }
    public string AspectRatio { get; set; } = "9:16";
    public string Resolution { get; set; } = "720p";
    public int SceneCount { get; set; } = 4;
    public int DurationSeconds { get; set; } = 6;
}

public sealed class RetryContentVideoSceneRequest
{
    public bool ConfirmPossibleDuplicate { get; set; }
}
