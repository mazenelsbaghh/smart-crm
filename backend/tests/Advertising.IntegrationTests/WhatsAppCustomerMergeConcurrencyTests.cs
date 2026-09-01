using Microsoft.EntityFrameworkCore;
using Modules.Campaigns.Domain;
using Modules.Conversations.Domain;
using Modules.Projects.Domain;
using Modules.WhatsApp.Services;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class WhatsAppCustomerMergeConcurrencyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Concurrent_LID_recovery_to_one_phone_keeps_one_customer_and_safest_campaign_recipient()
    {
        var projectId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var canonicalCustomerId = Guid.NewGuid();
        var firstLidCustomerId = Guid.NewGuid();
        var secondLidCustomerId = Guid.NewGuid();
        const string phone = "201012345678";

        await using (var seed = postgres.CreateContext())
        {
            await seed.Database.MigrateAsync();
            seed.Projects.Add(new Project { Id = projectId, Name = "WhatsApp merge concurrency" });
            seed.Campaigns.Add(new Campaign
            {
                Id = campaignId,
                ProjectId = projectId,
                Name = "Merge-safe campaign",
                SegmentId = Guid.NewGuid(),
                MessageTemplateA = "A",
                MessageTemplateB = "B"
            });
            seed.Customers.AddRange(
                Customer(canonicalCustomerId, projectId, phone, DateTime.UtcNow.AddDays(-3)),
                Customer(firstLidCustomerId, projectId, "111111111111111@lid", DateTime.UtcNow.AddDays(-2)),
                Customer(secondLidCustomerId, projectId, "222222222222222@lid", DateTime.UtcNow.AddDays(-1)));
            seed.CampaignRecipients.AddRange(
                Recipient(projectId, campaignId, canonicalCustomerId, RecipientStatus.Pending),
                Recipient(projectId, campaignId, firstLidCustomerId, RecipientStatus.Sent),
                Recipient(projectId, campaignId, secondLidCustomerId, RecipientStatus.Read));
            await seed.SaveChangesAsync();
        }

        await using var first = postgres.CreateContext();
        await using var second = postgres.CreateContext();
        var firstMerge = new WhatsAppCustomerMergeService(first)
            .BindPhoneAsync(projectId, firstLidCustomerId, phone);
        var secondMerge = new WhatsAppCustomerMergeService(second)
            .BindPhoneAsync(projectId, secondLidCustomerId, phone);

        var mergeResults = await Task.WhenAll(firstMerge, secondMerge);

        Assert.All(mergeResults, result => Assert.Equal(canonicalCustomerId, result.Id));
        await using var verification = postgres.CreateContext();
        var remainingCustomers = await verification.Customers.IgnoreQueryFilters()
            .Where(customer => customer.ProjectId == projectId)
            .ToListAsync();
        Assert.Equal(canonicalCustomerId, Assert.Single(remainingCustomers).Id);
        var identity = Assert.Single(await verification.WhatsAppPhoneCustomerIdentities
            .IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId)
            .ToListAsync());
        Assert.Equal(canonicalCustomerId, identity.CustomerId);
        Assert.Equal(phone, identity.NormalizedPhone);
        var recipient = Assert.Single(await verification.CampaignRecipients.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CampaignId == campaignId)
            .ToListAsync());
        Assert.Equal(canonicalCustomerId, recipient.CustomerId);
        Assert.Equal(RecipientStatus.Read, recipient.Status);
    }

    private static Customer Customer(Guid id, Guid projectId, string phone, DateTime createdAt) => new()
    {
        Id = id,
        ProjectId = projectId,
        PhoneNumber = phone,
        Name = "عميل",
        City = string.Empty,
        CreatedAt = createdAt
    };

    private static CampaignRecipient Recipient(
        Guid projectId,
        Guid campaignId,
        Guid customerId,
        RecipientStatus status) => new()
    {
        ProjectId = projectId,
        CampaignId = campaignId,
        CustomerId = customerId,
        Status = status
    };
}
