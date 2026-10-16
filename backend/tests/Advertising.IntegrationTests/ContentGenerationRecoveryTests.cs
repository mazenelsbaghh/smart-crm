using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Brain.Domain;
using Modules.Content.Domain;
using Modules.Content.Jobs;
using Modules.Content.Services;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Shared.Storage;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ContentGenerationRecoveryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private IsolatedPostgresDatabase database = null!;
    private readonly TenantContext tenant = new();
    private readonly ProjectSecretVault vault = new(new EphemeralDataProtectionProvider());
    private ContentAutomationSettings settings = null!;

    public async Task InitializeAsync()
    {
        database = await postgres.CreateIsolatedDatabaseAsync();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = database.CreateContext(tenant);
        await db.Database.MigrateAsync();
        settings = new ContentAutomationSettings { ProjectId = tenant.ProjectId, LogoObjectKey = "logo.png",
            HasApprovedStyle = true, LastPublishedAtUtc = DateTime.UtcNow.AddDays(-1), FacebookPageId = "test-page" };
        db.AddRange(new Project { Id = tenant.ProjectId, Name = "Content recovery" }, settings,
            new ProjectSettings { ProjectId = tenant.ProjectId, GeminiApiKey = vault.Protect(tenant.ProjectId, "test-key") },
            new KnowledgeDocument { ProjectId = tenant.ProjectId, Status = "Approved", Title = "English club", Content = "ورش ممارسة اللغة الإنجليزية" });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => database.DisposeAsync().AsTask();

    [Fact]
    public async Task Recovery_dispatches_stale_samples_empty_weeks_and_stopping_games()
    {
        await using var db = database.CreateContext(tenant);
        var sample = Sample();
        var plan = Plan(ContentWeekPlanStatus.AwaitingApproval);
        var game = Game(ContentCardGameDesignStatus.Stopping);
        db.AddRange(sample, plan, game);
        await db.SaveChangesAsync();
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        await db.ContentPosts.ExecuteUpdateAsync(update => update.SetProperty(post => post.UpdatedAt, cutoff));
        await db.ContentWeekPlans.ExecuteUpdateAsync(update => update.SetProperty(week => week.UpdatedAt, cutoff));
        await db.ContentCardGames.ExecuteUpdateAsync(update => update.SetProperty(deck => deck.UpdatedAt, cutoff));
        var jobs = new RecordingJobs();

        await new ContentGenerationRecoveryJob(db, jobs).RecoverAsync();

        Assert.Equal(3, jobs.Created.Count);
        Assert.Contains(jobs.Created, job => job.Method.Name == nameof(ContentAutomationJob.GenerateSampleAsync)
            && Equals(job.Args[0], tenant.ProjectId));
        Assert.Contains(jobs.Created, job => job.Method.Name == nameof(ContentAutomationJob.GenerateWeeklyPlanAsync)
            && Equals(job.Args[0], tenant.ProjectId));
        Assert.Contains(jobs.Created, job => job.Type == typeof(ContentCardGameDesignJob)
            && Equals(job.Args[0], tenant.ProjectId) && Equals(job.Args[1], game.Id));
    }

    [Fact]
    public async Task Sample_recovery_waits_for_owner_disconnect_and_reuses_saved_image()
    {
        await using var db = database.CreateContext(tenant);
        var sample = Sample();
        sample.ImageObjectKey = "saved-sample.png";
        db.ContentPosts.Add(sample);
        await db.SaveChangesAsync();
        await using var owner = database.CreateContext(tenant);
        await using var ownerLock = await ContentGenerationLock.TryAcquireAsync(owner,
            ContentGenerationLock.Sample(tenant.ProjectId), CancellationToken.None);
        Assert.NotNull(ownerLock);
        var job = Job(db, new ContentGenerationService(db, null!, null!, null!,
            NullLogger<ContentGenerationService>.Instance, vault));

        await job.GenerateSampleAsync(tenant.ProjectId);
        Assert.Equal(ContentPostStatus.Generating, (await db.ContentPosts.SingleAsync()).Status);
        await owner.Database.CloseConnectionAsync();
        await job.GenerateSampleAsync(tenant.ProjectId);

        db.ChangeTracker.Clear();
        var recovered = await db.ContentPosts.SingleAsync();
        Assert.Equal(sample.Id, recovered.Id);
        Assert.Equal(ContentPostStatus.AwaitingApproval, recovered.Status);
        Assert.Equal("saved-sample.png", recovered.ImageObjectKey);
        await using var nextOwner = await ContentGenerationLock.TryAcquireAsync(owner,
            ContentGenerationLock.Sample(tenant.ProjectId), CancellationToken.None);
        Assert.NotNull(nextOwner);
    }

    [Fact]
    public async Task Live_sample_worker_keeps_ownership_during_provider_request_and_resumes_existing_copy()
    {
        await using var db = database.CreateContext(tenant);
        var sample = Sample();
        db.ContentPosts.Add(sample);
        await db.SaveChangesAsync();
        using var provider = new ImageResponse { Pause = true };
        using var http = new HttpClient(provider) { BaseAddress = new Uri("https://images.invalid/") };
        var storage = new ImageStorage();
        var generation = Generation(db, http, storage);
        var running = Job(db, generation).GenerateSampleAsync(tenant.ProjectId);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var contender = database.CreateContext(tenant);
            await Job(contender, Generation(contender, http, storage)).GenerateSampleAsync(tenant.ProjectId);
            Assert.Equal(ContentPostStatus.Generating, (await contender.ContentPosts.SingleAsync()).Status);
            Assert.Empty(storage.Uploads);
        }
        finally { provider.Release.TrySetResult(); }
        await running;

        db.ChangeTracker.Clear();
        var recovered = await db.ContentPosts.SingleAsync();
        Assert.Equal(sample.Id, recovered.Id);
        Assert.Equal(sample.Caption, recovered.Caption);
        Assert.Equal(ContentPostStatus.AwaitingApproval, recovered.Status);
        Assert.Single(storage.Uploads);
    }

    [Theory]
    [InlineData(ContentWeekPlanStatus.Generating)]
    [InlineData(ContentWeekPlanStatus.AwaitingApproval)]
    public async Task Empty_week_recovery_regenerates_items_in_same_plan_and_can_be_approved(ContentWeekPlanStatus status)
    {
        await using var db = database.CreateContext(tenant);
        var plan = Plan(status);
        db.ContentWeekPlans.Add(plan);
        await db.SaveChangesAsync();
        var planner = new PlannerResponses(WeeklyResponse());
        var weekly = new ContentWeeklyPlanService(db, planner, vault, NullLogger<ContentWeeklyPlanService>.Instance);
        using var provider = new ImageResponse();
        using var http = new HttpClient(provider) { BaseAddress = new Uri("https://images.invalid/") };
        var storage = new ImageStorage();
        var job = new ContentAutomationJob(db, Generation(db, http, storage), null!, weekly,
            NullLogger<ContentAutomationJob>.Instance);

        await job.GenerateWeeklyPlanAsync(tenant.ProjectId);
        await job.GenerateWeeklyPlanAsync(tenant.ProjectId);

        db.ChangeTracker.Clear();
        var saved = await db.ContentWeekPlans.SingleAsync();
        Assert.Equal(plan.Id, saved.Id);
        Assert.Equal(ContentWeekPlanStatus.AwaitingApproval, saved.Status);
        var items = await db.ContentWeekPlanItems.OrderBy(item => item.DayIndex).ToListAsync();
        Assert.Equal(7, items.Count);
        Assert.All(items, item => Assert.NotNull(item.ContentPostId));
        Assert.Equal(7, storage.Uploads.Count);
        foreach (var post in await db.ContentPosts.ToListAsync()) post.Status = ContentPostStatus.Approved;
        await db.SaveChangesAsync();
        await weekly.ApproveAsync(tenant.ProjectId, saved.Id, CancellationToken.None);
        Assert.Equal(ContentWeekPlanStatus.Approved, (await db.ContentWeekPlans.SingleAsync()).Status);
    }

    [Fact]
    public async Task Empty_week_cannot_be_marked_ready_for_review()
    {
        await using var db = database.CreateContext(tenant);
        var plan = Plan(ContentWeekPlanStatus.Generating);
        db.ContentWeekPlans.Add(plan);
        await db.SaveChangesAsync();
        var weekly = new ContentWeeklyPlanService(db, null!, vault, NullLogger<ContentWeeklyPlanService>.Instance);

        await weekly.MarkReadyForReviewAsync(tenant.ProjectId, plan.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        var saved = await db.ContentWeekPlans.SingleAsync();
        Assert.Equal(ContentWeekPlanStatus.GenerationFailed, saved.Status);
        Assert.NotEmpty(saved.Error!);
    }

    [Fact]
    public async Task Week_recovery_links_an_already_generated_orphan_preview_without_provider_request()
    {
        await using var db = database.CreateContext(tenant);
        var post = Sample();
        post.IsStyleSample = false;
        post.Status = ContentPostStatus.AwaitingApproval;
        post.ScheduledForUtc = DateTime.UtcNow.AddDays(1);
        post.ImageObjectKey = "generated-before-crash.png";
        db.ContentPosts.Add(post);
        await db.SaveChangesAsync();
        var generation = new ContentGenerationService(db, null!, null!, null!, NullLogger<ContentGenerationService>.Instance, vault);

        var recovered = await generation.GenerateScheduledPreviewAsync(tenant.ProjectId, post.ScheduledForUtc.Value,
            new GeneratedCopy(post.Topic, post.VisualHeadline, post.Caption, "concept"), CancellationToken.None);

        Assert.Equal(post.Id, recovered.Id);
        Assert.Equal("generated-before-crash.png", recovered.ImageObjectKey);
        Assert.Single(await db.ContentPosts.ToListAsync());
    }

    [Fact]
    public async Task Interrupted_card_design_resumes_only_missing_artwork()
    {
        await using var db = database.CreateContext(tenant);
        var game = Game(ContentCardGameDesignStatus.Generating);
        game.BackImageObjectKey = "saved/full-card-v1/back.png";
        var cards = Enumerable.Range(0, 8).Select(index => new ContentGameCard { ProjectId = tenant.ProjectId,
            GameId = game.Id, CardIndex = index, Title = $"Card {index}", Prompt = "Draw one card.",
            ImageObjectKey = index == 7 ? null : $"saved/full-card-v1/card-{index}.png" }).ToArray();
        db.Add(game);
        db.AddRange(cards);
        await db.SaveChangesAsync();
        using var provider = new ImageResponse();
        using var http = new HttpClient(provider) { BaseAddress = new Uri("https://images.invalid/") };
        var storage = new ImageStorage();
        var design = new ContentCardGameDesignService(db, new GeminiImageClient(http), vault, storage,
            NullLogger<ContentCardGameDesignService>.Instance);

        await design.GenerateAsync(tenant.ProjectId, game.Id, CancellationToken.None);
        await design.GenerateAsync(tenant.ProjectId, game.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(ContentCardGameDesignStatus.Ready, (await db.ContentCardGames.SingleAsync()).DesignStatus);
        var savedCards = await db.ContentGameCards.OrderBy(card => card.CardIndex).ToArrayAsync();
        Assert.All(savedCards, card => Assert.NotNull(card.ImageObjectKey));
        Assert.Equal(cards[0].ImageObjectKey, savedCards[0].ImageObjectKey);
        Assert.Single(storage.Uploads);
    }

    [Fact]
    public async Task Interrupted_stopping_card_design_becomes_cancelled_without_generating_images()
    {
        await using var db = database.CreateContext(tenant);
        var game = Game(ContentCardGameDesignStatus.Stopping);
        db.ContentCardGames.Add(game);
        await db.SaveChangesAsync();
        var design = new ContentCardGameDesignService(db, null!, vault, null!, NullLogger<ContentCardGameDesignService>.Instance);

        await design.GenerateAsync(tenant.ProjectId, game.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(ContentCardGameDesignStatus.Cancelled, (await db.ContentCardGames.SingleAsync()).DesignStatus);
    }

    [Theory]
    [InlineData(3000)]
    [InlineData(3001)]
    public async Task Card_rules_are_preserved_whole_or_regenerated_before_persistence(int length)
    {
        await using var db = database.CreateContext(tenant);
        var prefix = "عدد اللاعبين: ٣. التجهيز: اخلط الكروت. توزيع الكروت: ٣ لكل لاعب. الدور: ";
        var ending = " تأثير الكروت: اسحب أو بدل. نهاية اللعبة: ينتهي السحب. الفوز: أكبر نقاط.";
        var firstRules = prefix + new string('أ', length - prefix.Length - ending.Length) + ending;
        var completeRules = prefix + "العب كارت واحد." + ending;
        var service = new ContentCardGameService(db, new PlannerResponses(DeckResponse(firstRules), DeckResponse(completeRules)), vault);

        var game = await service.CreateAsync(tenant.ProjectId, new CreateCardGameInput(null, "ورشة ممارسة اللغة الإنجليزية", null, 8), CancellationToken.None);

        db.ChangeTracker.Clear();
        var saved = await db.ContentCardGames.SingleAsync();
        Assert.Equal(game.Id, saved.Id);
        Assert.Equal(length == 3000 ? firstRules : completeRules, saved.Instructions);
        Assert.EndsWith(ending, saved.Instructions);
    }

    private ContentPost Sample() => new() { ProjectId = tenant.ProjectId, SettingsId = settings.Id, IsStyleSample = true,
        Status = ContentPostStatus.Generating, BrandLogoObjectKey = settings.LogoObjectKey!,
        BrandStylePrompt = ContentBrandReferences.Identity(settings), Topic = "Practice", VisualHeadline = "Small steps",
        Caption = "Saved original caption", ImagePrompt = "An English practice scene" };

    private ContentWeekPlan Plan(ContentWeekPlanStatus status) => new() { ProjectId = tenant.ProjectId, Status = status,
        StartDateLocal = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), DailyPublishTimeLocal = settings.DailyPublishTimeLocal,
        Timezone = settings.Timezone, BrandLogoObjectKey = settings.LogoObjectKey!, BrandStylePrompt = ContentBrandReferences.Identity(settings) };

    private ContentCardGame Game(string status) => new() { ProjectId = tenant.ProjectId, DesignStatus = status,
        CardCount = 8, BrandLogoObjectKey = settings.LogoObjectKey!, Title = "Card Quest" };

    private ContentGenerationService Generation(AppDbContext db, HttpClient http, ImageStorage storage) =>
        new(db, null!, new ContentImageClient(new GeminiImageClient(http), new OpenAiImageClient(http), vault), storage,
            NullLogger<ContentGenerationService>.Instance, vault);

    private static ContentAutomationJob Job(AppDbContext db, ContentGenerationService generation) =>
        new(db, generation, null!, null!, NullLogger<ContentAutomationJob>.Instance);

    private static string DeckResponse(string instructions) => JsonSerializer.Serialize(new { title = "Card Quest", mechanic = "لعبة كروت",
        instructions, cards = Enumerable.Range(0, 8).Select(index => new { category = new[] { "Action", "Swap", "Score" }[index % 3],
            title = $"Card {index}", prompt = "Draw one card and score one point.", instruction = "Play one card." }) });

    private static string WeeklyResponse()
    {
        string[] captions =
        [
            "قبل أول مقابلة بدقائق، ممكن تنسى كل الجمل اللي حضرتها وتفتكر بس صوت القلق. التدريب على موقف واحد بصوت مسموع يخلي ردك أهدى في المرة التالية، لأن الثقة بتتبني من محاولات صغيرة متكررة. اكتب أصعب سؤال قابلته وخليه نقطة البداية.",
            "الشهادة تثبت إنك درست، لكنها مش دايمًا تثبت إنك تعرف تتصرف وقت مكالمة حقيقية. جرّب تقارن بين إجابة محفوظة ورد طبيعي على عميل متردد، وهتلاحظ إن المرونة هي الفارق الأوضح. شارك المنشور مع شخص بيستعد لأول فرصة مهنية.",
            "خمس دقائق كفاية لاختبار عادة جديدة اليوم. اختر موقف خدمة بسيط، سجّل ردك بالإنجليزية، واسمعه مرة من غير ما تحكم على نفسك. ركّز فقط على وضوح الفكرة، وبكرة كرر المحاولة بنبرة أهدى. احفظ التمرين وارجع له نهاية الأسبوع.",
            "طريق طويل مليان مستويات، أو تدريب مركز مرتبط بالموقف اللي هتقابله في الشغل؟ الاختيار مش متعلق بالسرعة وحدها، لكنه متعلق بوضوح النتيجة اللي بتدور عليها. حدد المهارة الأهم بالنسبة لك أولًا، وبعدها قارن البرامج على أساسها. أي معيار بيحسم قرارك؟",
            "موقف واحد ممكن يكشف أكتر من قائمة مهارات كاملة: عميل غاضب، وقت محدود، ومعلومة ناقصة. هنا يظهر الفرق بين حفظ الكلمات وفهم طريقة التواصل. تخيل إن المكالمة بدأت الآن؛ ما أول جملة هتقولها عشان تهدي الحوار وتجمع التفاصيل الصحيحة؟",
            "التقدم ساعات بيكون هادي لدرجة إنك ما تلاحظوش يوم بيوم. كلمة نطقتها أوضح، سؤال فهمته أسرع، أو لحظة تردد اختفت. اعمل علامة أسبوعية بسيطة وسجّل تغييرًا واحدًا فقط، لأن متابعة التفاصيل الصغيرة بتوضح المسافة اللي قطعتها. احتفظ بالفكرة لمراجعة الجمعة.",
            "البداية القوية مش معناها إنك تعرف كل حاجة، لكنها تعني إنك عارف الخطوة التالية. اختار مهارة واحدة تحتاجها في بيئة العمل وخلي تدريبك الأسبوع ده مركز عليها فقط، بدل التنقل بين أهداف كثيرة. ابعت التحدي لزميلك واتفقوا تراجعوا النتيجة سوا."
        ];
        return JsonSerializer.Serialize(new { items = captions.Select((caption, index) => new { topic = $"فكرة {index}",
            visualHeadline = $"خطوة {index}", caption, imagePrompt = $"Practice scene {index}" }) });
    }

    private sealed class PlannerResponses(params string[] responses) : IGeminiClient
    {
        private int index;
        public Task<string> GenerateReplyAsync(string content, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) =>
            Task.FromResult(responses[Math.Min(index++, responses.Length - 1)]);
        public Task<string> GenerateReplyAsync(string content, byte[] fileBytes, string mimeType, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string content, string apiKeyOverride = null!, string modelOverride = null!) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string content, string model, int ttlSeconds, string apiKeyOverride = null!) => throw new NotSupportedException();
    }

    private sealed class RecordingJobs : Hangfire.IBackgroundJobClient
    {
        public List<Hangfire.Common.Job> Created { get; } = [];
        public string Create(Hangfire.Common.Job job, Hangfire.States.IState state)
        { Created.Add(job); return Guid.NewGuid().ToString(); }
        public bool ChangeState(string jobId, Hangfire.States.IState state, string? expectedState) => throw new NotSupportedException();
    }

    private sealed class ImageResponse : HttpMessageHandler
    {
        public bool Pause { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (Pause) await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"candidates":[{"content":{"parts":[{"inlineData":{"mimeType":"image/png","data":"AQID"}}]}}]}""") };
        }
    }

    private sealed class ImageStorage : IObjectStorage
    {
        public List<string> Uploads { get; } = [];
        public Task<string> UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken = default)
        { Uploads.Add(objectKey); return Task.FromResult(objectKey); }
        public Task<Stream> DownloadAsync(string objectKey, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        public Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
