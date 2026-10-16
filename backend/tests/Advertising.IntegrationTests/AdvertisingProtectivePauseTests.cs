using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Modules.Advertising.Workers;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AdvertisingProtectivePauseTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("dynamic", AuthorizedDestinationState.Eligible, AdvertisingConnectionState.Ready)]
    [InlineData("facebook", AuthorizedDestinationState.Ineligible, AdvertisingConnectionState.Degraded)]
    public async Task Emergency_stop_pauses_owned_delivery_despite_imported_placement_or_lost_activation_eligibility(
        string platform, AuthorizedDestinationState destinationState, AdvertisingConnectionState connectionState)
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = isolated.CreateContext();
        await db.Database.MigrateAsync();
        var projectId = Guid.NewGuid();
        var vault = new AdvertisingSecretVault(new EphemeralDataProtectionProvider());
        var connection = new AdvertisingConnection { ProjectId = projectId, State = connectionState, ProtectedAccessToken = vault.Protect("test-token") };
        var destination = new AuthorizedWhatsAppDestination { ProjectId = projectId, ConnectionId = connection.Id, State = destinationState };
        var owned = new ManagedOwnershipRecord { ProjectId = projectId, ConnectionId = connection.Id,
            OwnershipKind = ManagedOwnershipKind.ImportedWithAuthority, ProviderCampaignExternalId = "campaign-test" };
        var ad = new ManagedAdvertisement { ProjectId = projectId, ConnectionId = connection.Id, OwnershipRecordId = owned.Id,
            DestinationId = destination.Id, PublisherPlatform = platform, PositionsJson = "[\"feed\"]",
            CampaignExternalId = "campaign-test", AdSetExternalId = "adset-test", AdExternalId = "ad-test",
            ConfiguredStatus = ManagedDeliveryState.Active, EffectiveStatus = "ACTIVE", ProviderStateHash = "original" };
        db.AddRange(connection, destination, owned, ad);
        await db.SaveChangesAsync();
        var ownership = new AdvertisingOwnershipPolicy(db);
        var emergency = new AdvertisingEmergencyStopService(db, ownership);
        var stop = await emergency.ActivateAsync(projectId, EmergencyTrigger.Manual, "Protective regression");
        ad.ProviderStateHash = "changed-after-stop";
        await db.SaveChangesAsync();
        using var provider = new DeliveryProvider();
        using var http = new HttpClient(provider) { BaseAddress = new Uri("https://meta.invalid/") };
        var meta = new MetaAdsClient(http, Options.Create(new AdvertisingOptions { Enabled = true, Meta = new MetaOptions { UseMock = true } }));
        var worker = new AdvertisingCommandWorker(db, meta, vault, new AdvertisingSafetyEngine(db), emergency, ownership);

        foreach (var commandId in stop.CommandIds) await worker.ExecuteAsync(projectId, commandId);

        db.ChangeTracker.Clear();
        Assert.Equal(3, provider.PausedTargets.Count);
        Assert.All(await db.AdvertisingExecutionCommands.IgnoreQueryFilters().ToListAsync(), command => Assert.Equal(CommandState.Succeeded, command.State));
        Assert.Equal("Paused", (await db.AdvertisingEmergencyStops.IgnoreQueryFilters().SingleAsync()).State);
        Assert.Equal(ManagedDeliveryState.Paused, (await db.ManagedAdvertisements.IgnoreQueryFilters().SingleAsync()).ConfiguredStatus);
        // The exemption is protective only: the same invalid configuration must never authorize resuming.
        Assert.NotEqual(DecisionVerdict.Approve, (await new AdvertisingSafetyEngine(db).EvaluateAsync(
            new(projectId, "ResumeAd", null, null, platform, ["feed"], ad.Id, destination.Id))).Verdict);
    }

    [Theory]
    [InlineData(ManagedOwnershipKind.ManualUnowned)]
    [InlineData(ManagedOwnershipKind.ImportedWithAuthority)]
    public async Task Protective_pause_does_not_bypass_ownership_or_project_boundaries(ManagedOwnershipKind kind)
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = isolated.CreateContext();
        await db.Database.MigrateAsync();
        var projectId = Guid.NewGuid();
        var connection = new AdvertisingConnection { ProjectId = projectId, State = AdvertisingConnectionState.Ready };
        var ownership = new ManagedOwnershipRecord { ProjectId = projectId, ConnectionId = connection.Id, OwnershipKind = kind };
        var ad = new ManagedAdvertisement { ProjectId = projectId, ConnectionId = connection.Id, OwnershipRecordId = ownership.Id };
        db.AddRange(connection, ownership, ad); await db.SaveChangesAsync();
        var requesterProject = kind == ManagedOwnershipKind.ManualUnowned ? projectId : Guid.NewGuid();

        var verdict = await new AdvertisingSafetyEngine(db).EvaluateAsync(new(requesterProject, "PauseAd", null, null, "dynamic", [], ad.Id));

        Assert.Equal("ADS_TARGET_NOT_MANAGED", verdict.Code);
    }

    private sealed class DeliveryProvider : HttpMessageHandler
    {
        public HashSet<string> PausedTargets { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var target = request.RequestUri!.AbsolutePath.Split('/').Last();
            if (request.Method == HttpMethod.Post) PausedTargets.Add(target);
            var body = request.Method == HttpMethod.Post ? "{\"success\":true}" :
                $"{{\"id\":\"{target}\",\"status\":\"{(PausedTargets.Contains(target) ? "PAUSED" : "ACTIVE")}\",\"effective_status\":\"{(PausedTargets.Contains(target) ? "PAUSED" : "ACTIVE")}\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
