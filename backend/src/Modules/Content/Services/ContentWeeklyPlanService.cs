using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Modules.Content.Domain;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.Content.Services;

public sealed class ContentWeeklyPlanService
{
    private const int MaximumPlanGenerationAttempts = 6;
    private const int MaximumCopyRepairAttempts = 10;
    private static readonly string[] RetryCreativeDirections =
    [
        "ابنِ الأيام حول مواقف يومية دقيقة يمر بها الجمهور، من دون إعادة استخدام محاور السجل السابق.",
        "ابدأ من قرارات واعتراضات وأسئلة مختلفة تمامًا، واجعل كل منشور يعالج لحظة واحدة فقط.",
        "استخدم تمارين واختبارات ذاتية وملاحظات مهنية جديدة، مع Hooks ونهايات لم تظهر في السجل.",
        "استخدم قصصًا مصغرة ومفارقات ومشاهد واقعية، وابتعد عن سرد مزايا الخدمة بالطريقة المعتادة.",
        "انتقل إلى زوايا الهوية المهنية والتقدم والعادات والأخطاء الخفية، بصياغة وبناء جديدين بالكامل."
    ];
    private static readonly string[] RepairClosingDirections =
    [
        "اختم بسؤال حقيقي محدد يطلب تجربة القارئ، من دون رابط أو حجز أو بيع.",
        "اختم بدعوة لحفظ المنشور والرجوع إلى الخطوة لاحقًا، من دون رابط أو حجز.",
        "اختم بدعوة لمشاركة المنشور مع شخص يواجه الموقف نفسه، من دون رابط أو حجز.",
        "اختم بتحدٍ صغير قابل للتنفيذ اليوم، من دون طلب تواصل أو بيع.",
        "اختم بملاحظة مهنية قصيرة مكتملة المعنى، من دون أي أمر للقارئ.",
        "اختم بسؤال اختيار واضح بين بديلين، من دون رابط أو حجز.",
        "اختم بطلب كتابة مثال أو موقف شخصي في التعليقات، من دون رابط أو بيع.",
        "اختم بدعوة لتجربة تمرين واحد وقياس نتيجته، من دون رابط أو حجز.",
        "اختم بدعوة بيع مباشرة واحدة فقط، مستخدمًا رابطًا معتمدًا إن وُجد في قاعدة المعرفة.",
        "اختم بجملة نتيجة قصيرة لا تحتوي كلمات احجز أو تواصل أو جرّب أو شارك أو احفظ."
    ];

    private readonly AppDbContext _dbContext;
    private readonly IGeminiClient _geminiClient;
    private readonly IProjectSecretVault _secretVault;
    private readonly ILogger<ContentWeeklyPlanService> _logger;

    public ContentWeeklyPlanService(
        AppDbContext dbContext,
        IGeminiClient geminiClient,
        IProjectSecretVault secretVault,
        ILogger<ContentWeeklyPlanService> logger)
    {
        _dbContext = dbContext;
        _geminiClient = geminiClient;
        _secretVault = secretVault;
        _logger = logger;
    }

    public async Task<ContentWeekPlan> GenerateAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var draftPlan = await DraftPlanAsync(projectId, cancellationToken);
        if (draftPlan is not null) return draftPlan;

        var settings = await _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId, cancellationToken)
            ?? throw new InvalidOperationException("احفظ إعدادات المحتوى أولاً.");
        if (!settings.HasApprovedStyle || settings.LastPublishedAtUtc is null)
            throw new InvalidOperationException("اعتمد وانشر أول تصميم قبل تجهيز خطة الأسبوع.");

        var projectAi = await _dbContext.ProjectSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId, cancellationToken)
            ?? throw new InvalidOperationException("إعدادات الذكاء الاصطناعي للمشروع غير موجودة.");
        var apiKey = _secretVault.Unprotect(projectId, projectAi.GeminiApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("أضف مفتاح Gemini في إعدادات المشروع أولاً.");

        var knowledge = await _dbContext.KnowledgeDocuments.IgnoreQueryFilters()
            .ReadyForGeneration(projectId)
            .OrderBy(document => document.Title)
            .Select(document => new KnowledgeSource(document.Title, document.Content))
            .ToListAsync(cancellationToken);
        if (knowledge.Count == 0)
            throw new InvalidOperationException("اعتمد مستندًا واحدًا على الأقل في قاعدة المعرفة قبل تجهيز الخطة.");

        var contentHistory = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .Where(post => post.ProjectId == projectId && post.Status != ContentPostStatus.Rejected)
            .OrderByDescending(post => post.CreatedAt)
            .Select(post => new HistoricalContent(post.Topic, post.VisualHeadline, post.Caption))
            .ToListAsync(cancellationToken);
        var latestReservedSlot = await LatestReservedSlotAsync(projectId, cancellationToken);
        var scheduleAnchorUtc = latestReservedSlot is DateTime reservedSlot && reservedSlot > DateTime.UtcNow
            ? reservedSlot
            : DateTime.UtcNow;
        var schedule = ContentSchedule.NextWeekUtc(
            scheduleAnchorUtc,
            settings.DailyPublishTimeLocal,
            settings.Timezone);
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(schedule[0], timezone);
        var plan = NewPlan(projectId, settings, knowledge.Count, DateOnly.FromDateTime(localStart));
        var hasApprovedPlan = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .AnyAsync(candidate => candidate.ProjectId == projectId
                && candidate.Status == ContentWeekPlanStatus.Approved, cancellationToken);
        if (!hasApprovedPlan)
        {
            settings.IsEnabled = false;
            settings.NextPublishAtUtc = null;
        }
        settings.UpdatedAt = DateTime.UtcNow;
        _dbContext.ContentWeekPlans.Add(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var plannedPosts = await GenerateValidPlanAsync(new ContentPlanGenerationInput(
                projectAi,
                apiKey,
                settings,
                knowledge,
                contentHistory));
            if (!await BrandStillMatchesAsync(plan, cancellationToken))
            {
                plan.Status = ContentWeekPlanStatus.Rejected;
                plan.Error = "تم تغيير هوية البراند أثناء تجهيز الخطة؛ جهّز خطة جديدة.";
                plan.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return plan;
            }

            _dbContext.ContentWeekPlanItems.AddRange(plannedPosts.Select((copy, dayIndex) =>
                NewPlanItem(plan, copy, dayIndex, schedule[dayIndex])));
            plan.Error = null;
            plan.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return plan;
        }
        catch (Exception exception)
        {
            foreach (var entry in _dbContext.ChangeTracker.Entries<ContentWeekPlanItem>()
                         .Where(entry => entry.Entity.PlanId == plan.Id && entry.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }
            plan.Status = ContentWeekPlanStatus.GenerationFailed;
            plan.Error = Truncate(exception.Message);
            plan.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogError(exception, "Weekly content plan generation failed for project {ProjectId}", projectId);
            return plan;
        }
    }

    public async Task MarkReadyForReviewAsync(
        Guid projectId,
        Guid planId,
        CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(projectId, planId, cancellationToken);
        if (plan.Status is not (ContentWeekPlanStatus.Generating or ContentWeekPlanStatus.AwaitingApproval)) return;
        if (!await BrandStillMatchesAsync(plan, cancellationToken))
        {
            plan.Status = ContentWeekPlanStatus.Rejected;
            plan.Error = "تم تغيير هوية البراند أثناء تجهيز الصور؛ جهّز خطة جديدة.";
        }
        else
        {
            plan.Status = ContentWeekPlanStatus.AwaitingApproval;
            plan.GeneratedAtUtc = DateTime.UtcNow;
            plan.Error = null;
        }
        plan.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ApproveItemAsync(
        Guid projectId,
        Guid planId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(projectId, planId, cancellationToken);
        if (plan.Status != ContentWeekPlanStatus.AwaitingApproval)
            throw new InvalidOperationException("الخطة ليست جاهزة لمراجعة الصور.");
        if (!await BrandStillMatchesAsync(plan, cancellationToken))
            throw new InvalidOperationException("تم تغيير اللوجو أو شكل التصميم؛ جهّز خطة أسبوع جديدة.");
        var item = await PlanItemAsync(projectId, planId, itemId, cancellationToken);
        var post = await ReviewablePostAsync(projectId, item.ContentPostId, cancellationToken);
        post.Status = ContentPostStatus.Approved;
        post.ApprovedAtUtc = DateTime.UtcNow;
        post.Error = null;
        post.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ContentWeekPlanItem> RegenerableItemAsync(
        Guid projectId,
        Guid planId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(projectId, planId, cancellationToken);
        if (plan.Status != ContentWeekPlanStatus.AwaitingApproval)
            throw new InvalidOperationException("لا يمكن تغيير صورة بعد اعتماد الخطة.");
        var item = await PlanItemAsync(projectId, planId, itemId, cancellationToken);
        var post = await LinkedPostAsync(projectId, item.ContentPostId, cancellationToken);
        if (post.Status is not (ContentPostStatus.AwaitingApproval
            or ContentPostStatus.Approved
            or ContentPostStatus.GenerationFailed))
            throw new InvalidOperationException("لا يمكن إعادة هذه الصورة الآن.");
        return item;
    }

    public async Task ApproveAsync(Guid projectId, Guid planId, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId && candidate.Id == planId, cancellationToken)
            ?? throw new InvalidOperationException("خطة الأسبوع غير موجودة.");
        if (plan.Status != ContentWeekPlanStatus.AwaitingApproval)
            throw new InvalidOperationException("خطة الأسبوع ليست بانتظار الموافقة.");
        if (!await BrandStillMatchesAsync(plan, cancellationToken))
            throw new InvalidOperationException("تم تغيير اللوجو أو شكل التصميم؛ جهّز خطة أسبوع جديدة.");

        var items = await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.PlanId == planId)
            .OrderBy(item => item.DayIndex)
            .ToListAsync(cancellationToken);
        if (items.Count != 7 || items[0].ScheduledForUtc <= DateTime.UtcNow)
            throw new InvalidOperationException("انتهى وقت بداية هذه الخطة؛ جهّز خطة أسبوع جديدة.");
        var postIds = items.Where(item => item.ContentPostId.HasValue)
            .Select(item => item.ContentPostId!.Value)
            .ToArray();
        var approvedImageCount = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .CountAsync(post => post.ProjectId == projectId
                && postIds.Contains(post.Id)
                && post.Status == ContentPostStatus.Approved
                && post.ImageObjectKey != null, cancellationToken);
        if (postIds.Length != 7 || approvedImageCount != 7)
            throw new InvalidOperationException("وافق على صورة كل يوم قبل اعتماد خطة الأسبوع.");

        var settings = await _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == projectId, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.FacebookPageId))
            throw new InvalidOperationException("اختر صفحة Facebook قبل اعتماد الخطة.");

        plan.Status = ContentWeekPlanStatus.Approved;
        plan.ApprovedAtUtc = DateTime.UtcNow;
        plan.Error = null;
        plan.UpdatedAt = DateTime.UtcNow;
        var currentNextPublishAtUtc = await NextApprovedPublishAtAsync(projectId, cancellationToken);
        settings.IsEnabled = true;
        settings.NextPublishAtUtc = currentNextPublishAtUtc is DateTime currentSlot
            && currentSlot < items[0].ScheduledForUtc
                ? currentSlot
                : items[0].ScheduledForUtc;
        settings.LastError = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectAsync(
        Guid projectId,
        Guid planId,
        string reason,
        CancellationToken cancellationToken)
    {
        var plan = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId && candidate.Id == planId, cancellationToken)
            ?? throw new InvalidOperationException("خطة الأسبوع غير موجودة.");
        if (plan.Status is not (ContentWeekPlanStatus.AwaitingApproval or ContentWeekPlanStatus.GenerationFailed))
            throw new InvalidOperationException("لا يمكن استبدال خطة بدأ تنفيذها.");
        plan.Status = ContentWeekPlanStatus.Rejected;
        plan.Error = reason;
        plan.UpdatedAt = DateTime.UtcNow;
        await RejectPlanPostsAsync([plan.Id], reason, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkActivePlansRejectedAsync(
        Guid projectId,
        string reason,
        CancellationToken cancellationToken)
    {
        var plans = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .Where(plan => plan.ProjectId == projectId
                && (plan.Status == ContentWeekPlanStatus.Generating
                    || plan.Status == ContentWeekPlanStatus.AwaitingApproval
                    || plan.Status == ContentWeekPlanStatus.Approved))
            .ToListAsync(cancellationToken);
        foreach (var plan in plans)
        {
            plan.Status = ContentWeekPlanStatus.Rejected;
            plan.Error = reason;
            plan.UpdatedAt = DateTime.UtcNow;
        }
        await RejectPlanPostsAsync(plans.Select(plan => plan.Id).ToArray(), reason, cancellationToken);
    }

    public async Task<DateTime?> NextApprovedPublishAtAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var approvedPlanIds = _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .Where(plan => plan.ProjectId == projectId && plan.Status == ContentWeekPlanStatus.Approved)
            .Select(plan => plan.Id);
        return await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId
                && approvedPlanIds.Contains(item.PlanId)
                && (item.ContentPostId == null
                    || !_dbContext.ContentPosts.IgnoreQueryFilters().Any(post => post.ProjectId == projectId
                        && post.Id == item.ContentPostId
                        && post.Status == ContentPostStatus.Published)))
            .OrderBy(item => item.ScheduledForUtc)
            .Select(item => (DateTime?)item.ScheduledForUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DateTime?> RescheduleActivePlanAsync(
        ContentPlanScheduleChange scheduleChange,
        CancellationToken cancellationToken)
    {
        var plans = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == scheduleChange.ProjectId
                && (candidate.Status == ContentWeekPlanStatus.AwaitingApproval
                    || candidate.Status == ContentWeekPlanStatus.Approved))
            .OrderBy(candidate => candidate.StartDateLocal)
            .ThenBy(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);
        if (plans.Count == 0) return null;
        var planIds = plans.Select(plan => plan.Id).ToArray();

        var planItems = await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
            .Where(planItem => planItem.ProjectId == scheduleChange.ProjectId
                && planIds.Contains(planItem.PlanId))
            .OrderBy(planItem => planItem.ScheduledForUtc)
            .ToListAsync(cancellationToken);
        var linkedPosts = await LinkedPlanPostsAsync(scheduleChange.ProjectId, planItems, cancellationToken);
        var unpublishedItems = UnpublishedItems(planItems, linkedPosts);
        if (unpublishedItems.Count == 0) return null;

        var nextSlot = NextRescheduledSlot(scheduleChange, linkedPosts.Values);
        DateTime? nextApprovedSlot = null;
        foreach (var plan in plans)
        {
            var currentPlanItems = planItems
                .Where(planItem => planItem.PlanId == plan.Id)
                .OrderBy(planItem => planItem.DayIndex)
                .ToArray();
            var currentUnpublishedItems = UnpublishedItems(currentPlanItems, linkedPosts);
            if (currentUnpublishedItems.Count == 0) continue;

            var firstPlanSlot = nextSlot;
            ApplyRescheduledSlots(currentUnpublishedItems, linkedPosts, firstPlanSlot, scheduleChange);
            UpdatePlanSchedule(
                plan,
                currentPlanItems.Length == currentUnpublishedItems.Count,
                firstPlanSlot,
                scheduleChange);
            if (nextApprovedSlot is null && plan.Status == ContentWeekPlanStatus.Approved)
                nextApprovedSlot = firstPlanSlot;
            nextSlot = ContentSchedule.NextDayUtc(
                currentUnpublishedItems[^1].ScheduledForUtc,
                scheduleChange.DailyPublishTimeLocal,
                scheduleChange.Timezone);
        }
        return nextApprovedSlot;
    }

    private async Task<Dictionary<Guid, ContentPost>> LinkedPlanPostsAsync(
        Guid projectId,
        IReadOnlyCollection<ContentWeekPlanItem> planItems,
        CancellationToken cancellationToken)
    {
        var postIds = planItems.Where(planItem => planItem.ContentPostId.HasValue)
            .Select(planItem => planItem.ContentPostId!.Value)
            .ToArray();
        return await _dbContext.ContentPosts.IgnoreQueryFilters()
            .Where(post => post.ProjectId == projectId && postIds.Contains(post.Id))
            .ToDictionaryAsync(post => post.Id, cancellationToken);
    }

    private static IReadOnlyList<ContentWeekPlanItem> UnpublishedItems(
        IReadOnlyCollection<ContentWeekPlanItem> planItems,
        IReadOnlyDictionary<Guid, ContentPost> linkedPosts) =>
        planItems.Where(planItem => planItem.ContentPostId is not Guid postId
                || !linkedPosts.TryGetValue(postId, out var post)
                || post.Status != ContentPostStatus.Published)
            .ToArray();

    private static DateTime NextRescheduledSlot(
        ContentPlanScheduleChange scheduleChange,
        IEnumerable<ContentPost> linkedPosts)
    {
        var nowUtc = DateTime.SpecifyKind(scheduleChange.NowUtc, DateTimeKind.Utc);
        var nextSlot = ContentSchedule.NextUtc(nowUtc, scheduleChange.DailyPublishTimeLocal, scheduleChange.Timezone);
        var targetTimezone = TimeZoneInfo.FindSystemTimeZoneById(scheduleChange.Timezone);
        var nextLocalDate = TimeZoneInfo.ConvertTimeFromUtc(nextSlot, targetTimezone).Date;
        var alreadyPublishedThatDay = linkedPosts.Any(post => post.PublishedAtUtc is DateTime publishedAtUtc
            && TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(publishedAtUtc, DateTimeKind.Utc), targetTimezone).Date
                >= nextLocalDate);
        return alreadyPublishedThatDay
            ? ContentSchedule.NextDayUtc(nowUtc, scheduleChange.DailyPublishTimeLocal, scheduleChange.Timezone)
            : nextSlot;
    }

    private static void ApplyRescheduledSlots(
        IEnumerable<ContentWeekPlanItem> unpublishedItems,
        IReadOnlyDictionary<Guid, ContentPost> linkedPosts,
        DateTime firstSlot,
        ContentPlanScheduleChange scheduleChange)
    {
        var scheduledSlot = firstSlot;
        foreach (var planItem in unpublishedItems)
        {
            planItem.ScheduledForUtc = scheduledSlot;
            planItem.UpdatedAt = scheduleChange.NowUtc;
            if (planItem.ContentPostId is Guid postId && linkedPosts.TryGetValue(postId, out var linkedPost))
            {
                linkedPost.ScheduledForUtc = scheduledSlot;
                linkedPost.UpdatedAt = scheduleChange.NowUtc;
            }
            scheduledSlot = ContentSchedule.NextDayUtc(
                scheduledSlot,
                scheduleChange.DailyPublishTimeLocal,
                scheduleChange.Timezone);
        }
    }

    private static void UpdatePlanSchedule(
        ContentWeekPlan plan,
        bool allItemsUnpublished,
        DateTime firstSlot,
        ContentPlanScheduleChange scheduleChange)
    {
        if (allItemsUnpublished)
        {
            var targetTimezone = TimeZoneInfo.FindSystemTimeZoneById(scheduleChange.Timezone);
            plan.StartDateLocal = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(firstSlot, targetTimezone));
        }
        plan.DailyPublishTimeLocal = scheduleChange.DailyPublishTimeLocal;
        plan.Timezone = scheduleChange.Timezone;
        plan.Error = null;
        plan.UpdatedAt = scheduleChange.NowUtc;
    }

    internal static IReadOnlyList<GeneratedCopy> ParsePlan(string response)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<GeneratedWeekPlan>(
                JsonObject(response, "رد Gemini لخطة الأسبوع ليس JSON صالحًا."),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (plan?.Items is not { Count: 7 }
                || plan.Items.Any(MissingRequiredCopyField)
                || plan.Items.Any(item => ContentGenerationService.HasRepeatedHeadlineWord(item.VisualHeadline))
                || plan.Items.Select(item => item.Topic.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 7
                || plan.Items.Select(item => item.VisualHeadline.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 7)
            {
                throw new InvalidOperationException("خطة الأسبوع يجب أن تحتوي 7 أفكار وعناوين مختلفة ومكتملة.");
            }

            return plan.Items.Select(TrimCopy).ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("تعذر قراءة خطة الأسبوع المولدة.", exception);
        }
    }

    internal static GeneratedCopy ParseCopy(string response)
    {
        try
        {
            var copy = JsonSerializer.Deserialize<GeneratedCopy>(
                JsonObject(response, "رد إصلاح الكابشن ليس JSON صالحًا."),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (copy is null || MissingRequiredCopyField(copy))
                throw new InvalidOperationException("بديل الكابشن يجب أن يحتوي فكرة وعنوانًا وكابشنًا ووصف صورة.");
            return TrimCopy(copy);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("تعذر قراءة بديل الكابشن المولد.", exception);
        }
    }

    private static string JsonObject(string response, string invalidResponseMessage)
    {
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException(invalidResponseMessage);
        return response[start..(end + 1)];
    }

    private async Task<ContentWeekPlan?> DraftPlanAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var activeDraft = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .Where(plan => plan.ProjectId == projectId
                && (plan.Status == ContentWeekPlanStatus.Generating
                    || plan.Status == ContentWeekPlanStatus.AwaitingApproval))
            .OrderByDescending(plan => plan.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeDraft is not null) return activeDraft;

        var latestPlan = await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .Where(plan => plan.ProjectId == projectId)
            .OrderByDescending(plan => plan.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return latestPlan?.Status == ContentWeekPlanStatus.GenerationFailed ? latestPlan : null;
    }

    private async Task<DateTime?> LatestReservedSlotAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var reservingPlanIds = _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .Where(plan => plan.ProjectId == projectId
                && (plan.Status == ContentWeekPlanStatus.Generating
                    || plan.Status == ContentWeekPlanStatus.AwaitingApproval
                    || plan.Status == ContentWeekPlanStatus.Approved))
            .Select(plan => plan.Id);
        return await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && reservingPlanIds.Contains(item.PlanId))
            .OrderByDescending(item => item.ScheduledForUtc)
            .Select(item => (DateTime?)item.ScheduledForUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ContentWeekPlan> PlanAsync(
        Guid projectId,
        Guid planId,
        CancellationToken cancellationToken) =>
        await _dbContext.ContentWeekPlans.IgnoreQueryFilters()
            .SingleOrDefaultAsync(plan => plan.ProjectId == projectId && plan.Id == planId, cancellationToken)
        ?? throw new InvalidOperationException("خطة الأسبوع غير موجودة.");

    private async Task<ContentPost> ReviewablePostAsync(
        Guid projectId,
        Guid? postId,
        CancellationToken cancellationToken)
    {
        var post = await LinkedPostAsync(projectId, postId, cancellationToken);
        if (post.Status != ContentPostStatus.AwaitingApproval || string.IsNullOrWhiteSpace(post.ImageObjectKey))
            throw new InvalidOperationException("الصورة ليست جاهزة للموافقة.");
        return post;
    }

    private async Task<ContentWeekPlanItem> PlanItemAsync(
        Guid projectId,
        Guid planId,
        Guid itemId,
        CancellationToken cancellationToken) =>
        await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.ProjectId == projectId
                && item.PlanId == planId
                && item.Id == itemId, cancellationToken)
        ?? throw new InvalidOperationException("يوم الخطة غير موجود.");

    private async Task<ContentPost> LinkedPostAsync(
        Guid projectId,
        Guid? postId,
        CancellationToken cancellationToken) =>
        postId.HasValue
            ? await _dbContext.ContentPosts.IgnoreQueryFilters()
                .SingleOrDefaultAsync(post => post.ProjectId == projectId
                    && post.Id == postId.Value, cancellationToken)
                ?? throw new InvalidOperationException("صورة اليوم غير موجودة.")
            : throw new InvalidOperationException("الصورة ما زالت قيد التجهيز.");

    private async Task RejectPlanPostsAsync(
        IReadOnlyCollection<Guid> planIds,
        string reason,
        CancellationToken cancellationToken)
    {
        if (planIds.Count == 0) return;
        var postIds = await _dbContext.ContentWeekPlanItems.IgnoreQueryFilters()
            .Where(item => planIds.Contains(item.PlanId) && item.ContentPostId != null)
            .Select(item => item.ContentPostId!.Value)
            .ToListAsync(cancellationToken);
        var posts = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .Where(post => postIds.Contains(post.Id) && post.Status != ContentPostStatus.Published)
            .ToListAsync(cancellationToken);
        foreach (var post in posts)
        {
            post.Status = ContentPostStatus.Rejected;
            post.Error = reason;
            post.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task<IReadOnlyList<GeneratedCopy>> GenerateValidPlanAsync(
        ContentPlanGenerationInput input)
    {
        var basePrompt = BuildPrompt(
            input.ProjectAi,
            input.Settings,
            input.Knowledge,
            input.ContentHistory.Take(60).ToArray());
        var prompt = basePrompt;
        for (var attempt = 1; attempt <= MaximumPlanGenerationAttempts; attempt++)
        {
            try
            {
                var response = await GeneratePlanCandidateAsync(prompt, input);
                return await RepairInvalidCopiesAsync(ParsePlan(response), input);
            }
            catch (InvalidOperationException exception) when (attempt < MaximumPlanGenerationAttempts)
            {
                _logger.LogWarning(
                    "Weekly content draft {Attempt} rejected: {Reason}",
                    attempt,
                    exception.Message);
                prompt = BuildRetryPrompt(basePrompt, exception.Message, attempt);
            }
        }

        throw new InvalidOperationException("تعذر تجهيز خطة أسبوع أصلية بعد المحاولات التلقائية.");
    }

    private Task<string> GeneratePlanCandidateAsync(string prompt, ContentPlanGenerationInput input) =>
        _geminiClient.GenerateReplyAsync(prompt, input.ApiKey, input.ProjectAi.ResolveGeminiModel(DateTime.UtcNow));

    private async Task<IReadOnlyList<GeneratedCopy>> RepairInvalidCopiesAsync(
        IReadOnlyList<GeneratedCopy> generatedCopies,
        ContentPlanGenerationInput generationInput)
    {
        var acceptedCopies = new List<GeneratedCopy>(generatedCopies.Count);
        for (var dayIndex = 0; dayIndex < generatedCopies.Count; dayIndex++)
        {
            GeneratedCopy candidateCopy;
            try
            {
                candidateCopy = ValidatedCandidate(
                    generatedCopies[dayIndex],
                    acceptedCopies,
                    generationInput);
            }
            catch (InvalidOperationException exception)
            {
                candidateCopy = await GenerateReplacementCopyAsync(
                    new ContentCopyRepairContext(generationInput, acceptedCopies, dayIndex, exception.Message));
            }
            acceptedCopies.Add(candidateCopy);
        }
        return acceptedCopies;
    }

    private async Task<GeneratedCopy> GenerateReplacementCopyAsync(ContentCopyRepairContext repairContext)
    {
        var currentContext = repairContext;
        for (var attempt = 1; attempt <= MaximumCopyRepairAttempts; attempt++)
        {
            var response = await GeneratePlanCandidateAsync(
                BuildCopyRepairPrompt(currentContext, attempt),
                currentContext.GenerationInput);
            try
            {
                return ValidatedCandidate(
                    ParseCopy(response),
                    currentContext.AcceptedCopies,
                    currentContext.GenerationInput);
            }
            catch (InvalidOperationException exception) when (attempt < MaximumCopyRepairAttempts)
            {
                _logger.LogWarning(
                    "Weekly content day {Day} repair {Attempt} rejected: {Reason}",
                    currentContext.DayIndex + 1,
                    attempt,
                    exception.Message);
                currentContext = currentContext with { RejectionReason = exception.Message };
            }
        }
        throw new InvalidOperationException("تعذر إصلاح كابشن اليوم تلقائيًا.");
    }

    private static GeneratedCopy ValidatedCandidate(
        GeneratedCopy candidateCopy,
        IReadOnlyList<GeneratedCopy> acceptedCopies,
        ContentPlanGenerationInput generationInput)
    {
        if (MissingRequiredCopyField(candidateCopy))
            throw new InvalidOperationException("اليوم يجب أن يحتوي فكرة وعنوانًا وكابشنًا ووصف صورة.");
        if (ContentGenerationService.HasRepeatedHeadlineWord(candidateCopy.VisualHeadline))
            throw new InvalidOperationException("عنوان الصورة يكرر نفس الكلمة.");
        var trimmedCopy = TrimCopy(candidateCopy);
        EnsureCandidateIsOriginal(trimmedCopy, acceptedCopies, generationInput);
        return trimmedCopy;
    }

    private static void EnsureCandidateIsOriginal(
        GeneratedCopy candidateCopy,
        IReadOnlyList<GeneratedCopy> acceptedCopies,
        ContentPlanGenerationInput generationInput)
    {
        var priorTopics = generationInput.ContentHistory
            .Concat(acceptedCopies.Select(copy =>
                new HistoricalContent(copy.Topic, copy.VisualHeadline, copy.Caption)))
            .ToArray();
        EnsureNoHistoricalRepeats([candidateCopy], priorTopics);
        ContentCaptionOriginality.EnsureWeeklyPlan(
            acceptedCopies.Append(candidateCopy).ToArray(),
            generationInput.Knowledge,
            generationInput.ContentHistory);
    }

    private static string BuildRetryPrompt(string basePrompt, string rejectionReason, int rejectedAttempt)
    {
        return $"{basePrompt}\nالمحاولة السابقة رُفضت للسبب التالي: {rejectionReason}\n{RetryCreativeDirection(rejectedAttempt)}\nلا تصلح النص القديم ولا تعيد صياغته؛ أنشئ سبع أفكار وكابشنات جديدة من الصفر، ثم راجع الأصالة والاكتمال قبل إرجاع JSON.";
    }

    private static string BuildCopyRepairPrompt(ContentCopyRepairContext repairContext, int attempt)
    {
        var generationInput = repairContext.GenerationInput;
        var builder = BuildSharedPrompt(
            generationInput.ProjectAi,
            generationInput.Settings,
            generationInput.Knowledge,
            generationInput.ContentHistory.Take(60).ToArray());
        builder.AppendLine($"المهمة الحالية: استبدل منشور اليوم {repairContext.DayIndex + 1} فقط.");
        builder.AppendLine($"سبب رفض النسخة السابقة: {repairContext.RejectionReason}");
        builder.AppendLine(RetryCreativeDirection(attempt));
        builder.AppendLine($"نوع النهاية الإلزامي لهذه المحاولة: {RepairClosingDirection(attempt)}");
        AppendAcceptedCopies(builder, repairContext.AcceptedCopies);
        builder.AppendLine("اكتب فكرة وعنوانًا وكابشنًا جديدين من الصفر؛ لا تصلح النص المرفوض ولا تعيد صياغته. اجعل الـHook والـCTA مختلفين عن المنشورات المقبولة.");
        builder.AppendLine("أرجع JSON صالحًا فقط من دون markdown، لكائن واحد بالشكل التالي:");
        builder.AppendLine("{\"topic\":\"فكرة كريتيف جديدة\",\"visualHeadline\":\"عنوان من 2 إلى 5 كلمات\",\"caption\":\"كابشن أصلي من 45 إلى 95 كلمة\",\"imagePrompt\":\"English visual concept without a logo request\"}");
        return builder.ToString();
    }

    private static string RetryCreativeDirection(int attempt)
    {
        var directionIndex = Math.Min(attempt - 1, RetryCreativeDirections.Length - 1);
        return RetryCreativeDirections[directionIndex];
    }

    private static string RepairClosingDirection(int attempt)
    {
        var directionIndex = Math.Min(attempt - 1, RepairClosingDirections.Length - 1);
        return RepairClosingDirections[directionIndex];
    }

    private async Task<bool> BrandStillMatchesAsync(ContentWeekPlan plan, CancellationToken cancellationToken)
    {
        var currentBrand = await _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .Where(settings => settings.ProjectId == plan.ProjectId)
            .Select(settings => new { settings.LogoObjectKey, settings.StylePrompt })
            .SingleAsync(cancellationToken);
        return string.Equals(plan.BrandLogoObjectKey, currentBrand.LogoObjectKey, StringComparison.Ordinal)
            && string.Equals(plan.BrandStylePrompt, currentBrand.StylePrompt, StringComparison.Ordinal);
    }

    private static ContentWeekPlan NewPlan(
        Guid projectId,
        ContentAutomationSettings settings,
        int knowledgeDocumentCount,
        DateOnly startDateLocal) => new()
    {
        ProjectId = projectId,
        StartDateLocal = startDateLocal,
        DailyPublishTimeLocal = settings.DailyPublishTimeLocal,
        Timezone = settings.Timezone,
        BrandLogoObjectKey = settings.LogoObjectKey!,
        BrandStylePrompt = settings.StylePrompt,
        KnowledgeDocumentCount = knowledgeDocumentCount
    };

    private static ContentWeekPlanItem NewPlanItem(
        ContentWeekPlan plan,
        GeneratedCopy copy,
        int dayIndex,
        DateTime scheduledForUtc) => new()
    {
        ProjectId = plan.ProjectId,
        PlanId = plan.Id,
        DayIndex = dayIndex,
        ScheduledForUtc = scheduledForUtc,
        Topic = copy.Topic,
        VisualHeadline = copy.VisualHeadline,
        Caption = copy.Caption,
        ImagePrompt = copy.ImagePrompt
    };

    private static string BuildPrompt(
        Modules.Projects.Domain.ProjectSettings projectAi,
        ContentAutomationSettings settings,
        IReadOnlyList<KnowledgeSource> knowledge,
        IReadOnlyList<HistoricalContent> recentPosts)
    {
        var builder = BuildSharedPrompt(projectAi, settings, knowledge, recentPosts);
        builder.AppendLine("جهّز خطة Facebook من 7 منشورات مختلفة للأيام السبعة القادمة.");
        builder.AppendLine("اختَر سبع معالجات مختلفة من بنك زوايا واسع: موقف يومي، لقطة من مقابلة، ملاحظة مهنية، خطأ شائع، مفارقة، اختبار ذاتي، تمرين، قصة قصيرة، تفكيك خوف، قرار، مقارنة، قائمة تحقق، سؤال نقاش، هوية مهنية، أو مشهد قبل/بعد. لا تكرر توليفة الأسبوع السابق، وغيّر طول الجمل وإيقاعها وبناء الكابشن.");
        builder.AppendLine("وزّع النهايات على الأسبوع: منشوران فقط بدعوة بيع مباشرة أو حجز مع الرابط، منشوران بسؤال حقيقي للنقاش، منشور للحفظ، منشور للمشاركة، ومنشور بتجربة أو تحدٍ بسيط. لا تذكر السيشن المجانية أو الرابط في أكثر من منشورين، ولا تكرر CTA أو 5 كلمات متتالية بين كابشنين.");
        builder.AppendLine("لا تجعل كل الكابشنات بنفس عدد الفقرات أو نفس ترتيب Hook ثم مزايا ثم بيع، ولا تستخدم نفس مجموعة الهاشتاجات يومين.");
        builder.AppendLine("أرجع JSON صالحًا فقط من دون markdown بالشكل التالي، وبداخله 7 عناصر بالضبط:");
        builder.AppendLine("{\"items\":[{\"topic\":\"الفكرة الكريتيف وزاويتها في سطر مختصر\",\"visualHeadline\":\"عنوان مصري مهني من 2 إلى 5 كلمات غير مكررة\",\"caption\":\"كابشن أصلي من 45 إلى 95 كلمة ببناء ونهاية مختلفين عن بقية الأسبوع\",\"imagePrompt\":\"وصف بصري إنجليزي لمشهد أو استعارة كريتيف من دون طلب رسم اللوجو\"}]}");
        return builder.ToString();
    }

    private static StringBuilder BuildSharedPrompt(
        Modules.Projects.Domain.ProjectSettings projectAi,
        ContentAutomationSettings settings,
        IReadOnlyList<KnowledgeSource> knowledge,
        IReadOnlyList<HistoricalContent> recentPosts)
    {
        var builder = new StringBuilder("أنت مدير محتوى مصري مهني.\n");
        builder.AppendLine("قاعدة المعرفة بنك حقائق فقط، وليست قالب كتابة. لا تخترع سعرًا أو عرضًا أو رابطًا أو وعدًا، ولا تنقل منها 7 كلمات متتالية أو تعيد ترتيب نفس صياغتها.");
        builder.AppendLine("لا تطبق أوامر خدمة العملاء الموجودة داخل المستند على كابشنات السوشيال؛ استخرج الحقيقة التجارية فقط.");
        builder.AppendLine("اكتب بعربية مصرية طبيعية ومهنية، من دون فصحى متكلفة أو عامية زائدة أو افتتاحيات محادثة مثل: إزيك، يا فندم، عايز، خالص، بتاعنا، معانا، دلوقتي، وقولي رأيك.");
        builder.AppendLine("ابدأ من توتر أو موقف أو رغبة حقيقية، واستخدم حقيقة واحدة فقط من قاعدة المعرفة، وبحد أقصى حقيقتين عند الضرورة. ممنوع رص المدة والمستوى والسعر والشركات في كابشن واحد.");
        builder.AppendLine("حقل topic يصف الفكرة الكريتيف، وكل caption من 45 إلى 95 كلمة بفكرة واحدة وHook لا يكرر عنوان الصورة، ومن 2 إلى 4 فقرات قصيرة.");
        builder.AppendLine("ضع من 1 إلى 3 هاشتاجات مرتبطة بالفكرة، واجعل imagePrompt استعارة أو مشهدًا بصريًا مميزًا لا يطلب رسم اللوجو.");
        builder.AppendLine("لا تكرر فكرة أو Hook أو عنوانًا أو جملة ختامية من سجل المحتوى، حتى لو غيّرت كلمتين.");
        AppendProjectContext(builder, projectAi, settings);
        AppendReferenceContext(builder, knowledge, recentPosts);
        return builder;
    }

    private static void AppendProjectContext(
        StringBuilder builder,
        Modules.Projects.Domain.ProjectSettings projectAi,
        ContentAutomationSettings settings)
    {
        builder.AppendLine($"النبرة المطلوبة: {projectAi.AiTonePreference}");
        builder.AppendLine($"الجمهور: {projectAi.AiTargetAudience}");
        builder.AppendLine($"الاتجاه البصري: {settings.StylePrompt}");
        if (!string.IsNullOrWhiteSpace(projectAi.SystemPrompt))
            builder.AppendLine($"تعليمات المشروع: {projectAi.SystemPrompt}");
    }

    private static void AppendReferenceContext(
        StringBuilder builder,
        IReadOnlyList<KnowledgeSource> knowledge,
        IReadOnlyList<HistoricalContent> recentPosts)
    {
        if (recentPosts.Count > 0)
        {
            builder.AppendLine("ذاكرة المحتوى السابق والمحجوز: استخدمها للاستبعاد فقط، ولا تستلهم أو تعيد تدوير صياغتها:");
            foreach (var recent in recentPosts)
            {
                builder.AppendLine(
                    $"- الفكرة: {recent.Topic} | العنوان: {recent.VisualHeadline} | الكابشن السابق: {ContentCaptionOriginality.HistoryExcerpt(recent.Caption)}");
            }
        }
        builder.AppendLine("قاعدة المعرفة المعتمدة كاملة:");
        foreach (var source in knowledge)
        {
            builder.AppendLine($"### {source.Title}");
            builder.AppendLine(source.Content);
        }
    }

    private static void AppendAcceptedCopies(
        StringBuilder builder,
        IReadOnlyList<GeneratedCopy> acceptedCopies)
    {
        if (acceptedCopies.Count == 0) return;
        builder.AppendLine("منشورات هذا الأسبوع المقبولة؛ استبعد أفكارها وعناوينها وصياغتها:");
        foreach (var acceptedCopy in acceptedCopies)
        {
            builder.AppendLine(
                $"- الفكرة: {acceptedCopy.Topic} | العنوان: {acceptedCopy.VisualHeadline} | الكابشن: {ContentCaptionOriginality.HistoryExcerpt(acceptedCopy.Caption)}");
        }
    }

    private static bool MissingRequiredCopyField(GeneratedCopy copy) =>
        string.IsNullOrWhiteSpace(copy.Topic)
        || string.IsNullOrWhiteSpace(copy.VisualHeadline)
        || string.IsNullOrWhiteSpace(copy.Caption)
        || string.IsNullOrWhiteSpace(copy.ImagePrompt);

    private static GeneratedCopy TrimCopy(GeneratedCopy copy) => copy with
    {
        Topic = copy.Topic.Trim(),
        VisualHeadline = copy.VisualHeadline.Trim(),
        Caption = ContentGenerationService.NormalizeCaptionTone(copy.Caption),
        ImagePrompt = copy.ImagePrompt.Trim()
    };

    internal static void EnsureNoHistoricalRepeats(
        IReadOnlyList<GeneratedCopy> generatedPlan,
        IReadOnlyList<HistoricalContent> history)
    {
        var previousTopics = history.Select(item => ContentCaptionOriginality.NormalizeForComparison(item.Topic))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var previousHeadlines = history.Select(item => ContentCaptionOriginality.NormalizeForComparison(item.VisualHeadline))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (generatedPlan.Any(item => previousTopics.Contains(ContentCaptionOriginality.NormalizeForComparison(item.Topic))
                || previousHeadlines.Contains(ContentCaptionOriginality.NormalizeForComparison(item.VisualHeadline))))
        {
            throw new InvalidOperationException("الخطة كررت فكرة أو عنوانًا من المحتوى السابق.");
        }
    }

    private static string Truncate(string message) => message[..Math.Min(message.Length, 1000)];
}

internal sealed record GeneratedWeekPlan(List<GeneratedCopy> Items);

internal sealed record HistoricalContent(string Topic, string VisualHeadline, string Caption = "");

internal sealed record ContentPlanGenerationInput(
    Modules.Projects.Domain.ProjectSettings ProjectAi,
    string ApiKey,
    ContentAutomationSettings Settings,
    IReadOnlyList<KnowledgeSource> Knowledge,
    IReadOnlyList<HistoricalContent> ContentHistory);

internal sealed record ContentCopyRepairContext(
    ContentPlanGenerationInput GenerationInput,
    IReadOnlyList<GeneratedCopy> AcceptedCopies,
    int DayIndex,
    string RejectionReason);

public sealed record ContentPlanScheduleChange(
    Guid ProjectId,
    TimeSpan DailyPublishTimeLocal,
    string Timezone,
    DateTime NowUtc);
