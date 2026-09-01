using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Content.Domain;
using Modules.Content.Services;
using Shared.Infrastructure;
using Shared.Security;
using Shared.Storage;

namespace Modules.Content.Jobs;

public sealed class ContentVideoJob(
    AppDbContext dbContext,
    ContentVideoPlanningService planningService,
    GeminiOmniVideoClient omniClient,
    ContentVideoMediaService mediaService,
    IObjectStorage objectStorage,
    IProjectSecretVault secretVault,
    ContentVideoDispatchService dispatch,
    ILogger<ContentVideoJob> logger)
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumTransientRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumInteractionAge = TimeSpan.FromHours(2);
    private static readonly TimeSpan StaleWorkAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SubmissionLease = TimeSpan.FromMinutes(10);
    private const int RecoveryBatchSize = 50;

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    public async Task PlanAsync(Guid projectId, Guid videoId)
    {
        try
        {
            await planningService.PlanAsync(projectId, videoId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            dbContext.ChangeTracker.Clear();
            await MarkPlanningFailedAsync(projectId, videoId, exception, CancellationToken.None);
            logger.LogWarning(
                "Content video planning failed for {ProjectId}/{VideoId}: {ErrorCode}",
                projectId,
                videoId,
                ContentVideoErrors.Code(exception));
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task GenerateNextSceneAsync(Guid projectId, Guid videoId)
    {
        ContentVideoScene? activeScene = null;
        try
        {
            var video = await VideoAsync(projectId, videoId, CancellationToken.None);
            if (video is null || video.Status != ContentVideoStatus.Generating) return;
            var scenes = await ScenesAsync(projectId, videoId, CancellationToken.None);
            activeScene = scenes.FirstOrDefault(scene => scene.Status != ContentVideoSceneStatus.Completed);
            if (activeScene is null)
            {
                dbContext.ChangeTracker.Clear();
                if (await TryTransitionToAssemblyAsync(projectId, videoId, CancellationToken.None))
                    dispatch.EnqueueAssembly(projectId, videoId);
                return;
            }

            if (activeScene.Status is ContentVideoSceneStatus.Failed
                or ContentVideoSceneStatus.SubmissionUncertain
                or ContentVideoSceneStatus.RecoveryRequired)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (activeScene.Status == ContentVideoSceneStatus.Submitting
                && string.IsNullOrWhiteSpace(activeScene.ProviderInteractionId))
            {
                if (activeScene.NextAttemptAtUtc is DateTime leaseExpiresAt && leaseExpiresAt > now)
                    return;
                var observed = ProviderState(activeScene);
                dbContext.ChangeTracker.Clear();
                await MarkSubmissionUncertainAsync(
                    projectId,
                    videoId,
                    activeScene.Id,
                    observed,
                    new GeminiOmniSubmissionUncertainException(
                        "OMNI_SUBMISSION_INTERRUPTED",
                        "انقطع تنفيذ طلب المشهد قبل حفظ معرّف Gemini؛ يلزم تأكيد يدوي قبل إعادة المحاولة."),
                    CancellationToken.None);
                return;
            }

            if ((activeScene.Status is ContentVideoSceneStatus.Submitted
                    or ContentVideoSceneStatus.Generating)
                && string.IsNullOrWhiteSpace(activeScene.ProviderInteractionId))
            {
                var observed = ProviderState(activeScene);
                dbContext.ChangeTracker.Clear();
                await MarkSubmissionUncertainAsync(
                    projectId,
                    videoId,
                    activeScene.Id,
                    observed,
                    new GeminiOmniSubmissionUncertainException(
                        "OMNI_INTERACTION_ID_NOT_PERSISTED",
                        "حالة المشهد تشير إلى إرسال سابق دون معرّف Gemini؛ يلزم تأكيد يدوي قبل إعادة المحاولة."),
                    CancellationToken.None);
                return;
            }

            if (activeScene.NextAttemptAtUtc is DateTime nextAttemptAt && nextAttemptAt > now)
                return;

            var credentials = await GenerationCredentialsAsync(projectId, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(activeScene.ProviderInteractionId))
            {
                if (string.IsNullOrWhiteSpace(credentials.EnterpriseProjectId))
                    throw new ContentVideoException(
                        "OMNI_PROJECT_ID_MISSING",
                        "أضف Google Cloud Project ID في إعدادات المشروع.");
                var claimToken = Guid.NewGuid();
                var claimedSceneId = activeScene.Id;
                dbContext.ChangeTracker.Clear();
                if (!await TryClaimSubmissionAsync(
                        projectId,
                        videoId,
                        activeScene,
                        credentials.EnterpriseProjectId,
                        claimToken,
                        CancellationToken.None))
                {
                    return;
                }

                video = await VideoAsync(projectId, videoId, CancellationToken.None);
                scenes = await ScenesAsync(projectId, videoId, CancellationToken.None);
                activeScene = scenes.SingleOrDefault(scene => scene.Id == claimedSceneId);
                if (video is null
                    || activeScene?.SubmissionClaimToken != claimToken
                    || activeScene.Status != ContentVideoSceneStatus.Submitting)
                {
                    return;
                }
                await SubmitSceneAsync(video, activeScene, scenes, credentials, CancellationToken.None);
                return;
            }

            await PollSceneAsync(
                video,
                activeScene,
                credentials.AgentPlatformApiKey,
                CancellationToken.None);
        }
        catch (GeminiOmniRetryableException exception)
        {
            var observed = ProviderState(activeScene);
            dbContext.ChangeTracker.Clear();
            if (activeScene is null) throw;
            await ScheduleTransientRetryAsync(
                projectId,
                videoId,
                activeScene.Id,
                observed,
                exception,
                CancellationToken.None);
            logger.LogWarning(
                "Content video request will retry for {ProjectId}/{VideoId}: {ErrorCode}",
                projectId,
                videoId,
                ContentVideoErrors.Code(exception));
        }
        catch (GeminiOmniSubmissionUncertainException exception)
        {
            var observed = ProviderState(activeScene, exception.InteractionId);
            dbContext.ChangeTracker.Clear();
            if (activeScene is null) throw;
            if (!string.IsNullOrWhiteSpace(observed.InteractionId))
            {
                await MarkRecoveryRequiredAsync(
                    projectId,
                    videoId,
                    activeScene.Id,
                    observed,
                    exception,
                    CancellationToken.None);
            }
            else
            {
                await MarkSubmissionUncertainAsync(
                    projectId,
                    videoId,
                    activeScene.Id,
                    observed,
                    exception,
                    CancellationToken.None);
            }
            logger.LogWarning(
                "Content video submission outcome requires recovery for {ProjectId}/{VideoId}: {ErrorCode}",
                projectId,
                videoId,
                ContentVideoErrors.Code(exception));
        }
        catch (Exception exception)
        {
            var observed = ProviderState(activeScene);
            var providerTerminal = exception as ProviderTerminalPersistenceException;
            dbContext.ChangeTracker.Clear();
            await MarkGenerationFailedAsync(
                projectId,
                videoId,
                activeScene?.Id,
                observed,
                providerTerminal?.SafeError,
                exception,
                CancellationToken.None);
            logger.LogWarning(
                "Content video scene generation failed for {ProjectId}/{VideoId}: {ErrorCode}",
                projectId,
                videoId,
                ContentVideoErrors.Code(exception));
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task AssembleAsync(Guid projectId, Guid videoId)
    {
        try
        {
            var video = await VideoAsync(projectId, videoId, CancellationToken.None);
            if (video is null || video.Status != ContentVideoStatus.Assembling) return;
            var scenes = await ScenesAsync(projectId, videoId, CancellationToken.None);
            var objectKey = await mediaService.AssembleAndStoreAsync(
                projectId,
                videoId,
                scenes,
                CancellationToken.None);

            dbContext.ChangeTracker.Clear();
            var now = DateTime.UtcNow;
            await dbContext.ContentVideos.IgnoreQueryFilters()
                .Where(candidate => candidate.ProjectId == projectId
                    && candidate.Id == videoId
                    && candidate.Status == ContentVideoStatus.Assembling)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(candidate => candidate.FinalVideoObjectKey, objectKey)
                    .SetProperty(candidate => candidate.FinalVideoMimeType, "video/mp4")
                    .SetProperty(candidate => candidate.Status, ContentVideoStatus.Ready)
                    .SetProperty(candidate => candidate.Error, (string?)null)
                    .SetProperty(candidate => candidate.CompletedAtUtc, now)
                    .SetProperty(candidate => candidate.UpdatedAt, now));
        }
        catch (Exception exception)
        {
            dbContext.ChangeTracker.Clear();
            await MarkAssemblyFailedAsync(projectId, videoId, exception, CancellationToken.None);
            logger.LogWarning(
                "Content video assembly failed for {ProjectId}/{VideoId}: {ErrorCode}",
                projectId,
                videoId,
                ContentVideoErrors.Code(exception));
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 55)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RecoverAsync()
    {
        var recoveryNow = DateTime.UtcNow;
        var cutoff = recoveryNow - StaleWorkAge;
        var staleVideoCandidates =
            from video in dbContext.ContentVideos.IgnoreQueryFilters().AsNoTracking()
            let firstIncompleteSceneDueAt = dbContext.ContentVideoScenes.IgnoreQueryFilters()
                .Where(scene => scene.ProjectId == video.ProjectId
                    && scene.ContentVideoId == video.Id
                    && scene.Status != ContentVideoSceneStatus.Completed)
                .OrderBy(scene => scene.SceneIndex)
                .Select(scene => scene.NextAttemptAtUtc)
                .FirstOrDefault()
            where video.UpdatedAt < cutoff
                && (video.Status == ContentVideoStatus.Planning
                    || video.Status == ContentVideoStatus.Generating
                    || video.Status == ContentVideoStatus.Assembling)
                && (video.Status != ContentVideoStatus.Generating
                    || firstIncompleteSceneDueAt == null
                    || firstIncompleteSceneDueAt <= recoveryNow)
            orderby video.UpdatedAt, video.ProjectId, video.Id
            select new StaleVideo(video.ProjectId, video.Id, video.Status, video.UpdatedAt);
        var staleVideos = await staleVideoCandidates
            .Take(RecoveryBatchSize)
            .ToListAsync();

        foreach (var stale in staleVideos)
        {
            var claimedAt = DateTime.UtcNow;
            var claimed = stale.Status == ContentVideoStatus.Generating
                ? await TryClaimGenerationRecoveryAsync(stale, cutoff, claimedAt)
                : await TryClaimVideoRecoveryAsync(stale, cutoff, claimedAt);
            if (!claimed) continue;

            switch (stale.Status)
            {
                case ContentVideoStatus.Planning:
                    dispatch.EnqueuePlan(stale.ProjectId, stale.VideoId);
                    break;
                case ContentVideoStatus.Generating:
                    dispatch.EnqueueGeneration(stale.ProjectId, stale.VideoId);
                    break;
                case ContentVideoStatus.Assembling:
                    dispatch.EnqueueAssembly(stale.ProjectId, stale.VideoId);
                    break;
            }
        }
    }

    private async Task<bool> TryClaimGenerationRecoveryAsync(
        StaleVideo stale,
        DateTime cutoff,
        DateTime claimedAt)
    {
        var activeScene = await dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(scene => scene.ProjectId == stale.ProjectId
                && scene.ContentVideoId == stale.VideoId
                && scene.Status != ContentVideoSceneStatus.Completed)
            .OrderBy(scene => scene.SceneIndex)
            .FirstOrDefaultAsync();
        if (activeScene is null)
            return await TryClaimVideoRecoveryAsync(stale, cutoff, claimedAt);
        if (activeScene.NextAttemptAtUtc is DateTime dueAt && dueAt > claimedAt)
            return false;

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var sceneClaimed = await SceneCas(activeScene)
            .Where(scene => scene.NextAttemptAtUtc == null || scene.NextAttemptAtUtc <= claimedAt)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(scene => scene.NextAttemptAtUtc, claimedAt)
                .SetProperty(scene => scene.UpdatedAt, claimedAt));
        if (sceneClaimed != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        var videoClaimed = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(video => video.ProjectId == stale.ProjectId
                && video.Id == stale.VideoId
                && video.Status == stale.Status
                && video.UpdatedAt == stale.UpdatedAt
                && video.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(updates => updates.SetProperty(video => video.UpdatedAt, claimedAt));
        if (videoClaimed != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await transaction.CommitAsync();
        return true;
    }

    private async Task<bool> TryClaimVideoRecoveryAsync(
        StaleVideo stale,
        DateTime cutoff,
        DateTime claimedAt) =>
        await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(video => video.ProjectId == stale.ProjectId
                && video.Id == stale.VideoId
                && video.Status == stale.Status
                && video.UpdatedAt == stale.UpdatedAt
                && video.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(updates => updates.SetProperty(video => video.UpdatedAt, claimedAt)) == 1;

    private async Task<bool> TryClaimSubmissionAsync(
        Guid projectId,
        Guid videoId,
        ContentVideoScene scene,
        string providerProjectId,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        if (scene.Status is not (ContentVideoSceneStatus.Planned or ContentVideoSceneStatus.Queued)
            || !string.IsNullOrWhiteSpace(scene.ProviderInteractionId)
            || scene.SubmissionClaimToken is not null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var leaseExpiresAt = now + SubmissionLease;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var sceneClaimed = await SceneCas(scene)
            .Where(candidate => candidate.ProviderInteractionId == null
                && candidate.SubmissionClaimToken == null
                && (candidate.NextAttemptAtUtc == null || candidate.NextAttemptAtUtc <= now))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, ContentVideoSceneStatus.Submitting)
                .SetProperty(candidate => candidate.ProviderProjectId, providerProjectId)
                .SetProperty(candidate => candidate.SubmissionClaimToken, claimToken)
                .SetProperty(candidate => candidate.GenerationStartedAtUtc,
                    scene.GenerationStartedAtUtc ?? now)
                .SetProperty(candidate => candidate.NextAttemptAtUtc, leaseExpiresAt)
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (sceneClaimed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var videoClaimed = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.Id == videoId
                && candidate.Status == ContentVideoStatus.Generating)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (videoClaimed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task SubmitSceneAsync(
        ContentVideo video,
        ContentVideoScene scene,
        IReadOnlyList<ContentVideoScene> scenes,
        GenerationCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scene.ProviderProjectId)
            || scene.SubmissionClaimToken is not Guid claimToken)
            throw new ContentVideoException(
                "OMNI_SUBMISSION_CLAIM_MISSING",
                "تعذر حجز المشهد للتوليد بأمان.");

        byte[]? firstFrame = null;
        if (scene.SceneIndex > 0)
        {
            var previousScene = scenes.SingleOrDefault(
                candidate => candidate.SceneIndex == scene.SceneIndex - 1);
            if (previousScene?.Status != ContentVideoSceneStatus.Completed)
                throw new ContentVideoException("PREVIOUS_SCENE_MISSING", "المشهد السابق غير مكتمل.");
            firstFrame = await mediaService.ExtractLastFrameAsync(previousScene, cancellationToken);
        }

        EnsureWithinGenerationWindow(scene, DateTime.UtcNow);
        if (!await TryRenewSubmissionClaimAsync(
                video,
                scene,
                claimToken,
                cancellationToken))
        {
            return;
        }

        var interaction = await omniClient.SubmitAsync(
            new GeminiOmniVideoRequest(
                scene.ProviderProjectId,
                BuildScenePrompt(video, scene, scenes.Count, firstFrame is not null),
                video.AspectRatio,
                video.Resolution,
                scene.DurationSeconds,
                credentials.AgentPlatformApiKey,
                firstFrame),
            cancellationToken);

        scene.ProviderInteractionId = interaction.InteractionId;
        var terminalError = interaction.RequiresAction
            ? "طلب Gemini إجراءً إضافيًا غير مدعوم لهذا النوع من التوليد."
            : interaction.IsTerminalFailure
                ? "تعذر على Gemini توليد هذا المشهد."
                : null;
        if (terminalError is null
            && !interaction.IsCompleted
            && !interaction.IsPending)
        {
            throw UnknownInteractionStatus();
        }

        bool responsePersisted;
        try
        {
            responsePersisted = await PersistSubmissionResponseAsync(
                video,
                scene,
                claimToken,
                interaction,
                terminalError,
                cancellationToken);
        }
        catch (Exception exception) when (terminalError is not null)
        {
            throw new ProviderTerminalPersistenceException(terminalError, exception);
        }
        if (!responsePersisted)
        {
            var claimLost = new ContentVideoException(
                "OMNI_SUBMISSION_CLAIM_LOST",
                "استلم Gemini المشهد لكن تعذر تثبيت نتيجة الإرسال؛ يلزم استكماله بأمان.");
            if (terminalError is not null)
                throw new ProviderTerminalPersistenceException(terminalError, claimLost);
            throw claimLost;
        }

        if (terminalError is not null) return;
        if (interaction.IsPending)
        {
            dispatch.ScheduleGeneration(video.ProjectId, video.Id, PollDelay);
            return;
        }

        dbContext.ChangeTracker.Clear();
        var durableVideo = await VideoAsync(video.ProjectId, video.Id, cancellationToken);
        var durableScene = await dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == video.ProjectId
                && candidate.ContentVideoId == video.Id
                && candidate.Id == scene.Id,
                cancellationToken);
        if (durableVideo is null
            || durableScene is null
            || durableScene.ProviderInteractionId != interaction.InteractionId)
        {
            throw new ContentVideoException(
                "OMNI_SUBMISSION_STATE_MISSING",
                "تم حفظ طلب Gemini لكن تعذر تحميل حالة المشهد لاستكماله.");
        }
        await CompleteSceneAsync(durableVideo, durableScene, interaction, cancellationToken);
    }

    private async Task<bool> TryRenewSubmissionClaimAsync(
        ContentVideo video,
        ContentVideoScene scene,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseExpiresAt = now + SubmissionLease;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var sceneRenewed = await SceneCas(scene)
            .Where(candidate => candidate.Status == ContentVideoSceneStatus.Submitting
                && candidate.ProviderInteractionId == null
                && candidate.ProviderProjectId == scene.ProviderProjectId
                && candidate.SubmissionClaimToken == claimToken)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.NextAttemptAtUtc, leaseExpiresAt)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (sceneRenewed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var videoRenewed = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == video.ProjectId
                && candidate.Id == video.Id
                && candidate.Status == ContentVideoStatus.Generating)
            .ExecuteUpdateAsync(updates => updates.SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (videoRenewed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        scene.NextAttemptAtUtc = leaseExpiresAt;
        scene.UpdatedAt = now;
        video.UpdatedAt = now;
        return true;
    }

    private async Task<bool> PersistSubmissionResponseAsync(
        ContentVideo video,
        ContentVideoScene claimedScene,
        Guid claimToken,
        GeminiOmniInteraction interaction,
        string? terminalError,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var nextSceneStatus = terminalError is not null
            ? ContentVideoSceneStatus.Failed
            : interaction.IsCompleted
                ? ContentVideoSceneStatus.Generating
                : ContentVideoSceneStatus.Submitted;
        DateTime? nextAttemptAt = terminalError is not null
            ? null
            : interaction.IsCompleted
                ? now + SubmissionLease
                : now + PollDelay;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var scenePersisted = await dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == claimedScene.ProjectId
                && candidate.ContentVideoId == claimedScene.ContentVideoId
                && candidate.Id == claimedScene.Id
                && candidate.Status == ContentVideoSceneStatus.Submitting
                && candidate.ProviderInteractionId == null
                && candidate.ProviderProjectId == claimedScene.ProviderProjectId
                && candidate.SubmissionClaimToken == claimToken
                && candidate.NextAttemptAtUtc == claimedScene.NextAttemptAtUtc
                && candidate.TransientRetryCount == claimedScene.TransientRetryCount
                && candidate.UpdatedAt == claimedScene.UpdatedAt)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, nextSceneStatus)
                .SetProperty(candidate => candidate.ProviderInteractionId, interaction.InteractionId)
                .SetProperty(candidate => candidate.SubmissionClaimToken, (Guid?)null)
                .SetProperty(candidate => candidate.NextAttemptAtUtc, nextAttemptAt)
                .SetProperty(candidate => candidate.ProviderSubmittedAtUtc, now)
                .SetProperty(candidate => candidate.ProviderPolledAtUtc, (DateTime?)null)
                .SetProperty(candidate => candidate.Error, terminalError)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (scenePersisted != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var nextVideoStatus = terminalError is null
            ? ContentVideoStatus.Generating
            : ContentVideoStatus.GenerationFailed;
        var videoPersisted = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == video.ProjectId
                && candidate.Id == video.Id
                && candidate.Status == ContentVideoStatus.Generating)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, nextVideoStatus)
                .SetProperty(candidate => candidate.Error, terminalError)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (videoPersisted != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task PollSceneAsync(
        ContentVideo video,
        ContentVideoScene scene,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        EnsureWithinGenerationWindow(scene, DateTime.UtcNow);
        var providerProjectId = scene.ProviderProjectId
            ?? throw new ContentVideoException(
                "OMNI_PROVIDER_PROJECT_MISSING",
                "تعذر تحديد مشروع Google Cloud المستخدم لبدء هذا المشهد.");
        var interaction = await omniClient.GetAsync(
            providerProjectId,
            scene.ProviderInteractionId!,
            apiKey,
            cancellationToken);
        if (interaction.RequiresAction)
        {
            await PersistProviderTerminalFailureAsync(
                video,
                scene,
                "طلب Gemini إجراءً إضافيًا غير مدعوم لهذا النوع من التوليد.",
                cancellationToken);
            return;
        }
        if (interaction.IsTerminalFailure)
        {
            await PersistProviderTerminalFailureAsync(
                video,
                scene,
                "تعذر على Gemini توليد هذا المشهد.",
                cancellationToken);
            return;
        }
        if (interaction.IsCompleted)
        {
            await CompleteSceneAsync(video, scene, interaction, cancellationToken);
            return;
        }
        if (!interaction.IsPending) throw UnknownInteractionStatus();

        if (await TryPersistPendingPollAsync(video, scene, cancellationToken))
            dispatch.ScheduleGeneration(video.ProjectId, video.Id, PollDelay);
    }

    private async Task<bool> TryPersistPendingPollAsync(
        ContentVideo video,
        ContentVideoScene scene,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var scenePersisted = await SceneCas(scene)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, ContentVideoSceneStatus.Generating)
                .SetProperty(candidate => candidate.SubmissionClaimToken, (Guid?)null)
                .SetProperty(candidate => candidate.NextAttemptAtUtc, now + PollDelay)
                .SetProperty(candidate => candidate.ProviderPolledAtUtc, now)
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (scenePersisted != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var videoPersisted = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == video.ProjectId
                && candidate.Id == video.Id
                && candidate.Status == ContentVideoStatus.Generating)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (videoPersisted != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task CompleteSceneAsync(
        ContentVideo video,
        ContentVideoScene scene,
        GeminiOmniInteraction interaction,
        CancellationToken cancellationToken)
    {
        var videoBytes = interaction.VideoBytes
            ?? throw new ContentVideoException("OMNI_VIDEO_DATA_MISSING", "ملف المشهد المولد غير موجود.");
        var objectKey = $"content/{video.ProjectId:N}/videos/{video.Id:N}/scenes/{scene.SceneIndex + 1:D2}-{scene.Id:N}.mp4";
        await using var content = new MemoryStream(videoBytes, writable: false);
        await objectStorage.UploadAsync(
            objectKey,
            content,
            interaction.VideoMimeType ?? "video/mp4",
            cancellationToken);

        if (await TryPersistCompletedSceneAsync(
                video,
                scene,
                objectKey,
                interaction.VideoMimeType ?? "video/mp4",
                cancellationToken))
        {
            dispatch.EnqueueGeneration(video.ProjectId, video.Id);
        }
    }

    private async Task<bool> TryPersistCompletedSceneAsync(
        ContentVideo video,
        ContentVideoScene scene,
        string objectKey,
        string mimeType,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var scenePersisted = await SceneCas(scene)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.VideoObjectKey, objectKey)
                .SetProperty(candidate => candidate.VideoMimeType, mimeType)
                .SetProperty(candidate => candidate.Status, ContentVideoSceneStatus.Completed)
                .SetProperty(candidate => candidate.SubmissionClaimToken, (Guid?)null)
                .SetProperty(candidate => candidate.NextAttemptAtUtc, (DateTime?)null)
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.ProviderPolledAtUtc, now)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (scenePersisted != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var videoPersisted = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == video.ProjectId
                && candidate.Id == video.Id
                && candidate.Status == ContentVideoStatus.Generating)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (videoPersisted != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task PersistProviderTerminalFailureAsync(
        ContentVideo video,
        ContentVideoScene scene,
        string error,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var scenePersisted = await SceneCas(scene)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(candidate => candidate.Status, ContentVideoSceneStatus.Failed)
                    .SetProperty(candidate => candidate.SubmissionClaimToken, (Guid?)null)
                    .SetProperty(candidate => candidate.NextAttemptAtUtc, (DateTime?)null)
                    .SetProperty(candidate => candidate.ProviderPolledAtUtc, now)
                    .SetProperty(candidate => candidate.Error, error)
                    .SetProperty(candidate => candidate.UpdatedAt, now),
                    cancellationToken);
            if (scenePersisted != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var videoPersisted = await dbContext.ContentVideos.IgnoreQueryFilters()
                .Where(candidate => candidate.ProjectId == video.ProjectId
                    && candidate.Id == video.Id
                    && candidate.Status == ContentVideoStatus.Generating)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(candidate => candidate.Status, ContentVideoStatus.GenerationFailed)
                    .SetProperty(candidate => candidate.Error, error)
                    .SetProperty(candidate => candidate.UpdatedAt, now),
                    cancellationToken);
            if (videoPersisted != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw new ProviderTerminalPersistenceException(error, exception);
        }
    }

    private async Task<GenerationCredentials> GenerationCredentialsAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.ProjectSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId, cancellationToken)
            ?? throw new ContentVideoException("PROJECT_SETTINGS_MISSING", "إعدادات المشروع غير موجودة.");
        return new GenerationCredentials(
            settings.GeminiEnterpriseProjectId,
            secretVault.Unprotect(projectId, settings.GeminiAgentPlatformApiKey));
    }

    private static string BuildScenePrompt(
        ContentVideo video,
        ContentVideoScene scene,
        int sceneCount,
        bool hasFirstFrame) =>
        $$"""
          Generate scene {{scene.SceneIndex + 1}} of {{sceneCount}} for one coherent marketing video.
          Idea: {{video.IdeaTitle}}
          Hook: {{video.Hook}}
          Scene purpose: {{scene.Narrative}}
          Visual direction: {{scene.VisualPrompt}}
          Audio, dialogue, music, and effects: {{scene.AudioPrompt}}
          End transition: {{scene.TransitionPrompt}}
          Duration: exactly {{scene.DurationSeconds}} seconds. Aspect ratio: {{video.AspectRatio}}.
          {{(hasFirstFrame ? "Use the supplied image as the exact first frame and preserve its subjects, palette, lighting, camera direction, and spatial continuity." : "Establish a distinctive, production-quality opening visual style that later scenes can continue.")}}
          Do not render captions, subtitles, logos, watermarks, UI, or invented product claims. Keep all spoken factual claims within the supplied scene description.
          """;

    private async Task ScheduleTransientRetryAsync(
        Guid projectId,
        Guid videoId,
        Guid sceneId,
        ObservedProviderState observed,
        GeminiOmniRetryableException exception,
        CancellationToken cancellationToken)
    {
        var video = await dbContext.ContentVideos.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId
                && candidate.Id == videoId, cancellationToken);
        var scene = await dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId
                && candidate.ContentVideoId == videoId
                && candidate.Id == sceneId, cancellationToken);
        if (video is null || scene is null || video.Status != ContentVideoStatus.Generating)
        {
            LogIgnoredStaleExceptionTransition(projectId, videoId, sceneId, observed);
            return;
        }

        var now = DateTime.UtcNow;
        var effectiveInteractionId = observed.InteractionId;
        var effectiveProjectId = observed.ProjectId;
        var delay = NextTransientRetryDelay(scene, exception, now);
        var providerRequestCanBeRecovered = !string.IsNullOrWhiteSpace(effectiveInteractionId)
            && !string.IsNullOrWhiteSpace(effectiveProjectId);
        var nextStatus = delay is null
            ? providerRequestCanBeRecovered
                ? ContentVideoSceneStatus.RecoveryRequired
                : ContentVideoSceneStatus.Failed
            : string.IsNullOrWhiteSpace(effectiveInteractionId)
                ? ContentVideoSceneStatus.Queued
                : ContentVideoSceneStatus.Generating;
        var error = delay is null
            ? providerRequestCanBeRecovered
                ? "انتهت مهلة المتابعة التلقائية؛ يمكن استكمال طلب Gemini الحالي بأمان."
                : "انتهت مهلة توليد المشهد بعد محاولات اتصال مؤقتة."
            : null;
        var nextAttemptAt = delay is TimeSpan retryDelay ? now + retryDelay : (DateTime?)null;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var sceneUpdated = await ExceptionOriginCas(projectId, videoId, sceneId, observed)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, nextStatus)
                .SetProperty(candidate => candidate.ProviderInteractionId, effectiveInteractionId)
                .SetProperty(candidate => candidate.ProviderProjectId, effectiveProjectId)
                .SetProperty(candidate => candidate.TransientRetryCount, observed.TransientRetryCount + 1)
                .SetProperty(candidate => candidate.SubmissionClaimToken, (Guid?)null)
                .SetProperty(candidate => candidate.NextAttemptAtUtc, nextAttemptAt)
                .SetProperty(candidate => candidate.ProviderPolledAtUtc,
                    string.IsNullOrWhiteSpace(effectiveInteractionId) ? null : now)
                .SetProperty(candidate => candidate.Error, error)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (sceneUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            LogIgnoredStaleExceptionTransition(projectId, videoId, sceneId, observed);
            return;
        }

        var nextVideoStatus = delay is null
            ? ContentVideoStatus.GenerationFailed
            : ContentVideoStatus.Generating;
        var videoUpdated = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.Id == videoId
                && candidate.Status == ContentVideoStatus.Generating)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, nextVideoStatus)
                .SetProperty(candidate => candidate.Error, error)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (videoUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            LogIgnoredStaleExceptionTransition(projectId, videoId, sceneId, observed);
            return;
        }
        await transaction.CommitAsync(cancellationToken);
        if (delay is TimeSpan dispatchedDelay)
            dispatch.ScheduleGeneration(projectId, videoId, dispatchedDelay);
    }

    private static TimeSpan? NextTransientRetryDelay(
        ContentVideoScene scene,
        GeminiOmniRetryableException exception,
        DateTime now)
    {
        if (scene.GenerationStartedAtUtc is not DateTime startedAt) return null;
        var retryCount = scene.TransientRetryCount + 1;
        var exponent = Math.Min(retryCount - 1, 8);
        var exponentialSeconds = Math.Min(
            PollDelay.TotalSeconds * Math.Pow(2, exponent),
            MaximumTransientRetryDelay.TotalSeconds);
        var jitterFactor = 0.8 + (Random.Shared.NextDouble() * 0.2);
        var boundedDelay = TimeSpan.FromSeconds(exponentialSeconds * jitterFactor);
        var delay = exception.RetryAfter is TimeSpan retryAfter && retryAfter > boundedDelay
            ? retryAfter
            : boundedDelay;
        var remainingWindow = startedAt + MaximumInteractionAge - now;
        return delay < remainingWindow ? delay : null;
    }

    private static void EnsureWithinGenerationWindow(ContentVideoScene scene, DateTime now)
    {
        if (scene.GenerationStartedAtUtc is not DateTime startedAt
            || now - startedAt >= MaximumInteractionAge)
        {
            throw new ContentVideoException(
                "OMNI_INTERACTION_TIMEOUT",
                "استغرق توليد المشهد وقتًا أطول من المتوقع. أعد محاولة هذا المشهد.");
        }
    }

    private static ContentVideoException UnknownInteractionStatus() => new(
        "OMNI_STATUS_UNKNOWN",
        "أعاد Gemini حالة توليد غير معروفة؛ تم إيقاف المشهد بأمان.");

    private Task<ContentVideo?> VideoAsync(
        Guid projectId,
        Guid videoId,
        CancellationToken cancellationToken) =>
        dbContext.ContentVideos.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                video => video.ProjectId == projectId && video.Id == videoId,
                cancellationToken);

    private Task<List<ContentVideoScene>> ScenesAsync(
        Guid projectId,
        Guid videoId,
        CancellationToken cancellationToken) =>
        dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .Where(scene => scene.ProjectId == projectId && scene.ContentVideoId == videoId)
            .OrderBy(scene => scene.SceneIndex)
            .ToListAsync(cancellationToken);

    private async Task<bool> TryTransitionToAssemblyAsync(
        Guid projectId,
        Guid videoId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.ContentVideoScenes.IgnoreQueryFilters().AnyAsync(
            scene => scene.ProjectId == projectId
                && scene.ContentVideoId == videoId
                && scene.Status != ContentVideoSceneStatus.Completed,
            cancellationToken))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        return await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(video => video.ProjectId == projectId
                && video.Id == videoId
                && video.Status == ContentVideoStatus.Generating)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(video => video.Status, ContentVideoStatus.Assembling)
                .SetProperty(video => video.Error, (string?)null)
                .SetProperty(video => video.UpdatedAt, now),
                cancellationToken) == 1;
    }

    private async Task MarkPlanningFailedAsync(
        Guid projectId,
        Guid videoId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var error = ContentVideoErrors.Safe(exception);
        await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(video => video.ProjectId == projectId
                && video.Id == videoId
                && video.Status == ContentVideoStatus.Planning)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(video => video.Status, ContentVideoStatus.PlanningFailed)
                .SetProperty(video => video.Error, error)
                .SetProperty(video => video.UpdatedAt, now),
                cancellationToken);
    }

    private async Task MarkGenerationFailedAsync(
        Guid projectId,
        Guid videoId,
        Guid? sceneId,
        ObservedProviderState observed,
        string? providerTerminalError,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (sceneId is not Guid targetSceneId) return;
        var providerTerminal = providerTerminalError is not null;
        var error = providerTerminalError ?? ContentVideoErrors.Safe(exception);
        if (HasOriginFencedInteraction(observed))
        {
            var persisted = await TryPersistOriginFencedInteractionAsync(
                projectId,
                videoId,
                targetSceneId,
                observed,
                providerTerminal
                    ? ContentVideoSceneStatus.Failed
                    : ContentVideoSceneStatus.RecoveryRequired,
                error,
                cancellationToken);
            if (!persisted)
                LogIgnoredLateInteraction(projectId, videoId, targetSceneId, observed);
            return;
        }

        var nextStatus = !providerTerminal && !string.IsNullOrWhiteSpace(observed.InteractionId)
            ? ContentVideoSceneStatus.RecoveryRequired
            : ContentVideoSceneStatus.Failed;
        var transition = new FailedGenerationTransition(
            projectId,
            videoId,
            targetSceneId,
            observed,
            nextStatus,
            observed.InteractionId,
            observed.ProjectId,
            null,
            error);
        if (!await TryUpdateFailedGenerationPairAsync(transition, cancellationToken))
            LogIgnoredStaleExceptionTransition(projectId, videoId, targetSceneId, observed);
    }

    private async Task MarkRecoveryRequiredAsync(
        Guid projectId,
        Guid videoId,
        Guid sceneId,
        ObservedProviderState observed,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (HasOriginFencedInteraction(observed))
        {
            var persisted = await TryPersistOriginFencedInteractionAsync(
                projectId,
                videoId,
                sceneId,
                observed,
                ContentVideoSceneStatus.RecoveryRequired,
                ContentVideoErrors.Safe(exception),
                cancellationToken);
            if (!persisted)
                LogIgnoredLateInteraction(projectId, videoId, sceneId, observed);
            return;
        }

        if (string.IsNullOrWhiteSpace(observed.InteractionId))
        {
            await MarkSubmissionUncertainAsync(
                projectId,
                videoId,
                sceneId,
                observed,
                new GeminiOmniSubmissionUncertainException(
                    "OMNI_SUBMISSION_RESPONSE_UNCERTAIN",
                    ContentVideoErrors.Safe(exception)),
                cancellationToken);
            return;
        }

        var transition = new FailedGenerationTransition(
            projectId,
            videoId,
            sceneId,
            observed,
            ContentVideoSceneStatus.RecoveryRequired,
            observed.InteractionId,
            observed.ProjectId,
            null,
            ContentVideoErrors.Safe(exception));
        if (!await TryUpdateFailedGenerationPairAsync(transition, cancellationToken))
            LogIgnoredStaleExceptionTransition(projectId, videoId, sceneId, observed);
    }

    private async Task MarkSubmissionUncertainAsync(
        Guid projectId,
        Guid videoId,
        Guid sceneId,
        ObservedProviderState observed,
        GeminiOmniSubmissionUncertainException exception,
        CancellationToken cancellationToken)
    {
        var transition = new FailedGenerationTransition(
            projectId,
            videoId,
            sceneId,
            observed,
            ContentVideoSceneStatus.SubmissionUncertain,
            null,
            observed.ProjectId,
            observed.SubmissionClaimToken,
            ContentVideoErrors.Safe(exception));
        if (!await TryUpdateFailedGenerationPairAsync(transition, cancellationToken))
            LogIgnoredStaleExceptionTransition(projectId, videoId, sceneId, observed);
    }

    private async Task<bool> TryPersistOriginFencedInteractionAsync(
        Guid projectId,
        Guid videoId,
        Guid sceneId,
        ObservedProviderState observed,
        ContentVideoSceneStatus sceneStatus,
        string error,
        CancellationToken cancellationToken)
    {
        if (observed.SubmissionClaimToken is not Guid originatingClaimToken
            || string.IsNullOrWhiteSpace(observed.InteractionId))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var sceneUpdated = await dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.ContentVideoId == videoId
                && candidate.Id == sceneId
                && candidate.ProviderInteractionId == null
                && candidate.ProviderProjectId == observed.ProjectId
                && candidate.SubmissionClaimToken == originatingClaimToken
                && (candidate.Status == ContentVideoSceneStatus.Submitting
                    || candidate.Status == ContentVideoSceneStatus.SubmissionUncertain))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, sceneStatus)
                .SetProperty(candidate => candidate.ProviderInteractionId, observed.InteractionId)
                .SetProperty(candidate => candidate.SubmissionClaimToken, (Guid?)null)
                .SetProperty(candidate => candidate.NextAttemptAtUtc, (DateTime?)null)
                .SetProperty(candidate => candidate.Error, error)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (sceneUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var videoUpdated = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.Id == videoId
                && (candidate.Status == ContentVideoStatus.Generating
                    || candidate.Status == ContentVideoStatus.GenerationFailed))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, ContentVideoStatus.GenerationFailed)
                .SetProperty(candidate => candidate.Error, error)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (videoUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> TryUpdateFailedGenerationPairAsync(
        FailedGenerationTransition transition,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var sceneUpdated = await ExceptionOriginCas(
                transition.ProjectId,
                transition.VideoId,
                transition.SceneId,
                transition.Observed)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, transition.SceneStatus)
                .SetProperty(candidate => candidate.ProviderInteractionId, transition.InteractionId)
                .SetProperty(candidate => candidate.ProviderProjectId, transition.ProviderProjectId)
                .SetProperty(candidate => candidate.SubmissionClaimToken,
                    transition.SubmissionClaimToken)
                .SetProperty(candidate => candidate.NextAttemptAtUtc, (DateTime?)null)
                .SetProperty(candidate => candidate.Error, transition.Error)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (sceneUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var videoUpdated = await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == transition.ProjectId
                && candidate.Id == transition.VideoId
                && candidate.Status == ContentVideoStatus.Generating)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, ContentVideoStatus.GenerationFailed)
                .SetProperty(candidate => candidate.Error, transition.Error)
                .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);
        if (videoUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

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

    private IQueryable<ContentVideoScene> ExceptionOriginCas(
        Guid projectId,
        Guid videoId,
        Guid sceneId,
        ObservedProviderState observed)
    {
        var scenes = dbContext.ContentVideoScenes.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.ContentVideoId == videoId
                && candidate.Id == sceneId);
        if (observed.SceneStatus is not ContentVideoSceneStatus sceneStatus
            || observed.UpdatedAt is not DateTime updatedAt
            || !CanTransitionExceptionOrigin(observed, sceneStatus))
        {
            return scenes.Where(_ => false);
        }

        return scenes.Where(candidate => candidate.Status == sceneStatus
            && candidate.ProviderInteractionId == observed.InteractionId
            && candidate.ProviderProjectId == observed.ProjectId
            && candidate.SubmissionClaimToken == observed.SubmissionClaimToken
            && candidate.NextAttemptAtUtc == observed.NextAttemptAtUtc
            && candidate.TransientRetryCount == observed.TransientRetryCount
            && candidate.UpdatedAt == updatedAt);
    }

    private static bool CanTransitionExceptionOrigin(
        ObservedProviderState observed,
        ContentVideoSceneStatus sceneStatus)
    {
        if (observed.SubmissionClaimToken is not null)
            return sceneStatus == ContentVideoSceneStatus.Submitting
                && string.IsNullOrWhiteSpace(observed.InteractionId);
        if (!string.IsNullOrWhiteSpace(observed.InteractionId))
            return sceneStatus is ContentVideoSceneStatus.Submitted
                or ContentVideoSceneStatus.Generating;
        return sceneStatus is ContentVideoSceneStatus.Planned
            or ContentVideoSceneStatus.Queued
            or ContentVideoSceneStatus.Submitting
            or ContentVideoSceneStatus.Submitted
            or ContentVideoSceneStatus.Generating;
    }

    private async Task MarkAssemblyFailedAsync(
        Guid projectId,
        Guid videoId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var error = ContentVideoErrors.Safe(exception);
        await dbContext.ContentVideos.IgnoreQueryFilters()
            .Where(video => video.ProjectId == projectId
                && video.Id == videoId
                && video.Status == ContentVideoStatus.Assembling)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(video => video.Status, ContentVideoStatus.AssemblyFailed)
                .SetProperty(video => video.Error, error)
                .SetProperty(video => video.UpdatedAt, now),
                cancellationToken);
    }

    private static ObservedProviderState ProviderState(
        ContentVideoScene? scene,
        string? interactionId = null) => new(
        interactionId ?? scene?.ProviderInteractionId,
        scene?.ProviderProjectId,
        scene?.SubmissionClaimToken,
        scene?.Status,
        scene?.NextAttemptAtUtc,
        scene?.TransientRetryCount ?? 0,
        scene?.UpdatedAt);

    private static bool HasOriginFencedInteraction(ObservedProviderState observed) =>
        !string.IsNullOrWhiteSpace(observed.InteractionId)
        && observed.SubmissionClaimToken is not null;

    private void LogIgnoredLateInteraction(
        Guid projectId,
        Guid videoId,
        Guid sceneId,
        ObservedProviderState observed) =>
        logger.LogWarning(
            "Ignored late Gemini interaction {InteractionId} for {ProjectId}/{VideoId}/{SceneId} because its submission claim no longer owns the scene.",
            observed.InteractionId,
            projectId,
            videoId,
            sceneId);

    private void LogIgnoredStaleExceptionTransition(
        Guid projectId,
        Guid videoId,
        Guid sceneId,
        ObservedProviderState observed) =>
        logger.LogInformation(
            "Ignored stale content video exception transition for {ProjectId}/{VideoId}/{SceneId} at provider interaction {InteractionId}.",
            projectId,
            videoId,
            sceneId,
            observed.InteractionId);

    private sealed record GenerationCredentials(
        string? EnterpriseProjectId,
        string? AgentPlatformApiKey);
    private sealed record FailedGenerationTransition(
        Guid ProjectId,
        Guid VideoId,
        Guid SceneId,
        ObservedProviderState Observed,
        ContentVideoSceneStatus SceneStatus,
        string? InteractionId,
        string? ProviderProjectId,
        Guid? SubmissionClaimToken,
        string Error);
    private sealed record ObservedProviderState(
        string? InteractionId,
        string? ProjectId,
        Guid? SubmissionClaimToken,
        ContentVideoSceneStatus? SceneStatus,
        DateTime? NextAttemptAtUtc,
        int TransientRetryCount,
        DateTime? UpdatedAt);
    private sealed record StaleVideo(
        Guid ProjectId,
        Guid VideoId,
        ContentVideoStatus Status,
        DateTime UpdatedAt);

    private sealed class ProviderTerminalPersistenceException(string safeError, Exception innerException)
        : Exception(safeError, innerException)
    {
        public string SafeError { get; } = safeError;
    }
}
