using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingCapabilityTests
{
    [Fact]
    public void Capability_requires_current_healthy_whatsapp_evidence()
    {
        var snapshot = new AdvertisingCapabilitySnapshot
        {
            State = AdvertisingCapabilityState.Healthy,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            ObjectivesJson = "[\"OUTCOME_ENGAGEMENT\"]",
            OptimizationGoalsJson = "[\"CONVERSATIONS\"]",
            PlacementEligibilityJson = "{\"automatic\":true,\"whatsappDestinationEligible\":true}"
        };

        Assert.True(AdvertisingCapabilityPolicy.CanProvisionWhatsApp(snapshot, DateTime.UtcNow).Ready);
        snapshot.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        Assert.Equal("ADS_CAPABILITY_STALE", AdvertisingCapabilityPolicy.CanProvisionWhatsApp(snapshot, DateTime.UtcNow).Code);
    }

    [Fact]
    public void Enum_presence_without_runtime_probe_is_not_capability_evidence()
    {
        var snapshot = new AdvertisingCapabilitySnapshot { State = AdvertisingCapabilityState.Healthy, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5) };
        Assert.Equal("ADS_WHATSAPP_CAPABILITY_UNPROVEN", AdvertisingCapabilityPolicy.CanProvisionWhatsApp(snapshot, DateTime.UtcNow).Code);
    }

    [Fact]
    public void Gateway_probe_proves_conversation_and_automatic_delivery_without_waba()
    {
        var snapshot = new AdvertisingCapabilitySnapshot
        {
            State = AdvertisingCapabilityState.Healthy,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            OptimizationGoalsJson = "[\"CONVERSATIONS\"]",
            PlacementEligibilityJson = "{\"automatic\":true,\"destination\":\"WHATSAPP_GATEWAY\"}"
        };

        Assert.True(AdvertisingCapabilityPolicy.CanProvisionWhatsApp(snapshot, DateTime.UtcNow).Ready);
    }
}
