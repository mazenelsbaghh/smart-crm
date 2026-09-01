using Modules.Advertising.Domain;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingPrimitivesTests
{
    [Fact]
    public void Matching_whatsapp_delivery_evidence_satisfies_the_invariant()
    {
        var evidence = DeliveryEvidence();

        var result = AdvertisingInvariants.ValidateWhatsAppDestination(evidence, evidence);

        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    [Theory]
    [InlineData("WEBSITE", "WHATSAPP_MESSAGE", "WHATSAPP")]
    [InlineData("WHATSAPP", "LEARN_MORE", "WHATSAPP")]
    [InlineData("WHATSAPP", "WHATSAPP_MESSAGE", "MESSENGER")]
    public void Non_whatsapp_destination_or_cta_fails_closed(
        string destinationType,
        string callToAction,
        string appDestination)
    {
        var planned = DeliveryEvidence();
        var effective = planned with
        {
            DestinationType = destinationType,
            CallToAction = callToAction,
            AppDestination = appDestination
        };

        var result = AdvertisingInvariants.ValidateWhatsAppDestination(planned, effective);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, violation => violation.Severity == InvariantSeverity.Blocking);
    }

    [Fact]
    public void A_different_phone_identity_is_a_blocking_drift()
    {
        var planned = DeliveryEvidence();
        var effective = planned with { PhoneExternalId = "phone_2" };

        var result = AdvertisingInvariants.ValidateWhatsAppDestination(planned, effective);

        var violation = Assert.Single(result.Violations);
        Assert.Equal("promoted_object.whatsapp_phone_number", violation.Field);
        Assert.Equal(InvariantSeverity.Blocking, violation.Severity);
    }

    [Fact]
    public void Unknown_provider_result_can_only_enter_reconciliation()
    {
        Assert.True(AdvertisingStateMachine.CanTransition(
            ProviderReconciliationState.Unknown,
            ProviderReconciliationState.Reconciling));
        Assert.False(AdvertisingStateMachine.CanTransition(
            ProviderReconciliationState.Unknown,
            ProviderReconciliationState.Active));
        Assert.False(AdvertisingStateMachine.CanTransition(
            ProviderReconciliationState.Unknown,
            ProviderReconciliationState.VerifiedPaused));
    }

    [Fact]
    public void Activation_requires_a_verified_paused_delivery_object()
    {
        Assert.True(AdvertisingStateMachine.CanTransition(
            ProviderReconciliationState.VerifiedPaused,
            ProviderReconciliationState.ActivationQueued));
        Assert.False(AdvertisingStateMachine.CanTransition(
            ProviderReconciliationState.PausedUnverified,
            ProviderReconciliationState.ActivationQueued));
    }

    private static WhatsAppDeliveryEvidence DeliveryEvidence() => new(
        DestinationId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        PageExternalId: "page_1",
        PhoneExternalId: "phone_1",
        DestinationType: "WHATSAPP",
        CallToAction: "WHATSAPP_MESSAGE",
        AppDestination: "WHATSAPP");
}
