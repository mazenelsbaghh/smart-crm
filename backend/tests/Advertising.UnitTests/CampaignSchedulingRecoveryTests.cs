using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Services;
using Modules.Campaigns.Application.Services;
using Modules.Campaigns.Domain;
using Modules.Campaigns.Jobs;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class CampaignSchedulingRecoveryTests
{
    [Fact]
    public async Task Start_retry_reschedules_committed_recipients_without_creating_duplicates()
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var campaign = Campaign(projectId, CampaignStatus.Running);
        db.Campaigns.Add(campaign);
        db.CampaignRecipients.AddRange(
            Recipient(projectId, campaign.Id, RecipientStatus.Pending),
            Recipient(projectId, campaign.Id, RecipientStatus.Accelerated));
        await db.SaveChangesAsync();
        var backgroundJobs = new CapturingBackgroundJobClient();

        await Job(db, backgroundJobs).StartCampaignAsync(campaign.Id);

        Assert.Equal(2, db.CampaignRecipients.Count());
        Assert.Equal(CampaignStatus.Running, campaign.Status);
        Assert.Collection(
            backgroundJobs.Jobs.OrderBy(job => job.Method.Name),
            job => Assert.Equal(nameof(CampaignSenderJob.SendAcceleratedMessageAsync), job.Method.Name),
            job => Assert.Equal(nameof(CampaignSenderJob.SendSingleMessageAsync), job.Method.Name));
    }

    [Fact]
    public async Task Resume_reschedules_every_pending_and_accelerated_recipient()
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var campaign = Campaign(projectId, CampaignStatus.Paused);
        db.Campaigns.Add(campaign);
        db.CampaignRecipients.AddRange(
            Recipient(projectId, campaign.Id, RecipientStatus.Pending),
            Recipient(projectId, campaign.Id, RecipientStatus.Pending),
            Recipient(projectId, campaign.Id, RecipientStatus.Accelerated));
        await db.SaveChangesAsync();
        var backgroundJobs = new CapturingBackgroundJobClient();

        await Job(db, backgroundJobs).ResumeFirstBatchAsync(campaign.Id);

        Assert.Equal(CampaignStatus.Running, campaign.Status);
        Assert.Equal(3, backgroundJobs.Jobs.Count);
        Assert.Equal(
            2,
            backgroundJobs.Jobs.Count(job => job.Method.Name == nameof(CampaignSenderJob.SendSingleMessageAsync)));
        Assert.Single(
            backgroundJobs.Jobs,
            job => job.Method.Name == nameof(CampaignSenderJob.SendAcceleratedMessageAsync));
    }

    private static CampaignSenderJob Job(AppDbContext db, IBackgroundJobClient backgroundJobs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsAppGateway:Url"] = "http://gateway.test",
                ["CampaignDispatch:BatchSize"] = "1"
            })
            .Build();
        var rejectingHandler = new RejectingHttpHandler();
        return new CampaignSenderJob(
            db,
            configuration,
            new RejectingCampaignAiService(),
            new WhatsAppGatewaySessionClient(
                new HttpClient(rejectingHandler, disposeHandler: false),
                configuration),
            new TestHttpClientFactory(rejectingHandler),
            backgroundJobs);
    }

    private static AppDbContext Context(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
    }

    private static Campaign Campaign(Guid projectId, CampaignStatus status) => new()
    {
        ProjectId = projectId,
        Name = "Recovery campaign",
        SegmentId = Guid.NewGuid(),
        MessageTemplateA = "Template",
        MessageTemplateB = "Template",
        Status = status
    };

    private static CampaignRecipient Recipient(
        Guid projectId,
        Guid campaignId,
        RecipientStatus status) => new()
    {
        ProjectId = projectId,
        CampaignId = campaignId,
        CustomerId = Guid.NewGuid(),
        Status = status
    };

    private sealed class CapturingBackgroundJobClient : IBackgroundJobClient
    {
        public List<Job> Jobs { get; } = [];

        public string Create(Job job, IState state)
        {
            Jobs.Add(job);
            return Guid.NewGuid().ToString("N");
        }

        public bool ChangeState(string jobId, IState state, string expectedState) =>
            throw new NotSupportedException("Campaign scheduling only creates jobs.");
    }

    private sealed class RejectingCampaignAiService : ICampaignAIService
    {
        public Task<string> GenerateCampaignCopyAsync(string prompt, string baseTemplate, string targetContext) =>
            throw new InvalidOperationException("Scheduling recovery must not call AI.");

        public Task<string> GenerateProjectCampaignCopyAsync(
            Guid projectId,
            string prompt,
            string baseTemplate,
            string targetContext) =>
            throw new InvalidOperationException("Scheduling recovery must not call AI.");
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RejectingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Scheduling recovery must not call the gateway.");
    }
}
