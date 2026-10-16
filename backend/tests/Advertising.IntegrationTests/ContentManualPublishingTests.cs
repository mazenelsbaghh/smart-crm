using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Content.Domain;
using Modules.Content.Jobs;
using Modules.Content.Services;
using Modules.Facebook.Domain;
using Modules.Projects.Domain;
using Shared.Security;
using Shared.Storage;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ContentManualPublishingTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(0, true, false)]
    [InlineData(3, true, false)]
    [InlineData(6, true, false)]
    [InlineData(10, true, false)]
    [InlineData(6, false, false)]
    [InlineData(6, false, true)]
    [InlineData(6, true, true)]
    public async Task Publishing_selected_post_preserves_remaining_slots_and_paused_schedule(
        int selectedDay, bool enabled, bool finishWeek)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var firstSlot = new DateTime(2030, 10, 1, 7, 0, 0, DateTimeKind.Utc);
        var settings = new ContentAutomationSettings
        {
            ProjectId = tenant.ProjectId, FacebookPageId = $"test-page-{tenant.ProjectId:N}", LogoObjectKey = "logo.png",
            IsEnabled = enabled, NextPublishAtUtc = enabled ? firstSlot : null
        };
        var plans = Enumerable.Range(0, 2).Select(week => new ContentWeekPlan
        {
            ProjectId = tenant.ProjectId, Status = ContentWeekPlanStatus.Approved,
            StartDateLocal = DateOnly.FromDateTime(firstSlot.AddDays(week * 7))
        }).ToArray();
        var posts = Enumerable.Range(0, 14).Select(day => new ContentPost
        {
            ProjectId = tenant.ProjectId, SettingsId = settings.Id,
            Status = finishWeek && day < 7 && day != selectedDay ? ContentPostStatus.Published : ContentPostStatus.Approved,
            Caption = $"Post {day}", ImageObjectKey = $"post-{day}.png",
            BrandLogoObjectKey = settings.LogoObjectKey,
            BrandStylePrompt = ContentBrandReferences.Identity(settings), ScheduledForUtc = firstSlot.AddDays(day)
        }).ToArray();
        var items = posts.Select((post, day) => new ContentWeekPlanItem
        {
            ProjectId = tenant.ProjectId, PlanId = plans[day / 7].Id, DayIndex = day % 7,
            ContentPostId = post.Id, ScheduledForUtc = post.ScheduledForUtc!.Value
        }).ToArray();
        db.AddRange(new Project { Id = tenant.ProjectId, Name = "Manual publishing" }, settings,
            new ConnectedPage { ProjectId = tenant.ProjectId, FacebookPageId = settings.FacebookPageId, PageAccessToken = "test-token" });
        db.AddRange(plans);
        db.AddRange(posts);
        db.AddRange(items);
        await db.SaveChangesAsync();
        using var handler = new FacebookResponse();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://facebook.invalid/") };
        var publisher = new ContentPublishingService(db, new ImageStorage(), new FacebookPhotoPublisher(http),
            NullLogger<ContentPublishingService>.Instance);
        var weeklyPlans = new ContentWeeklyPlanService(db, null!, null!, NullLogger<ContentWeeklyPlanService>.Instance);
        var job = new ContentAutomationJob(db, null!, publisher, weeklyPlans, NullLogger<ContentAutomationJob>.Instance);

        await job.PublishPostAsync(tenant.ProjectId, posts[selectedDay].Id);
        await job.PublishPostAsync(tenant.ProjectId, posts[selectedDay].Id);

        db.ChangeTracker.Clear();
        var savedSettings = await db.ContentAutomationSettings.SingleAsync();
        var savedPosts = await db.ContentPosts.OrderBy(post => post.ScheduledForUtc).ToArrayAsync();
        Assert.Equal(ContentPostStatus.Published, savedPosts[selectedDay].Status);
        Assert.Equal("facebook-post", savedPosts[selectedDay].FacebookPostId);
        Assert.Equal(1, handler.PublishCount);
        Assert.Equal(enabled, savedSettings.IsEnabled);
        var nextDay = finishWeek ? 7 : selectedDay == 0 ? 1 : 0;
        Assert.Equal(enabled ? firstSlot.AddDays(nextDay) : (DateTime?)null, savedSettings.NextPublishAtUtc);
        Assert.Equal(finishWeek ? ContentWeekPlanStatus.Completed : ContentWeekPlanStatus.Approved,
            (await db.ContentWeekPlans.SingleAsync(plan => plan.Id == plans[0].Id)).Status);
        Assert.Equal(items.Select(item => item.ScheduledForUtc).Order(),
            await db.ContentWeekPlanItems.OrderBy(item => item.ScheduledForUtc).Select(item => item.ScheduledForUtc).ToArrayAsync());
        Assert.All(savedPosts.Where((_, day) => day != selectedDay && (!finishWeek || day >= 7)),
            post => Assert.Equal(ContentPostStatus.Approved, post.Status));
    }

    private sealed class FacebookResponse : HttpMessageHandler
    {
        public int PublishCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            PublishCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"post_id\":\"facebook-post\"}")
            });
        }
    }

    private sealed class ImageStorage : IObjectStorage
    {
        public Task<Stream> DownloadAsync(string objectKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        public Task<string> UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
