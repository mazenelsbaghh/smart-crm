using Modules.Advertising.API;
using Modules.Advertising.Services;
using System.Text.RegularExpressions;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingApiContractTests
{
    [Fact]
    public void Every_mutation_requires_a_nonempty_idempotency_key()
    {
        var error = Assert.Throws<AdvertisingException>(() =>
            AdvertisingMutationProtocol.RequireIdempotencyKey(null));

        Assert.Equal("ADS_IDEMPOTENCY_REQUIRED", error.Code);
        Assert.Equal("operation-key", AdvertisingMutationProtocol.RequireIdempotencyKey(" operation-key "));
    }

    [Fact]
    public void Concurrency_sensitive_mutations_require_a_valid_if_match()
    {
        var error = Assert.Throws<AdvertisingException>(() =>
            AdvertisingMutationProtocol.RequireIfMatch("invalid"));

        Assert.Equal("ADS_IF_MATCH_REQUIRED", error.Code);
        Assert.Equal(7, AdvertisingMutationProtocol.RequireIfMatch("\"7\""));
    }

    [Fact]
    public void Async_operation_receipt_contains_a_pollable_project_status_url()
    {
        var projectId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        var receipt = AdvertisingMutationProtocol.Accepted(projectId, operationId, Guid.NewGuid(), "Requested");

        Assert.Equal(operationId, receipt.OperationId);
        Assert.Equal($"/api/projects/{projectId}/ad-manager/operations/{operationId}", receipt.StatusUrl);
        Assert.Equal("Requested", receipt.State);
    }

    [Fact]
    public void Provider_errors_are_sanitized_before_the_api_envelope_is_created()
    {
        var envelope = AdvertisingErrorEnvelope.ProviderFailure(
            "CreateAdSet",
            "access_token=secret buyer@example.com",
            "trace-1");

        Assert.DoesNotContain("secret", envelope.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("buyer@example.com", envelope.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("trace-1", envelope.ProviderTraceId);
    }

    [Fact]
    public void Production_registers_each_critical_advertising_worker_consumer_and_schedule_once()
    {
        var program = File.ReadAllText(FindProgram());
        var registrations = new[]
        {
            "Modules.Advertising.Workers.ConnectionDisconnectWorker",
            "Modules.Advertising.Workers.AdvertisingCommandWorker",
            "Modules.Advertising.Workers.AdvertisingAiWorkResultConsumer",
            "Modules.Advertising.Workers.WhatsAppAttributionObservationConsumer",
            "Modules.Advertising.Jobs.AdvertisingRecurringJobs",
            "Modules.Advertising.Jobs.AdvertisingRetentionJob",
            "Modules.Advertising.Jobs.ConversionDeliveryJob",
            "Modules.Advertising.Jobs.AdvertisingProjectionBackfillJob",
            "Shared.Audit.ElasticsearchAuditIndexer",
            "Shared.Queue.IntegrationOutboxDispatcher"
        };
        foreach (var type in registrations)
            Assert.Single(Regex.Matches(program, $@"Add(?:Scoped|Singleton)<{Regex.Escape(type)}>\(\)"));

        var subscriptions = new[]
        {
            "AdvertisingProjectLifecycleChanged, Modules.Advertising.Jobs.AdvertisingRetentionJob",
            "AdvertisingWhatsAppDestinationChanged, Modules.WhatsApp.Workers.WhatsAppInboundRouteConsumer",
            "WhatsAppAttributionObserved, Modules.Advertising.Workers.WhatsAppAttributionObservationConsumer",
            "AdvertisingAiWorkRequested, Modules.AI.Workers.AdvertisingAiWorkConsumer",
            "AdvertisingAiWorkCompleted, Modules.Advertising.Workers.AdvertisingAiWorkResultConsumer",
            "AdvertisingAuditRecorded, Shared.Audit.ElasticsearchAuditIndexer"
        };
        foreach (var subscription in subscriptions)
            Assert.Single(Regex.Matches(program, $@"Subscribe<Shared\.(?:Queue\.)?{Regex.Escape(subscription)}>"));

        var scheduleIds = new[] { "ads-conversion-delivery", "ads-spend-monitor", "ads-provider-sync", "ads-insights",
            "ads-tracking-health", "ads-decision-cycle", "ads-creative-fatigue", "ads-daily-rebalance",
            "ads-impact-review", "ads-new-tests", "ads-strategy-review", "ads-retention",
            "ads-projection-backfill", "ads-audit-index-retry" };
        foreach (var id in scheduleIds) Assert.Single(Regex.Matches(program, "\"" + Regex.Escape(id) + "\""));
    }

    private static string FindProgram()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Program.cs");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not find backend Program.cs from the test output directory.");
    }
}
