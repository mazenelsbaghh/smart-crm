using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Advertising.Services;
using Modules.Campaigns.Application.Services;
using Modules.Campaigns.Domain;
using Modules.Campaigns.Jobs;
using Modules.Conversations.Domain;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Shared.Infrastructure;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CampaignSenderConcurrencyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Concurrent_jobs_send_each_recipient_once_and_complete_without_losing_counts()
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var firstRecipientId = Guid.NewGuid();
        var secondRecipientId = Guid.NewGuid();
        await SeedCampaignAsync(
            projectId,
            accountId,
            campaignId,
            (firstRecipientId, Guid.NewGuid()),
            (secondRecipientId, Guid.NewGuid()));
        var gateway = new CampaignGatewayHandler(waitForPostCount: 2);
        var ai = new FixedCampaignAiService();

        await using var firstAttempt = postgres.CreateContext();
        await using var firstDuplicate = postgres.CreateContext();
        await using var secondAttempt = postgres.CreateContext();
        await using var secondDuplicate = postgres.CreateContext();
        var sends = new[]
        {
            Job(firstAttempt, gateway, ai).SendSingleMessageAsync(firstRecipientId),
            Job(firstDuplicate, gateway, ai).SendSingleMessageAsync(firstRecipientId),
            Job(secondAttempt, gateway, ai).SendSingleMessageAsync(secondRecipientId),
            Job(secondDuplicate, gateway, ai).SendSingleMessageAsync(secondRecipientId)
        };

        await Task.WhenAll(sends);

        Assert.Equal(2, gateway.PostCount);
        Assert.Equal(2, gateway.SendPayloads.Count);
        Assert.All(gateway.StatusRequestUris, uri =>
            Assert.Contains($"whatsappAccountId={accountId}", uri.Query, StringComparison.Ordinal));
        var idempotencyKeys = new HashSet<string>();
        foreach (var payloadJson in gateway.SendPayloads)
        {
            using var payload = JsonDocument.Parse(payloadJson);
            Assert.Equal(accountId, payload.RootElement.GetProperty("whatsappAccountId").GetGuid());
            idempotencyKeys.Add(payload.RootElement.GetProperty("idempotencyKey").GetString()!);
        }
        Assert.Equal(2, idempotencyKeys.Count);

        await using var verification = postgres.CreateContext();
        var campaign = await verification.Campaigns.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == campaignId);
        Assert.Equal(2, campaign.SentCount);
        Assert.Equal(2, campaign.DeliveredCount);
        Assert.Equal(CampaignStatus.Completed, campaign.Status);
        Assert.NotNull(campaign.CompletedAt);
        Assert.All(
            await verification.CampaignRecipients.IgnoreQueryFilters()
                .Where(recipient => recipient.CampaignId == campaignId)
                .ToListAsync(),
            recipient => Assert.Equal(RecipientStatus.Sent, recipient.Status));
    }

    [Fact]
    public async Task Successful_http_response_without_provider_id_is_delivery_unknown()
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        await SeedCampaignAsync(projectId, accountId, campaignId, (recipientId, Guid.NewGuid()));
        var gateway = new CampaignGatewayHandler(includeProviderMessageId: false);

        await using var context = postgres.CreateContext();
        await Job(context, gateway, new FixedCampaignAiService()).SendSingleMessageAsync(recipientId);

        await using var verification = postgres.CreateContext();
        var recipient = await verification.CampaignRecipients.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == recipientId);
        var campaign = await verification.Campaigns.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == campaignId);
        Assert.Equal(RecipientStatus.DeliveryUnknown, recipient.Status);
        Assert.Equal(CampaignStatus.Completed, campaign.Status);
        Assert.Equal(0, campaign.SentCount);
        Assert.Equal(0, campaign.DeliveredCount);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(500)]
    [InlineData(0)]
    public async Task Ambiguous_gateway_outcomes_are_terminal_delivery_unknown(int gatewayStatusCode)
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        await SeedCampaignAsync(projectId, accountId, campaignId, (recipientId, Guid.NewGuid()));
        var gateway = new CampaignGatewayHandler(responseStatusCode: gatewayStatusCode);

        await using var context = postgres.CreateContext();
        await Job(context, gateway, new FixedCampaignAiService()).SendSingleMessageAsync(recipientId);

        await using var verification = postgres.CreateContext();
        var recipient = await verification.CampaignRecipients.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == recipientId);
        var campaign = await verification.Campaigns.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == campaignId);
        Assert.Equal(RecipientStatus.DeliveryUnknown, recipient.Status);
        Assert.NotEmpty(recipient.ErrorMessage);
        Assert.Equal(CampaignStatus.Completed, campaign.Status);
        Assert.Equal(0, campaign.SentCount);
        Assert.Equal(0, campaign.DeliveredCount);
    }

    [Theory]
    [InlineData(412)]
    [InlineData(503)]
    public async Task Definitely_unsent_gateway_responses_pause_and_release_the_recipient(int gatewayStatusCode)
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        await SeedCampaignAsync(projectId, accountId, campaignId, (recipientId, Guid.NewGuid()));
        var gateway = new CampaignGatewayHandler(responseStatusCode: gatewayStatusCode);

        await using var context = postgres.CreateContext();
        await Job(context, gateway, new FixedCampaignAiService()).SendSingleMessageAsync(recipientId);

        await using var verification = postgres.CreateContext();
        var recipient = await verification.CampaignRecipients.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == recipientId);
        var campaign = await verification.Campaigns.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == campaignId);
        Assert.Equal(RecipientStatus.Pending, recipient.Status);
        Assert.Contains(gatewayStatusCode.ToString(), recipient.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(CampaignStatus.Paused, campaign.Status);
        Assert.Equal(0, campaign.SentCount);
    }

    [Fact]
    public async Task Connected_status_without_an_epoch_pauses_before_AI_or_delivery()
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        await SeedCampaignAsync(projectId, accountId, campaignId, (recipientId, Guid.NewGuid()));
        var gateway = new CampaignGatewayHandler(includeConnectedAt: false);
        var ai = new FixedCampaignAiService();

        await using var context = postgres.CreateContext();
        await Job(context, gateway, ai).SendSingleMessageAsync(recipientId);

        await using var verification = postgres.CreateContext();
        Assert.Equal(
            RecipientStatus.Pending,
            (await verification.CampaignRecipients.IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.Id == recipientId)).Status);
        Assert.Equal(
            CampaignStatus.Paused,
            (await verification.Campaigns.IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.Id == campaignId)).Status);
        Assert.Equal(0, ai.CallCount);
        Assert.Equal(0, gateway.PostCount);
    }

    private async Task SeedCampaignAsync(
        Guid projectId,
        Guid accountId,
        Guid campaignId,
        params (Guid RecipientId, Guid CustomerId)[] recipients)
    {
        await using var seed = postgres.CreateContext();
        await seed.Database.MigrateAsync();
        seed.Add(new Project { Id = projectId, Name = "Campaign concurrency project" });
        seed.Add(new WhatsAppAccount
        {
            Id = accountId,
            ProjectId = projectId,
            Name = "Selected campaign account",
            IsDefault = false
        });
        seed.Add(new Campaign
        {
            Id = campaignId,
            ProjectId = projectId,
            WhatsAppAccountId = accountId,
            Name = "Concurrent campaign",
            SegmentId = Guid.NewGuid(),
            MessageTemplateA = "أهلاً {{CustomerName}} عن {{InterestTopic}}",
            MessageTemplateB = "أهلاً {{CustomerName}}",
            Status = CampaignStatus.Running
        });
        for (var index = 0; index < recipients.Length; index++)
        {
            var (recipientId, customerId) = recipients[index];
            seed.Add(new Customer
            {
                Id = customerId,
                ProjectId = projectId,
                PhoneNumber = $"2010000000{index}",
                Name = "عميل",
                City = string.Empty,
                Notes = string.Empty
            });
            seed.Add(new CampaignRecipient
            {
                Id = recipientId,
                ProjectId = projectId,
                CampaignId = campaignId,
                CustomerId = customerId,
                Status = RecipientStatus.Pending
            });
        }

        await seed.SaveChangesAsync();
    }

    private static CampaignSenderJob Job(
        AppDbContext context,
        CampaignGatewayHandler gateway,
        ICampaignAIService ai)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsAppGateway:Url"] = "http://gateway.test"
            })
            .Build();
        return new CampaignSenderJob(
            context,
            configuration,
            ai,
            new WhatsAppGatewaySessionClient(
                new HttpClient(gateway, disposeHandler: false),
                configuration),
            new TestHttpClientFactory(gateway),
            new RejectingBackgroundJobClient());
    }

    private sealed class FixedCampaignAiService : ICampaignAIService
    {
        private int _callCount;
        public int CallCount => _callCount;

        public Task<string> GenerateCampaignCopyAsync(string prompt, string baseTemplate, string targetContext) =>
            GenerateAsync();

        public Task<string> GenerateProjectCampaignCopyAsync(
            Guid projectId,
            string prompt,
            string baseTemplate,
            string targetContext) => GenerateAsync();

        private Task<string> GenerateAsync()
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult("تفاصيل الكورس");
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RejectingBackgroundJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) =>
            throw new InvalidOperationException("Message delivery must not schedule another background job.");

        public bool ChangeState(string jobId, IState state, string expectedState) =>
            throw new InvalidOperationException("Message delivery must not mutate a background job.");
    }

    private sealed class CampaignGatewayHandler : HttpMessageHandler
    {
        private readonly int _responseStatusCode;
        private readonly int _waitForPostCount;
        private readonly bool _includeConnectedAt;
        private readonly bool _includeProviderMessageId;
        private readonly DateTimeOffset _connectedAt = DateTimeOffset.UtcNow;
        private readonly TaskCompletionSource<bool> _postBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _postCount;

        public CampaignGatewayHandler(
            int responseStatusCode = 200,
            int waitForPostCount = 1,
            bool includeConnectedAt = true,
            bool includeProviderMessageId = true)
        {
            _responseStatusCode = responseStatusCode;
            _waitForPostCount = waitForPostCount;
            _includeConnectedAt = includeConnectedAt;
            _includeProviderMessageId = includeProviderMessageId;
        }

        public int PostCount => _postCount;
        public ConcurrentBag<Uri> StatusRequestUris { get; } = [];
        public ConcurrentBag<string> SendPayloads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                StatusRequestUris.Add(request.RequestUri!);
                var connectedAt = _includeConnectedAt
                    ? $",\"connectedAt\":\"{_connectedAt:O}\""
                    : string.Empty;
                return JsonResponse(
                    HttpStatusCode.OK,
                    $"{{\"status\":\"Connected\",\"phoneNumber\":\"201000000000\"{connectedAt}}}");
            }

            if (_responseStatusCode == 0)
            {
                throw new HttpRequestException("Connection dropped after request dispatch.");
            }

            var payloadJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            SendPayloads.Add(payloadJson);
            if (Interlocked.Increment(ref _postCount) >= _waitForPostCount)
            {
                _postBarrier.TrySetResult(true);
            }
            await _postBarrier.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!_includeProviderMessageId)
            {
                return JsonResponse((HttpStatusCode)_responseStatusCode, "{\"status\":\"accepted\"}");
            }

            using var payload = JsonDocument.Parse(payloadJson);
            var idempotencyKey = payload.RootElement.GetProperty("idempotencyKey").GetString();
            return JsonResponse(
                (HttpStatusCode)_responseStatusCode,
                JsonSerializer.Serialize(new
                {
                    status = "accepted",
                    messageId = $"provider-{idempotencyKey}"
                }));
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
