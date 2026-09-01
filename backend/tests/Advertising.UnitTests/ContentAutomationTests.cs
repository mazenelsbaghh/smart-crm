using Amazon.S3;
using Modules.Brain.Domain;
using Modules.AI.Services;
using Modules.Content.API;
using Modules.Content.Domain;
using Modules.Content.Services;
using Modules.Projects.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Shared.Infrastructure;
using Shared.Security;
using Shared.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ContentAutomationTests
{
    [Fact]
    public async Task Approved_and_legacy_published_documents_are_ready_for_content_generation()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        dbContext.KnowledgeDocuments.AddRange(
            CreateKnowledgeDocument(projectId, "العرض المعتمد", "Approved"),
            CreateKnowledgeDocument(projectId, "المحتوى المنشور قديمًا", "Published"),
            CreateKnowledgeDocument(projectId, "مسودة", "Draft"),
            CreateKnowledgeDocument(Guid.NewGuid(), "مشروع آخر", "Approved"));
        await dbContext.SaveChangesAsync();

        var readyTitles = await dbContext.KnowledgeDocuments.IgnoreQueryFilters()
            .ReadyForGeneration(projectId)
            .OrderBy(document => document.Title)
            .Select(document => document.Title)
            .ToListAsync();

        Assert.Equal(["العرض المعتمد", "المحتوى المنشور قديمًا"], readyTitles);
    }

    [Theory]
    [InlineData("2026-08-25T06:30:00Z", "2026-08-25T07:00:00Z")]
    [InlineData("2026-08-25T08:30:00Z", "2026-08-26T07:00:00Z")]
    public void Daily_cairo_schedule_uses_the_next_local_ten_oclock_slot(
        string afterUtc,
        string expectedUtc)
    {
        var actual = ContentSchedule.NextUtc(
            DateTime.Parse(afterUtc).ToUniversalTime(),
            new TimeSpan(10, 0, 0),
            "Africa/Cairo");

        Assert.Equal(DateTime.Parse(expectedUtc).ToUniversalTime(), actual);
    }

    [Fact]
    public void Weekly_schedule_contains_seven_consecutive_cairo_days_starting_tomorrow()
    {
        var schedule = ContentSchedule.NextWeekUtc(
            DateTime.Parse("2026-08-25T20:30:00Z").ToUniversalTime(),
            new TimeSpan(10, 0, 0),
            "Africa/Cairo");

        Assert.Equal(7, schedule.Count);
        Assert.Equal(DateTime.Parse("2026-08-26T07:00:00Z").ToUniversalTime(), schedule[0]);
        Assert.Equal(DateTime.Parse("2026-09-01T07:00:00Z").ToUniversalTime(), schedule[6]);
        Assert.All(schedule.Zip(schedule.Skip(1)), pair => Assert.Equal(TimeSpan.FromDays(1), pair.Second - pair.First));
    }

    [Fact]
    public void Copy_prompt_includes_project_context_and_the_expected_json_contract()
    {
        var prompt = ContentGenerationService.BuildCopyPrompt(
            new ProjectSettings
            {
                AiTonePreference = "مصري مهني",
                AiTargetAudience = "شباب يبحثون عن عمل"
            },
            new ContentAutomationSettings { StylePrompt = "تحريري جريء" },
            [new KnowledgeSource("تفاصيل المنتج", "السعر والمزايا الصحيحة")],
            []);

        Assert.Contains("مصري مهني", prompt);
        Assert.Contains("شباب يبحثون عن عمل", prompt);
        Assert.Contains("تحريري جريء", prompt);
        Assert.Contains("تفاصيل المنتج", prompt);
        Assert.Contains("السعر والمزايا الصحيحة", prompt);
        Assert.Contains("\"topic\"", prompt);
        Assert.Contains("\"visualHeadline\"", prompt);
        Assert.Contains("\"caption\"", prompt);
        Assert.Contains("\"imagePrompt\"", prompt);
    }

    [Fact]
    public async Task Uploaded_logo_colors_are_extracted_for_the_generation_palette()
    {
        await using var logo = await TwoColorLogoAsync();
        var service = new LogoBrandingService();

        var palette = await service.ExtractPaletteAsync(logo, CancellationToken.None);

        Assert.Contains("#E51F33", palette);
        Assert.Contains("#1769E0", palette);
    }

    [Theory]
    [InlineData(0, (int)ContentVisualDirection.DarkEditorial)]
    [InlineData(1, (int)ContentVisualDirection.LightEditorial)]
    [InlineData(2, (int)ContentVisualDirection.DarkConceptual)]
    [InlineData(3, (int)ContentVisualDirection.LightConceptual)]
    [InlineData(4, (int)ContentVisualDirection.DarkEditorial)]
    public void Generated_posts_rotate_across_dark_and_light_agency_directions(
        int recentPostCount,
        int expectedDirection)
    {
        Assert.Equal((ContentVisualDirection)expectedDirection, ContentGenerationService.SelectVisualDirection(recentPostCount));
    }

    [Fact]
    public void Markdown_wrapped_llm_json_still_produces_a_complete_post()
    {
        const string response = """
            ```json
            {"topic":"نصيحة بيع","visualHeadline":"خليك أقرب","caption":"كابشن جاهز للنشر","imagePrompt":"A premium 3D visual"}
            ```
            """;

        var copy = ContentGenerationService.ParseCopy(response);

        Assert.Equal("نصيحة بيع", copy.Topic);
        Assert.Equal("خليك أقرب", copy.VisualHeadline);
        Assert.Equal("كابشن جاهز للنشر", copy.Caption);
        Assert.Equal("A premium 3D visual", copy.ImagePrompt);
    }

    [Fact]
    public void Llm_json_missing_a_publishable_field_is_rejected()
    {
        const string response =
            "{\"topic\":\"نصيحة\",\"visualHeadline\":\"عنوان\",\"caption\":\"كابشن\",\"imagePrompt\":\"\"}";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ContentGenerationService.ParseCopy(response));

        Assert.Contains("ينقصه جزء مطلوب", exception.Message);
    }

    [Fact]
    public void Headline_that_repeats_a_word_is_rejected_before_image_generation()
    {
        const string response =
            "{\"topic\":\"نصيحة\",\"visualHeadline\":\"ابدأ صح صح\",\"caption\":\"كابشن\",\"imagePrompt\":\"Visual\"}";

        Assert.Throws<InvalidOperationException>(() =>
            ContentGenerationService.ParseCopy(response));
    }

    [Fact]
    public void Overly_colloquial_caption_is_normalized_before_image_generation()
    {
        const string response =
            "{\"topic\":\"نصيحة\",\"visualHeadline\":\"ابدأ صح\",\"caption\":\"إزيك يا فندم، جرّب دلوقتي\",\"imagePrompt\":\"Visual\"}";

        var copy = ContentGenerationService.ParseCopy(response);

        Assert.Equal("جرّب الآن", copy.Caption);
    }

    [Fact]
    public void Dense_caption_is_split_into_readable_facebook_blocks()
    {
        const string caption = "قف لحظة! فكرة الكورس بتبدأ من التدريب العملي. جرّب بنفسك من هنا: https://example.com/try #TalkTips #كول_سنتر";

        var normalized = ContentGenerationService.NormalizeCaptionTone(caption);

        Assert.Equal(
            "قف لحظة!\n\nفكرة الكورس بتبدأ من التدريب العملي.\n\nجرّب بنفسك من هنا:\n\nhttps://example.com/try\n\n#TalkTips #كول_سنتر",
            normalized);
    }

    [Fact]
    public void Existing_caption_paragraphs_are_preserved_and_excess_whitespace_is_trimmed()
    {
        const string caption = "Hook مهم\\n\\n  جسم الفكرة هنا   بشكل واضح.\\n\\nاكتشف التفاصيل:\\nhttps://example.com\\n\\n#Brand";

        var normalized = ContentGenerationService.NormalizeCaptionTone(caption);

        Assert.Equal(
            "Hook مهم\n\nجسم الفكرة هنا بشكل واضح.\n\nاكتشف التفاصيل:\nhttps://example.com\n\n#Brand",
            normalized);
    }

    [Fact]
    public void Weekly_plan_requires_seven_distinct_complete_posts()
    {
        var response = JsonSerializer.Serialize(new
        {
            items = Enumerable.Range(1, 7).Select(day => new
            {
                topic = $"فكرة {day}",
                visualHeadline = $"عنوان {day}",
                caption = OriginalWeeklyCaption(day),
                imagePrompt = $"Visual concept {day}"
            })
        });

        var plan = ContentWeeklyPlanService.ParsePlan(response);

        Assert.Equal(7, plan.Count);
        Assert.Equal("فكرة 1", plan[0].Topic);
        Assert.Equal("عنوان 7", plan[6].VisualHeadline);
    }

    [Fact]
    public void Weekly_plan_with_a_duplicate_idea_is_rejected()
    {
        var response = JsonSerializer.Serialize(new
        {
            items = Enumerable.Range(1, 7).Select(day => new
            {
                topic = day == 7 ? "فكرة 1" : $"فكرة {day}",
                visualHeadline = $"عنوان {day}",
                caption = OriginalWeeklyCaption(day),
                imagePrompt = $"Visual concept {day}"
            })
        });

        Assert.Throws<InvalidOperationException>(() =>
            ContentWeeklyPlanService.ParsePlan(response));
    }

    [Fact]
    public void Weekly_plan_cannot_repeat_a_normalized_topic_or_headline_from_history()
    {
        var generatedPlan = Enumerable.Range(1, 7)
            .Select(day => new GeneratedCopy(
                day == 1 ? "ابدأ بثقة" : $"فكرة جديدة {day}",
                $"عنوان جديد {day}",
                $"كابشن {day}",
                $"Visual concept {day}"))
            .ToArray();
        HistoricalContent[] history = [new("أبدأ... بثقة!", "عنوان قديم")];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ContentWeeklyPlanService.EnsureNoHistoricalRepeats(generatedPlan, history));

        Assert.Contains("المحتوى السابق", exception.Message);
    }

    [Fact]
    public void Caption_cannot_copy_a_seven_word_sequence_from_the_knowledge_base()
    {
        var copy = new GeneratedCopy(
            "فكرة موقف واقعي",
            "ابدأ بطريقتك",
            "في بداية الرحلة ممكن تكون الخطوة مربكة، لكن الوضوح يغيّر شكل القرار. شرطنا الوحيد هو الالتزام فقط لا غير، وبعدها تقدر تبني عادة صغيرة كل يوم وتلاحظ الفرق في ثقتك وطريقة تعاملك مع المواقف. احفظ الفكرة وارجع لها وقت ما تحتاج تبدأ بهدوء.",
            "A person taking a confident first step");
        KnowledgeSource[] knowledge =
        [
            new("دليل الخدمة", "مش محتاج مستوى محدد. شرطنا الوحيد هو الالتزام فقط لا غير، وبعدها يبدأ التدريب.")
        ];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ContentCaptionOriginality.EnsureStandaloneCopy(copy, knowledge, []));

        Assert.Contains("قاعدة المعرفة", exception.Message);
    }

    [Fact]
    public void Caption_cannot_recycle_a_previous_caption_under_a_new_topic_and_headline()
    {
        const string recycledCaption =
            "أحيانًا المشكلة مش في قدراتك، لكنها في إنك بتحاول تبدأ من مكان مش مناسب. خذ خطوة صغيرة وحدد موقفًا واحدًا تتدرب عليه اليوم، وبعد أسبوع قارن بين أول محاولة وآخر محاولة. التطور الحقيقي يظهر في التفاصيل البسيطة اللي بتتكرر، فاحفظ التمرين وابدأ وقت ما تكون جاهزًا.";
        var copy = new GeneratedCopy(
            "فكرة مختلفة بالاسم",
            "خطوة جديدة",
            recycledCaption,
            "A minimal staircase concept");
        HistoricalContent[] history =
        [
            new("فكرة قديمة", "عنوان قديم", recycledCaption)
        ];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ContentCaptionOriginality.EnsureStandaloneCopy(copy, [], history));

        Assert.Contains("كابشن قديم", exception.Message);
    }

    [Fact]
    public void Weekly_captions_cannot_share_the_same_body_sentence()
    {
        var first = new GeneratedCopy(
            "موقف أول",
            "المحاولة الأولى",
            "المشهد يبدأ قبل المقابلة بدقائق، لما تحس إن كل الكلمات اختفت فجأة. الممارسة اليومية تحول هذا التوتر إلى رد فعل هادئ وواضح. اختار موقف واحد وابدأ تدرب عليه بصوت مسموع كل يوم عشان تشوف الفرق بنفسك. شارك التمرين مع شخص محتاج يبدأ من غير ضغط.",
            "A calm interview waiting room");
        var second = new GeneratedCopy(
            "موقف ثان",
            "الثقة بالتدريب",
            "الثقة مش جملة بنقولها، لكنها نتيجة مواقف صغيرة اتكررت بوعي. الممارسة اليومية تحول هذا التوتر إلى رد فعل هادئ وواضح. سجّل إجابة قصيرة واسمعها بعد يومين، وركز على نقطة واحدة تتحسن بدل ما تحاول تصلح كل حاجة مرة واحدة. إيه أول موقف تحب تتدرب عليه؟",
            "A voice note becoming a clear waveform");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ContentCaptionOriginality.EnsureWeeklyPlan([first, second], [], []));

        Assert.Contains("متشابهان", exception.Message);
    }

    [Fact]
    public async Task Approving_a_complete_week_plan_enables_only_its_first_publish_slot()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = "logos/current.png",
            StylePrompt = "الستايل الحالي",
            FacebookPageId = "page-1",
            HasApprovedStyle = true,
            LastPublishedAtUtc = DateTime.UtcNow
        };
        var plan = new ContentWeekPlan
        {
            ProjectId = projectId,
            Status = ContentWeekPlanStatus.AwaitingApproval,
            StartDateLocal = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            DailyPublishTimeLocal = new TimeSpan(10, 0, 0),
            BrandLogoObjectKey = settings.LogoObjectKey,
            BrandStylePrompt = settings.StylePrompt
        };
        dbContext.AddRange(settings, plan);
        var firstSlot = AddPlanItemsWithImages(
            dbContext,
            projectId,
            plan,
            Enumerable.Repeat(ContentPostStatus.Approved, 7).ToArray());
        await dbContext.SaveChangesAsync();
        var service = new ContentWeeklyPlanService(
            dbContext,
            null!,
            null!,
            NullLogger<ContentWeeklyPlanService>.Instance);

        await service.ApproveAsync(projectId, plan.Id, CancellationToken.None);
        var nextPublishAtUtc = await service.NextApprovedPublishAtAsync(projectId, CancellationToken.None);

        Assert.Equal(ContentWeekPlanStatus.Approved, plan.Status);
        Assert.True(settings.IsEnabled);
        Assert.Equal(firstSlot, settings.NextPublishAtUtc);
        Assert.Equal(firstSlot, nextPublishAtUtc);
    }

    [Fact]
    public async Task Approving_an_additional_week_keeps_the_current_weeks_earlier_publish_slot()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = "logos/current.png",
            StylePrompt = "الستايل الحالي",
            FacebookPageId = "page-1",
            HasApprovedStyle = true,
            IsEnabled = true,
            LastPublishedAtUtc = DateTime.UtcNow
        };
        var currentPlan = NewTestPlan(projectId, settings, ContentWeekPlanStatus.Approved);
        var futurePlan = NewTestPlan(projectId, settings, ContentWeekPlanStatus.AwaitingApproval);
        dbContext.AddRange(settings, currentPlan, futurePlan);
        var currentSlot = AddPlanItemsWithImages(
            dbContext,
            projectId,
            currentPlan,
            Enumerable.Repeat(ContentPostStatus.Approved, 7).ToArray());
        AddPlanItemsWithImages(
            dbContext,
            projectId,
            futurePlan,
            Enumerable.Repeat(ContentPostStatus.Approved, 7).ToArray());
        foreach (var item in dbContext.ContentWeekPlanItems.Local.Where(item => item.PlanId == futurePlan.Id))
            item.ScheduledForUtc = item.ScheduledForUtc.AddDays(7);
        settings.NextPublishAtUtc = currentSlot;
        await dbContext.SaveChangesAsync();
        var service = new ContentWeeklyPlanService(
            dbContext,
            null!,
            null!,
            NullLogger<ContentWeeklyPlanService>.Instance);

        await service.ApproveAsync(projectId, futurePlan.Id, CancellationToken.None);

        Assert.Equal(ContentWeekPlanStatus.Approved, futurePlan.Status);
        Assert.Equal(currentSlot, settings.NextPublishAtUtc);
        Assert.Equal(currentSlot, await service.NextApprovedPublishAtAsync(projectId, CancellationToken.None));
    }

    [Fact]
    public async Task Changing_publish_time_after_approval_preserves_plan_2026_08_26_regression()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var plan = new ContentWeekPlan
        {
            ProjectId = projectId,
            Status = ContentWeekPlanStatus.Approved,
            StartDateLocal = new DateOnly(2026, 8, 26),
            DailyPublishTimeLocal = new TimeSpan(10, 0, 0),
            Timezone = "Africa/Cairo"
        };
        dbContext.ContentWeekPlans.Add(plan);
        AddPlanItemsWithImages(
            dbContext,
            projectId,
            plan,
            Enumerable.Repeat(ContentPostStatus.Approved, 7).ToArray());
        await dbContext.SaveChangesAsync();
        var firstItem = await dbContext.ContentWeekPlanItems
            .OrderBy(item => item.DayIndex)
            .FirstAsync();
        var firstPost = await dbContext.ContentPosts.SingleAsync(post => post.Id == firstItem.ContentPostId);
        firstPost.Status = ContentPostStatus.Published;
        firstPost.PublishedAtUtc = DateTime.Parse("2026-08-26T07:00:00Z").ToUniversalTime();
        await dbContext.SaveChangesAsync();
        var service = new ContentWeeklyPlanService(
            dbContext,
            null!,
            null!,
            NullLogger<ContentWeeklyPlanService>.Instance);

        var nextPublishAtUtc = await service.RescheduleActivePlanAsync(
            new ContentPlanScheduleChange(
                projectId,
                new TimeSpan(22, 0, 0),
                "Africa/Cairo",
                DateTime.Parse("2026-08-26T13:00:00Z").ToUniversalTime()),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(ContentWeekPlanStatus.Approved, plan.Status);
        Assert.Equal(new TimeSpan(22, 0, 0), plan.DailyPublishTimeLocal);
        Assert.Equal(DateTime.Parse("2026-08-27T19:00:00Z").ToUniversalTime(), nextPublishAtUtc);
        var remainingItems = await dbContext.ContentWeekPlanItems
            .Where(item => item.PlanId == plan.Id && item.DayIndex > 0)
            .OrderBy(item => item.DayIndex)
            .ToListAsync();
        DateTime[] expectedSlots =
        [
            DateTime.Parse("2026-08-27T19:00:00Z").ToUniversalTime(),
            DateTime.Parse("2026-08-28T19:00:00Z").ToUniversalTime(),
            DateTime.Parse("2026-08-29T19:00:00Z").ToUniversalTime(),
            DateTime.Parse("2026-08-30T19:00:00Z").ToUniversalTime(),
            DateTime.Parse("2026-08-31T19:00:00Z").ToUniversalTime(),
            DateTime.Parse("2026-09-01T19:00:00Z").ToUniversalTime()
        ];
        Assert.Equal(expectedSlots, remainingItems.Select(planItem => planItem.ScheduledForUtc));
        var remainingPosts = await dbContext.ContentPosts
            .Where(post => remainingItems.Select(planItem => planItem.ContentPostId).Contains(post.Id))
            .ToListAsync();
        Assert.All(remainingPosts, post => Assert.Equal(ContentPostStatus.Approved, post.Status));
        Assert.Equal(
            remainingItems.Select(planItem => planItem.ScheduledForUtc).OrderBy(slot => slot),
            remainingPosts.Select(post => post.ScheduledForUtc!.Value).OrderBy(slot => slot));
    }

    [Fact]
    public async Task Changing_publish_time_reschedules_all_prepared_weeks_consecutively()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = "logos/current.png",
            StylePrompt = "الستايل الحالي"
        };
        var firstPlan = NewTestPlan(projectId, settings, ContentWeekPlanStatus.Approved);
        var secondPlan = NewTestPlan(projectId, settings, ContentWeekPlanStatus.Approved);
        secondPlan.StartDateLocal = firstPlan.StartDateLocal.AddDays(7);
        dbContext.AddRange(firstPlan, secondPlan);
        AddPlanItemsWithImages(
            dbContext,
            projectId,
            firstPlan,
            Enumerable.Repeat(ContentPostStatus.Approved, 7).ToArray());
        AddPlanItemsWithImages(
            dbContext,
            projectId,
            secondPlan,
            Enumerable.Repeat(ContentPostStatus.Approved, 7).ToArray());
        foreach (var item in dbContext.ContentWeekPlanItems.Local.Where(item => item.PlanId == secondPlan.Id))
            item.ScheduledForUtc = item.ScheduledForUtc.AddDays(7);
        await dbContext.SaveChangesAsync();
        var service = new ContentWeeklyPlanService(
            dbContext,
            null!,
            null!,
            NullLogger<ContentWeeklyPlanService>.Instance);

        var firstSlot = await service.RescheduleActivePlanAsync(
            new ContentPlanScheduleChange(
                projectId,
                new TimeSpan(22, 0, 0),
                "Africa/Cairo",
                DateTime.Parse("2026-08-27T12:00:00Z").ToUniversalTime()),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var slots = await dbContext.ContentWeekPlanItems
            .OrderBy(item => item.ScheduledForUtc)
            .Select(item => item.ScheduledForUtc)
            .ToListAsync();

        Assert.Equal(DateTime.Parse("2026-08-27T19:00:00Z").ToUniversalTime(), firstSlot);
        Assert.Equal(14, slots.Count);
        Assert.All(slots.Zip(slots.Skip(1)), pair => Assert.Equal(TimeSpan.FromDays(1), pair.Second - pair.First));
        Assert.Equal(slots[7], TimeZoneInfo.ConvertTimeToUtc(
            secondPlan.StartDateLocal.ToDateTime(new TimeOnly(22, 0)),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")));
    }

    [Fact]
    public async Task Weekly_plan_cannot_start_until_every_image_is_approved()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = "logos/current.png",
            StylePrompt = "الستايل الحالي",
            FacebookPageId = "page-1"
        };
        var plan = new ContentWeekPlan
        {
            ProjectId = projectId,
            Status = ContentWeekPlanStatus.AwaitingApproval,
            BrandLogoObjectKey = settings.LogoObjectKey,
            BrandStylePrompt = settings.StylePrompt
        };
        dbContext.AddRange(settings, plan);
        var statuses = Enumerable.Repeat(ContentPostStatus.Approved, 7).ToArray();
        statuses[6] = ContentPostStatus.AwaitingApproval;
        AddPlanItemsWithImages(dbContext, projectId, plan, statuses);
        await dbContext.SaveChangesAsync();
        var service = new ContentWeeklyPlanService(
            dbContext,
            null!,
            null!,
            NullLogger<ContentWeeklyPlanService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(projectId, plan.Id, CancellationToken.None));

        Assert.Contains("صورة كل يوم", exception.Message);
        Assert.False(settings.IsEnabled);
    }

    [Fact]
    public async Task Approving_a_weekly_image_marks_only_its_linked_preview_ready()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var plan = new ContentWeekPlan
        {
            ProjectId = projectId,
            Status = ContentWeekPlanStatus.AwaitingApproval,
            BrandLogoObjectKey = "logos/current.png",
            BrandStylePrompt = "الستايل الحالي"
        };
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = plan.BrandLogoObjectKey,
            StylePrompt = plan.BrandStylePrompt
        };
        var post = new ContentPost
        {
            ProjectId = projectId,
            Status = ContentPostStatus.AwaitingApproval,
            ImageObjectKey = "content/project/day-1.png"
        };
        var item = new ContentWeekPlanItem
        {
            ProjectId = projectId,
            PlanId = plan.Id,
            ContentPostId = post.Id
        };
        dbContext.AddRange(plan, settings, post, item);
        await dbContext.SaveChangesAsync();
        var service = new ContentWeeklyPlanService(
            dbContext,
            null!,
            null!,
            NullLogger<ContentWeeklyPlanService>.Instance);

        await service.ApproveItemAsync(projectId, plan.Id, item.Id, CancellationToken.None);

        Assert.Equal(ContentPostStatus.Approved, post.Status);
        Assert.NotNull(post.ApprovedAtUtc);
        Assert.Equal(ContentWeekPlanStatus.AwaitingApproval, plan.Status);
    }

    [Fact]
    public async Task Three_rejected_weekly_drafts_are_replaced_before_the_plan_is_saved()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = "logos/current.png",
            StylePrompt = "الستايل الحالي",
            HasApprovedStyle = true,
            LastPublishedAtUtc = DateTime.UtcNow,
            IsEnabled = true,
            NextPublishAtUtc = DateTime.UtcNow.AddDays(1)
        };
        dbContext.AddRange(
            settings,
            new ProjectSettings { ProjectId = projectId, GeminiApiKey = "test-key" },
            CreateKnowledgeDocument(projectId, "الخدمة", "Approved"));
        await dbContext.SaveChangesAsync();
        var validResponse = JsonSerializer.Serialize(new
        {
            items = Enumerable.Range(1, 7).Select(day => new
            {
                topic = $"فكرة {day}",
                visualHeadline = $"عنوان {day}",
                caption = OriginalWeeklyCaption(day),
                imagePrompt = $"Visual concept {day}"
            })
        });
        var gemini = new SequencedGeminiClient(
            "{\"items\":[]}",
            "{\"items\":[]}",
            "{\"items\":[]}",
            validResponse);
        var service = new ContentWeeklyPlanService(
            dbContext,
            gemini,
            new PassThroughSecretVault(),
            NullLogger<ContentWeeklyPlanService>.Instance);

        var plan = await service.GenerateAsync(projectId, CancellationToken.None);

        Assert.Equal(ContentWeekPlanStatus.Generating, plan.Status);
        Assert.False(settings.IsEnabled);
        Assert.Null(settings.NextPublishAtUtc);
        Assert.Equal(7, await dbContext.ContentWeekPlanItems.CountAsync(item => item.PlanId == plan.Id));
    }

    [Fact]
    public async Task Production_2026_08_27_repeated_caption_is_repaired_without_restarting_the_week()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = "logos/current.png",
            StylePrompt = "الستايل الحالي",
            HasApprovedStyle = true,
            LastPublishedAtUtc = DateTime.UtcNow
        };
        const string historicalCaption =
            "أحيانًا البداية بتبان معقدة لأنك بتحاول تجمع كل المهارات في خطوة واحدة. اختار موقفًا صغيرًا من يومك، وسجّل ردك عليه، وبعدها اسمع المحاولة وحدد نقطة واحدة فقط للتحسين. التقدم الحقيقي بيظهر لما تكرر التمرين بهدوء وتقارن النتيجة بعد أيام. احتفظ بالملاحظة وارجع لها نهاية الأسبوع.";
        dbContext.AddRange(
            settings,
            new ProjectSettings { ProjectId = projectId, GeminiApiKey = "test-key" },
            CreateKnowledgeDocument(projectId, "الخدمة", "Approved"),
            new ContentPost
            {
                ProjectId = projectId,
                Status = ContentPostStatus.Published,
                Topic = "فكرة منشورة",
                VisualHeadline = "عنوان منشور",
                Caption = historicalCaption
            });
        await dbContext.SaveChangesAsync();
        var weeklyDraft = JsonSerializer.Serialize(new
        {
            items = Enumerable.Range(1, 7).Select(day => new
            {
                topic = $"فكرة {day}",
                visualHeadline = $"عنوان {day}",
                caption = day == 3 ? historicalCaption : OriginalWeeklyCaption(day),
                imagePrompt = $"Visual concept {day}"
            })
        });
        var repairedThirdDay = JsonSerializer.Serialize(new
        {
            topic = "فكرة 3",
            visualHeadline = "عنوان 3",
            caption = OriginalWeeklyCaption(3),
            imagePrompt = "Visual concept 3"
        });
        var service = new ContentWeeklyPlanService(
            dbContext,
            new SequencedGeminiClient(weeklyDraft, JsonSerializer.Serialize(new
            {
                topic = "فكرة مكررة",
                visualHeadline = "عنوان مكرر",
                caption = historicalCaption,
                imagePrompt = "Repeated visual concept"
            }), repairedThirdDay),
            new PassThroughSecretVault(),
            NullLogger<ContentWeeklyPlanService>.Instance);

        var plan = await service.GenerateAsync(projectId, CancellationToken.None);
        var savedThirdDay = await dbContext.ContentWeekPlanItems
            .SingleAsync(planItem => planItem.PlanId == plan.Id && planItem.DayIndex == 2);

        Assert.Equal(ContentWeekPlanStatus.Generating, plan.Status);
        Assert.Equal(7, await dbContext.ContentWeekPlanItems.CountAsync(planItem => planItem.PlanId == plan.Id));
        Assert.Equal(
            ContentGenerationService.NormalizeCaptionTone(OriginalWeeklyCaption(3)),
            savedThirdDay.Caption);
    }

    [Fact]
    public async Task Additional_week_starts_after_the_last_reserved_day_without_stopping_the_current_week()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var currentNextPublishAtUtc = DateTime.UtcNow.AddDays(1);
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = "logos/current.png",
            StylePrompt = "الستايل الحالي",
            HasApprovedStyle = true,
            LastPublishedAtUtc = DateTime.UtcNow,
            IsEnabled = true,
            NextPublishAtUtc = currentNextPublishAtUtc
        };
        var approvedPlan = NewTestPlan(projectId, settings, ContentWeekPlanStatus.Approved);
        var historicalFailedPlan = NewTestPlan(projectId, settings, ContentWeekPlanStatus.GenerationFailed);
        dbContext.ContentWeekPlans.Add(historicalFailedPlan);
        await dbContext.SaveChangesAsync();
        historicalFailedPlan.CreatedAt = DateTime.UtcNow.AddDays(-2);
        await dbContext.SaveChangesAsync();
        dbContext.AddRange(
            settings,
            approvedPlan,
            new ProjectSettings { ProjectId = projectId, GeminiApiKey = "test-key" },
            CreateKnowledgeDocument(projectId, "الخدمة", "Approved"));
        AddPlanItemsWithImages(
            dbContext,
            projectId,
            approvedPlan,
            Enumerable.Repeat(ContentPostStatus.Approved, 7).ToArray());
        await dbContext.SaveChangesAsync();
        var finalReservedSlot = await dbContext.ContentWeekPlanItems
            .Where(item => item.PlanId == approvedPlan.Id)
            .MaxAsync(item => item.ScheduledForUtc);
        var validResponse = JsonSerializer.Serialize(new
        {
            items = Enumerable.Range(1, 7).Select(day => new
            {
                topic = $"فكرة إضافية {day}",
                visualHeadline = $"عنوان إضافي {day}",
                caption = OriginalWeeklyCaption(day),
                imagePrompt = $"Additional visual concept {day}"
            })
        });
        var service = new ContentWeeklyPlanService(
            dbContext,
            new SequencedGeminiClient(validResponse),
            new PassThroughSecretVault(),
            NullLogger<ContentWeeklyPlanService>.Instance);

        var additionalPlan = await service.GenerateAsync(projectId, CancellationToken.None);
        var firstAdditionalSlot = await dbContext.ContentWeekPlanItems
            .Where(item => item.PlanId == additionalPlan.Id)
            .MinAsync(item => item.ScheduledForUtc);
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        Assert.NotEqual(approvedPlan.Id, additionalPlan.Id);
        Assert.Equal(
            TimeZoneInfo.ConvertTimeFromUtc(finalReservedSlot, timezone).Date.AddDays(1),
            TimeZoneInfo.ConvertTimeFromUtc(firstAdditionalSlot, timezone).Date);
        Assert.True(settings.IsEnabled);
        Assert.Equal(currentNextPublishAtUtc, settings.NextPublishAtUtc);
    }

    [Theory]
    [InlineData("logos/old.png", "الستايل الحالي", "اللوجو")]
    [InlineData("logos/current.png", "ستايل قديم", "شكل التصميم")]
    public async Task Post_with_outdated_brand_identity_is_blocked_before_facebook(
        string postLogoObjectKey,
        string postStylePrompt,
        string expectedReason)
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = "logos/current.png",
            StylePrompt = "الستايل الحالي",
            FacebookPageId = "page-1"
        };
        var post = new ContentPost
        {
            ProjectId = projectId,
            SettingsId = settings.Id,
            Status = ContentPostStatus.Approved,
            BrandLogoObjectKey = postLogoObjectKey,
            BrandStylePrompt = postStylePrompt,
            ImageObjectKey = "posts/post.png"
        };
        dbContext.AddRange(settings, post);
        await dbContext.SaveChangesAsync();
        var service = new ContentPublishingService(
            dbContext,
            new UnexpectedObjectStorage(),
            new FacebookPhotoPublisher(new HttpClient()),
            NullLogger<ContentPublishingService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PublishAsync(projectId, post.Id, CancellationToken.None));

        Assert.Contains(expectedReason, exception.Message);
        Assert.Equal(ContentPostStatus.Approved, post.Status);
    }

    [Fact]
    public async Task Image_generation_uses_supported_pro_model_for_square_4k_contract()
    {
        var handler = new RecordingImageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var client = new GeminiImageClient(httpClient);

        var image = await client.GenerateAsync(
            new GeminiImageRequest(
                "Generate the approved brand poster",
                "project-gemini-key",
                new byte[] { 9, 8, 7 },
                "image/jpeg"),
            CancellationToken.None);

        Assert.Equal("v1beta/models/gemini-3-pro-image:generateContent", handler.RequestUri);
        Assert.Equal("project-gemini-key", handler.ApiKey);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        var imageFormat = request.RootElement
            .GetProperty("generationConfig")
            .GetProperty("responseFormat")
            .GetProperty("image");
        var logoReference = request.RootElement
            .GetProperty("contents")[0]
            .GetProperty("parts")[1]
            .GetProperty("inlineData");
        Assert.Equal("IMAGE_SIZE_FOUR_K", imageFormat.GetProperty("imageSize").GetString());
        Assert.Equal("ASPECT_RATIO_ONE_BY_ONE", imageFormat.GetProperty("aspectRatio").GetString());
        Assert.Equal("image/jpeg", logoReference.GetProperty("mimeType").GetString());
        Assert.Equal("CQgH", logoReference.GetProperty("data").GetString());
        Assert.Equal(GeminiImageClient.HighestQualityModel, image.Model);
        Assert.Equal("4K", image.Size);
        Assert.Equal(new byte[] { 1, 2, 3 }, image.Bytes);
    }

    [Fact]
    public async Task Production_regression_private_content_assets_use_authenticated_api_routes()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = "content/project/brand/logo.png",
            LogoMimeType = "image/png"
        };
        var post = new ContentPost
        {
            ProjectId = projectId,
            SettingsId = settings.Id,
            ImageObjectKey = "content/project/post.png",
            ImageMimeType = "image/png",
            Status = ContentPostStatus.AwaitingApproval
        };
        var plan = new ContentWeekPlan
        {
            ProjectId = projectId,
            Status = ContentWeekPlanStatus.AwaitingApproval
        };
        var item = new ContentWeekPlanItem
        {
            ProjectId = projectId,
            PlanId = plan.Id,
            ContentPostId = post.Id
        };
        dbContext.AddRange(settings, post, plan, item);
        await dbContext.SaveChangesAsync();
        await using var originalImage = await TwoColorLogoAsync();
        var storage = new DownloadableContentStorage(
            post.ImageObjectKey,
            originalImage.ToArray(),
            settings.LogoObjectKey);
        var controller = CreateContentController(dbContext, projectId, storage);

        var studio = Assert.IsType<OkObjectResult>(await controller.Get(CancellationToken.None));
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(studio.Value));
        Assert.Equal(ContentAssetRoutes.Logo(settings.UpdatedAt), response.RootElement.GetProperty("settings").GetProperty("logoUrl").GetString());
        Assert.Equal(ContentAssetRoutes.PostImage(post.Id), response.RootElement.GetProperty("posts")[0].GetProperty("imageUrl").GetString());
        Assert.Equal(ContentAssetRoutes.PostImage(post.Id), response.RootElement.GetProperty("weeklyPlan").GetProperty("items")[0].GetProperty("imageUrl").GetString());

        var image = Assert.IsType<FileStreamResult>(await controller.GetPostImage(post.Id, CancellationToken.None));
        using var preview = await Image.LoadAsync(image.FileStream);
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.Equal(40, preview.Width);
        Assert.Equal(20, preview.Height);
        Assert.Equal(1, storage.UploadCount);

        var cachedImage = Assert.IsType<FileStreamResult>(await controller.GetPostImage(post.Id, CancellationToken.None));
        await cachedImage.FileStream.DisposeAsync();
        Assert.Equal(1, storage.UploadCount);

        var logo = Assert.IsType<FileStreamResult>(await controller.GetLogoFile(CancellationToken.None));
        using var logoBytes = new MemoryStream();
        await logo.FileStream.CopyToAsync(logoBytes);
        Assert.Equal("image/png", logo.ContentType);
        Assert.Equal(Encoding.UTF8.GetBytes(settings.LogoObjectKey), logoBytes.ToArray());
    }

    [Fact]
    public async Task Latest_rejected_plan_does_not_resurface_an_old_failure_2026_08_26_regression()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var oldFailedPlan = new ContentWeekPlan
        {
            ProjectId = projectId,
            Status = ContentWeekPlanStatus.GenerationFailed,
            Error = "خطة قديمة فشلت"
        };
        dbContext.ContentWeekPlans.Add(oldFailedPlan);
        await dbContext.SaveChangesAsync();
        oldFailedPlan.CreatedAt = DateTime.UtcNow.AddDays(-1);
        await dbContext.SaveChangesAsync();
        var latestRejectedPlan = new ContentWeekPlan
        {
            ProjectId = projectId,
            Status = ContentWeekPlanStatus.Rejected,
            Error = "تم تغيير الموعد"
        };
        dbContext.ContentWeekPlans.Add(latestRejectedPlan);
        await dbContext.SaveChangesAsync();
        var controller = CreateContentController(dbContext, projectId, new UnexpectedObjectStorage());

        var studio = Assert.IsType<OkObjectResult>(await controller.Get(CancellationToken.None));
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(studio.Value));
        var displayedPlan = response.RootElement.GetProperty("weeklyPlan");

        Assert.Equal(latestRejectedPlan.Id, displayedPlan.GetProperty("Id").GetGuid());
        Assert.Equal("Rejected", displayedPlan.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Approved_plan_remains_visible_after_a_later_replacement_is_rejected()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var approvedPlan = new ContentWeekPlan
        {
            ProjectId = projectId,
            Status = ContentWeekPlanStatus.Approved
        };
        dbContext.ContentWeekPlans.Add(approvedPlan);
        await dbContext.SaveChangesAsync();
        var rejectedReplacement = new ContentWeekPlan
        {
            ProjectId = projectId,
            Status = ContentWeekPlanStatus.Rejected
        };
        dbContext.ContentWeekPlans.Add(rejectedReplacement);
        await dbContext.SaveChangesAsync();
        var controller = CreateContentController(dbContext, projectId, new UnexpectedObjectStorage());

        var studio = Assert.IsType<OkObjectResult>(await controller.Get(CancellationToken.None));
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(studio.Value));
        var displayedPlan = response.RootElement.GetProperty("weeklyPlan");

        Assert.Equal(approvedPlan.Id, displayedPlan.GetProperty("Id").GetGuid());
        Assert.Equal("Approved", displayedPlan.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Studio_returns_all_current_and_future_week_plans_in_schedule_order()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var settings = new ContentAutomationSettings
        {
            ProjectId = projectId,
            LogoObjectKey = "logos/current.png",
            StylePrompt = "الستايل الحالي"
        };
        var currentPlan = NewTestPlan(projectId, settings, ContentWeekPlanStatus.Approved);
        var futurePlan = NewTestPlan(projectId, settings, ContentWeekPlanStatus.AwaitingApproval);
        futurePlan.StartDateLocal = currentPlan.StartDateLocal.AddDays(7);
        dbContext.AddRange(currentPlan, futurePlan);
        await dbContext.SaveChangesAsync();
        var controller = CreateContentController(dbContext, projectId, new UnexpectedObjectStorage());

        var studio = Assert.IsType<OkObjectResult>(await controller.Get(CancellationToken.None));
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(studio.Value));
        var plans = response.RootElement.GetProperty("weeklyPlans");

        Assert.Equal(2, plans.GetArrayLength());
        Assert.Equal(currentPlan.Id, plans[0].GetProperty("Id").GetGuid());
        Assert.Equal(futurePlan.Id, plans[1].GetProperty("Id").GetGuid());
    }

    private static async Task<MemoryStream> TwoColorLogoAsync()
    {
        using var image = new Image<Rgba32>(40, 20);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                image[x, y] = x < 20
                    ? new Rgba32(229, 31, 51, 255)
                    : new Rgba32(23, 105, 224, 255);
            }
        }

        var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }

    private static AppDbContext CreateDbContext(Guid projectId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetProjectId(projectId);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantContext,
            new ServiceCollection().BuildServiceProvider());
    }

    private static string OriginalWeeklyCaption(int day) => day switch
    {
        1 => "قبل أول مقابلة بدقائق، ممكن تنسى كل الجمل اللي حضرتها وتفتكر بس صوت القلق. التدريب على موقف واحد بصوت مسموع يخلي ردك أهدى في المرة التالية، لأن الثقة بتتبني من محاولات صغيرة متكررة. اكتب أصعب سؤال قابلته وخليه نقطة البداية.",
        2 => "الشهادة تثبت إنك درست، لكنها مش دايمًا تثبت إنك تعرف تتصرف وقت مكالمة حقيقية. جرّب تقارن بين إجابة محفوظة ورد طبيعي على عميل متردد، وهتلاحظ إن المرونة هي الفارق الأوضح. شارك المنشور مع شخص بيستعد لأول فرصة مهنية.",
        3 => "خمس دقائق كفاية لاختبار عادة جديدة اليوم. اختر موقف خدمة بسيط، سجّل ردك بالإنجليزية، واسمعه مرة من غير ما تحكم على نفسك. ركّز فقط على وضوح الفكرة، وبكرة كرر المحاولة بنبرة أهدى. احفظ التمرين وارجع له نهاية الأسبوع.",
        4 => "طريق طويل مليان مستويات، أو تدريب مركز مرتبط بالموقف اللي هتقابله في الشغل؟ الاختيار مش متعلق بالسرعة وحدها، لكنه متعلق بوضوح النتيجة اللي بتدور عليها. حدد المهارة الأهم بالنسبة لك أولًا، وبعدها قارن البرامج على أساسها. أي معيار بيحسم قرارك؟",
        5 => "موقف واحد ممكن يكشف أكتر من قائمة مهارات كاملة: عميل غاضب، وقت محدود، ومعلومة ناقصة. هنا يظهر الفرق بين حفظ الكلمات وفهم طريقة التواصل. تخيل إن المكالمة بدأت الآن؛ ما أول جملة هتقولها عشان تهدي الحوار وتجمع التفاصيل الصحيحة؟",
        6 => "التقدم ساعات بيكون هادي لدرجة إنك ما تلاحظوش يوم بيوم. كلمة نطقتها أوضح، سؤال فهمته أسرع، أو لحظة تردد اختفت. اعمل علامة أسبوعية بسيطة وسجّل تغييرًا واحدًا فقط، لأن متابعة التفاصيل الصغيرة بتوضح المسافة اللي قطعتها. احتفظ بالفكرة لمراجعة الجمعة.",
        7 => "البداية القوية مش معناها إنك تعرف كل حاجة، لكنها تعني إنك عارف الخطوة التالية. اختار مهارة واحدة تحتاجها في بيئة العمل وخلي تدريبك الأسبوع ده مركز عليها فقط، بدل التنقل بين أهداف كثيرة. ابعت التحدي لزميلك واتفقوا تراجعوا النتيجة سوا.",
        _ => throw new ArgumentOutOfRangeException(nameof(day))
    };

    private static DateTime AddPlanItemsWithImages(
        AppDbContext dbContext,
        Guid projectId,
        ContentWeekPlan plan,
        IReadOnlyList<ContentPostStatus> statuses)
    {
        var firstSlot = DateTime.UtcNow.AddDays(1);
        foreach (var day in Enumerable.Range(0, statuses.Count))
        {
            var post = new ContentPost
            {
                ProjectId = projectId,
                Status = statuses[day],
                ImageObjectKey = $"content/{projectId:N}/day-{day}.png"
            };
            dbContext.Add(post);
            dbContext.ContentWeekPlanItems.Add(new ContentWeekPlanItem
            {
                ProjectId = projectId,
                PlanId = plan.Id,
                DayIndex = day,
                ScheduledForUtc = firstSlot.AddDays(day),
                Topic = $"فكرة {day}",
                VisualHeadline = $"عنوان {day}",
                Caption = $"كابشن {day}",
                ImagePrompt = $"Visual {day}",
                ContentPostId = post.Id
            });
        }
        return firstSlot;
    }

    private static ContentWeekPlan NewTestPlan(
        Guid projectId,
        ContentAutomationSettings settings,
        ContentWeekPlanStatus status) => new()
    {
        ProjectId = projectId,
        Status = status,
        StartDateLocal = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
        DailyPublishTimeLocal = new TimeSpan(10, 0, 0),
        Timezone = "Africa/Cairo",
        BrandLogoObjectKey = settings.LogoObjectKey!,
        BrandStylePrompt = settings.StylePrompt
    };

    private static ContentController CreateContentController(
        AppDbContext dbContext,
        Guid projectId,
        IObjectStorage objectStorage)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetProjectId(projectId);
        var identity = new ClaimsIdentity(
            [new Claim("ProjectId", projectId.ToString())],
            "content-test");
        return new ContentController(
            dbContext,
            tenantContext,
            new ProjectAuthorizationService(),
            objectStorage,
            new LogoBrandingService(),
            new ContentImagePreviewService(
                objectStorage,
                NullLogger<ContentImagePreviewService>.Instance),
            null!,
            null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    private static KnowledgeDocument CreateKnowledgeDocument(Guid projectId, string title, string status) => new()
    {
        ProjectId = projectId,
        Title = title,
        Content = $"محتوى {title}",
        Status = status
    };

    private sealed class UnexpectedObjectStorage : IObjectStorage
    {
        public Task<string> UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Storage must not be called for an outdated brand.");

        public Task<Stream> DownloadAsync(string objectKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Storage must not be called for an outdated brand.");

        public Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Storage must not be called for an outdated brand.");

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Storage must not be called for an outdated brand.");
    }

    private sealed class DownloadableContentStorage(
        string imageObjectKey,
        byte[] imageBytes,
        string logoObjectKey) : IObjectStorage
    {
        private readonly Dictionary<string, byte[]> _objects = new()
        {
            [imageObjectKey] = imageBytes
        };

        public int UploadCount { get; private set; }

        public async Task<string> UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            await using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            _objects[objectKey] = copy.ToArray();
            UploadCount++;
            return objectKey;
        }

        public Task<Stream> DownloadAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            if (objectKey == logoObjectKey)
                return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(objectKey)));
            if (_objects.TryGetValue(objectKey, out var bytes))
                return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
            throw new AmazonS3Exception("Object not found")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey"
            };
        }

        public Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Private content assets must not expose storage URLs.");

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Delete is not part of the asset download scenario.");
    }

    private sealed class RecordingImageHandler : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.PathAndQuery.TrimStart('/');
            ApiKey = request.Headers.GetValues("x-goog-api-key").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"AQID\"}}]}}]}")
            };
        }
    }

    private sealed class PassThroughSecretVault : IProjectSecretVault
    {
        public bool IsProtected(string? storedValue) => false;
        public string Protect(Guid projectId, string secret) => secret;
        public string? Unprotect(Guid projectId, string? storedValue) => storedValue;
    }

    private sealed class SequencedGeminiClient(params string[] responses) : IGeminiClient
    {
        private readonly Queue<string> _responses = new(responses);

        public Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!)
        {
            return Task.FromResult(_responses.Dequeue());
        }

        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) =>
            throw new NotSupportedException();

        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) =>
            throw new NotSupportedException();

        public Task<int> CountTokensAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!) =>
            throw new NotSupportedException();

        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string apiKeyOverride = null!) =>
            throw new NotSupportedException();
    }
}
