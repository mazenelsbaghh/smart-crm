using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Content.Domain;
using Modules.Content.Services;
using Shared.Infrastructure;

namespace Modules.Content.Jobs;

public sealed class ContentAutomationJob
{
    private readonly AppDbContext _dbContext;
    private readonly ContentGenerationService _generation;
    private readonly ContentPublishingService _publishing;
    private readonly ContentWeeklyPlanService _weeklyPlans;
    private readonly ILogger<ContentAutomationJob> _logger;

    public ContentAutomationJob(
        AppDbContext dbContext,
        ContentGenerationService generation,
        ContentPublishingService publishing,
        ContentWeeklyPlanService weeklyPlans,
        ILogger<ContentAutomationJob> logger)
    {
        _dbContext = dbContext;
        _generation = generation;
        _publishing = publishing;
        _weeklyPlans = weeklyPlans;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task PublishDueAsync()
    {
        var now = DateTime.UtcNow;
        await RecoverStalePublicationsAsync(now, CancellationToken.None);
        var projectIds = await _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .Where(settings => settings.IsEnabled
                && settings.HasApprovedStyle
                && settings.NextPublishAtUtc != null
                && settings.NextPublishAtUtc <= now)
            .Select(settings => settings.ProjectId)
            .ToListAsync();
        foreach (var projectId in projectIds)
        {
            await PublishDueProjectAsync(projectId, CancellationToken.None);
        }
    }

    private async Task RecoverStalePublicationsAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var staleBeforeUtc = nowUtc.AddMinutes(-5);
        var stalePosts = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .Where(post => post.Status == ContentPostStatus.Publishing
                && post.UpdatedAt <= staleBeforeUtc)
            .ToListAsync(cancellationToken);
        if (stalePosts.Count == 0) return;

        var affectedProjectIds = stalePosts
            .Select(post => post.ProjectId)
            .Distinct()
            .ToArray();
        var settingsByProject = await _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .Where(settings => affectedProjectIds.Contains(settings.ProjectId))
            .ToDictionaryAsync(settings => settings.ProjectId, cancellationToken);
        const string recoveryMessage =
            "تعذر تأكيد نتيجة نشر سابق بعد انقطاع التنفيذ. راجع صفحة Facebook قبل استئناف الجدول.";
        foreach (var post in stalePosts)
        {
            post.Status = ContentPostStatus.PublishUnknown;
            post.Error = recoveryMessage;
            post.UpdatedAt = nowUtc;
            if (settingsByProject.TryGetValue(post.ProjectId, out var settings))
            {
                StopForUnknownOutcome(settings, recoveryMessage);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    public async Task GenerateSampleAsync(Guid projectId)
    {
        var activeGeneration = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .AnyAsync(post => post.ProjectId == projectId
                && post.IsStyleSample
                && (post.Status == ContentPostStatus.Generating
                    || post.Status == ContentPostStatus.AwaitingApproval));
        if (activeGeneration) return;
        await _generation.GenerateSampleAsync(projectId, CancellationToken.None);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RegenerateSampleAsync(Guid projectId, Guid rejectedPostId)
    {
        var rejected = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(post => post.ProjectId == projectId && post.Id == rejectedPostId);
        if (rejected is not null && rejected.Status == ContentPostStatus.AwaitingApproval)
        {
            rejected.Status = ContentPostStatus.Rejected;
            rejected.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
        await GenerateSampleAsync(projectId);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task GenerateWeeklyPlanAsync(Guid projectId)
    {
        var plan = await _weeklyPlans.GenerateAsync(projectId, CancellationToken.None);
        if (plan.Status is not (ContentWeekPlanStatus.Generating or ContentWeekPlanStatus.AwaitingApproval)) return;
        var generatedPreviews = await GenerateMissingPlanPreviewsAsync(plan, CancellationToken.None);
        if (!generatedPreviews && plan.Status == ContentWeekPlanStatus.AwaitingApproval) return;
        await _weeklyPlans.MarkReadyForReviewAsync(projectId, plan.Id, CancellationToken.None);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RegenerateWeeklyPlanItemAsync(Guid projectId, Guid planId, Guid itemId)
    {
        var item = await _weeklyPlans.RegenerableItemAsync(
            projectId,
            planId,
            itemId,
            CancellationToken.None);
        await RejectLinkedPreviewAsync(projectId, item, CancellationToken.None);
        await GeneratePlanPreviewAsync(item, CancellationToken.None);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ApproveSampleAndStartAsync(Guid projectId, Guid postId)
    {
        var settings = await _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == projectId);
        var post = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId && candidate.Id == postId)
            ?? throw new InvalidOperationException("عينة المحتوى غير موجودة.");
        if (!post.IsStyleSample
            || post.Status is not (ContentPostStatus.AwaitingApproval
                or ContentPostStatus.Approved
                or ContentPostStatus.PublishFailed
                or ContentPostStatus.Published))
            throw new InvalidOperationException("هذه العينة غير جاهزة للاعتماد.");
        if (!string.Equals(post.BrandLogoObjectKey, settings.LogoObjectKey, StringComparison.Ordinal))
            throw new InvalidOperationException("تم تغيير اللوجو بعد إنشاء هذه العينة. ولّد عينة جديدة أولاً.");
        if (!string.Equals(post.BrandStylePrompt, settings.StylePrompt, StringComparison.Ordinal))
            throw new InvalidOperationException("تم تغيير شكل التصميم بعد إنشاء هذه العينة. ولّد عينة جديدة أولاً.");
        if (string.IsNullOrWhiteSpace(settings.FacebookPageId))
            throw new InvalidOperationException("اختر صفحة Facebook قبل اعتماد العينة.");

        if (post.Status == ContentPostStatus.AwaitingApproval)
        {
            post.Status = ContentPostStatus.Approved;
            post.ApprovedAtUtc = DateTime.UtcNow;
            post.UpdatedAt = DateTime.UtcNow;
            settings.ApprovedSamplePostId = post.Id;
            settings.HasApprovedStyle = true;
            settings.IsEnabled = false;
            settings.NextPublishAtUtc = null;
            settings.LastError = null;
            settings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        if (post.Status != ContentPostStatus.Published)
            await _publishing.PublishAsync(projectId, postId, CancellationToken.None);
        await PrepareWeeklyPlanAfterFirstPublishAsync(settings, post);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    public async Task PublishPostAsync(Guid projectId, Guid postId)
    {
        var post = await _publishing.PublishAsync(projectId, postId, CancellationToken.None);
        var settings = await _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == projectId);
        if (post.IsStyleSample)
        {
            await PrepareWeeklyPlanAfterFirstPublishAsync(settings, post);
            return;
        }
        await AdvancePlanForPublishedPostAsync(settings, post, CancellationToken.None);
    }

    private async Task PrepareWeeklyPlanAfterFirstPublishAsync(
        ContentAutomationSettings settings,
        ContentPost post)
    {
        if (post.Status != ContentPostStatus.Published
            || settings.ApprovedSamplePostId != post.Id)
        {
            return;
        }

        settings.IsEnabled = false;
        settings.NextPublishAtUtc = null;
        settings.LastError = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await GenerateWeeklyPlanAsync(settings.ProjectId);
    }

    private async Task PublishDueProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == projectId, cancellationToken);
        if (!settings.IsEnabled || settings.NextPublishAtUtc is null) return;
        ContentWeekPlanItem? dueItem = null;

        try
        {
            var plan = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
                .Where(candidate => candidate.ProjectId == projectId
                    && candidate.Status == ContentWeekPlanStatus.Approved)
                .OrderBy(candidate => candidate.StartDateLocal)
                .ThenBy(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (plan is null)
            {
                StopForUnknownOutcome(settings, "لا توجد خطة أسبوع معتمدة للنشر.");
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var items = await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
                .Where(item => item.ProjectId == projectId && item.PlanId == plan.Id)
                .OrderBy(item => item.DayIndex)
                .ToListAsync(cancellationToken);
            var postIds = items.Where(item => item.ContentPostId != null)
                .Select(item => item.ContentPostId!.Value)
                .ToArray();
            var posts = await _dbContext.ContentPosts.IgnoreQueryFilters()
                .Where(post => post.ProjectId == projectId && postIds.Contains(post.Id))
                .ToDictionaryAsync(post => post.Id, cancellationToken);
            dueItem = items.FirstOrDefault(item => item.ContentPostId is null
                || !posts.TryGetValue(item.ContentPostId.Value, out var linkedPost)
                || linkedPost.Status != ContentPostStatus.Published);
            if (dueItem is null)
            {
                await CompletePlanAsync(plan, settings, cancellationToken);
                return;
            }
            if (dueItem.ScheduledForUtc > DateTime.UtcNow)
            {
                settings.NextPublishAtUtc = dueItem.ScheduledForUtc;
                settings.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            posts.TryGetValue(dueItem.ContentPostId ?? Guid.Empty, out var post);
            if (post is null || post.Status == ContentPostStatus.GenerationFailed)
            {
                post = await _generation.GenerateScheduledAsync(
                    projectId,
                    dueItem.ScheduledForUtc,
                    new GeneratedCopy(
                        dueItem.Topic,
                        dueItem.VisualHeadline,
                        dueItem.Caption,
                        dueItem.ImagePrompt),
                    cancellationToken);
                dueItem.ContentPostId = post.Id;
                dueItem.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (post.Status is ContentPostStatus.Approved or ContentPostStatus.PublishFailed)
            {
                await _publishing.PublishAsync(projectId, post.Id, cancellationToken);
            }
            else if (post.Status == ContentPostStatus.PublishUnknown)
            {
                StopForUnknownOutcome(settings, post.Error);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            if (post.Status == ContentPostStatus.Published)
                await AdvanceApprovedPlanAsync(plan, dueItem, items, settings, cancellationToken);
        }
        catch (FacebookPublishException exception) when (!exception.OutcomeUnknown)
        {
            settings.NextPublishAtUtc = DateTime.UtcNow.AddMinutes(15);
            settings.LastError = Truncate(exception.Message);
            settings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            var unknown = dueItem?.ContentPostId is Guid postId
                && await _dbContext.ContentPosts.IgnoreQueryFilters()
                    .AnyAsync(post => post.ProjectId == projectId
                        && post.Id == postId
                        && post.Status == ContentPostStatus.PublishUnknown, cancellationToken);
            if (unknown)
            {
                StopForUnknownOutcome(settings, exception.Message);
            }
            else
            {
                settings.NextPublishAtUtc = DateTime.UtcNow.AddMinutes(15);
                settings.LastError = Truncate(exception.Message);
            }
            settings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogError(exception, "Scheduled content failed for project {ProjectId}", projectId);
        }
    }

    private async Task AdvancePlanForPublishedPostAsync(
        ContentAutomationSettings settings,
        ContentPost post,
        CancellationToken cancellationToken)
    {
        if (post.Status != ContentPostStatus.Published) return;
        var item = await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == post.ProjectId
                && candidate.ContentPostId == post.Id, cancellationToken);
        if (item is null) return;
        var plan = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == post.ProjectId
                && candidate.Id == item.PlanId, cancellationToken);
        if (plan.Status != ContentWeekPlanStatus.Approved) return;
        var items = await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == post.ProjectId && candidate.PlanId == plan.Id)
            .OrderBy(candidate => candidate.DayIndex)
            .ToListAsync(cancellationToken);
        await AdvanceApprovedPlanAsync(plan, item, items, settings, cancellationToken);
    }

    private async Task AdvanceApprovedPlanAsync(
        ContentWeekPlan plan,
        ContentWeekPlanItem publishedItem,
        IReadOnlyList<ContentWeekPlanItem> items,
        ContentAutomationSettings settings,
        CancellationToken cancellationToken)
    {
        var nextItem = items.FirstOrDefault(item => item.DayIndex > publishedItem.DayIndex);
        if (nextItem is null)
        {
            await CompletePlanAsync(plan, settings, cancellationToken);
            return;
        }
        if (nextItem.ScheduledForUtc <= DateTime.UtcNow)
        {
            nextItem.ScheduledForUtc = ContentSchedule.NextDayUtc(
                DateTime.UtcNow,
                settings.DailyPublishTimeLocal,
                settings.Timezone);
            nextItem.UpdatedAt = DateTime.UtcNow;
        }
        settings.NextPublishAtUtc = nextItem.ScheduledForUtc;
        settings.LastError = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CompletePlanAsync(
        ContentWeekPlan plan,
        ContentAutomationSettings settings,
        CancellationToken cancellationToken)
    {
        plan.Status = ContentWeekPlanStatus.Completed;
        plan.CompletedAtUtc = DateTime.UtcNow;
        plan.UpdatedAt = DateTime.UtcNow;
        settings.LastError = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var nextApprovedPublishAtUtc = await _weeklyPlans.NextApprovedPublishAtAsync(
            settings.ProjectId,
            cancellationToken);
        settings.IsEnabled = nextApprovedPublishAtUtc.HasValue;
        settings.NextPublishAtUtc = nextApprovedPublishAtUtc;
        settings.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (!nextApprovedPublishAtUtc.HasValue)
            await GenerateWeeklyPlanAsync(settings.ProjectId);
    }

    private async Task<bool> GenerateMissingPlanPreviewsAsync(
        ContentWeekPlan plan,
        CancellationToken cancellationToken)
    {
        var items = await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
            .Where(item => item.ProjectId == plan.ProjectId && item.PlanId == plan.Id)
            .OrderBy(item => item.DayIndex)
            .ToListAsync(cancellationToken);
        var missingItems = items.Where(item => item.ContentPostId is null).ToArray();
        foreach (var item in missingItems)
            await GeneratePlanPreviewAsync(item, cancellationToken);
        return missingItems.Length > 0;
    }

    private async Task GeneratePlanPreviewAsync(
        ContentWeekPlanItem item,
        CancellationToken cancellationToken)
    {
        var post = await _generation.GenerateScheduledPreviewAsync(
            item.ProjectId,
            item.ScheduledForUtc,
            new GeneratedCopy(item.Topic, item.VisualHeadline, item.Caption, item.ImagePrompt),
            cancellationToken);
        item.ContentPostId = post.Id;
        item.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RejectLinkedPreviewAsync(
        Guid projectId,
        ContentWeekPlanItem item,
        CancellationToken cancellationToken)
    {
        if (item.ContentPostId is Guid postId)
        {
            var post = await _dbContext.ContentPosts.IgnoreQueryFilters()
                .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId && candidate.Id == postId, cancellationToken);
            if (post is not null && post.Status != ContentPostStatus.Published)
            {
                post.Status = ContentPostStatus.Rejected;
                post.Error = "طلب المستخدم صورة بديلة لهذا اليوم.";
                post.UpdatedAt = DateTime.UtcNow;
            }
        }
        item.ContentPostId = null;
        item.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void StopForUnknownOutcome(ContentAutomationSettings settings, string? error)
    {
        settings.IsEnabled = false;
        settings.NextPublishAtUtc = null;
        settings.LastError = Truncate(error ?? "نتيجة النشر غير معروفة. تم إيقاف الجدول لمنع تكرار المنشور.");
        settings.UpdatedAt = DateTime.UtcNow;
    }

    private static string Truncate(string message) => message[..Math.Min(message.Length, 1000)];
}
