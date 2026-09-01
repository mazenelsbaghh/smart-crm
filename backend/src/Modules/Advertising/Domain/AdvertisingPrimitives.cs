using System.Text.Json.Serialization;

namespace Modules.Advertising.Domain;

public enum AdvertisingProvider { Meta }
public enum AdvertisingConnectionState { PendingSelection, Ready, Degraded, ReconnectRequired, Disconnecting, Revoked }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WhatsAppIntegrationMode { CloudApi, CloudApiCoexistence, BaileysObservedExperimental }
public enum ReferralProofState { Unverified, CtwaClidObserved, Missing, Unsupported }
public enum AuthorizedDestinationState { Pending, Eligible, Degraded, Ineligible, Revoking, Revoked }
public enum AdvertisingCapabilityState { Healthy, Partial, Unsupported, Failed }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DisconnectMode { PauseManaged, LeaveRunning, ForceRevoke }
public enum DisconnectPhase { Requested, AuthoritySuspended, ProtectiveStopQueued, ReconcilingPauses, DisposingCredential, PublishingRouteTombstone, Completed, ManualActionRequired, Failed }
public enum PlacementPolicy { DynamicEligibleMeta }
public enum EnvelopeState { Draft, Active, Suspended, Revoked, Expired }
public enum PromotionState { Draft, Ready, Canary, Active, Paused, Completed, Blocked }
public enum ManagedDeliveryState { Draft, Paused, Active, Rejected, Archived }
public enum CreativeSourceType { ExistingPagePost, ProjectAsset }
public enum CreativeMediaType { Image, Carousel, Video }
public enum CreativeEligibility { Pending, Eligible, Ineligible, Stale }
public enum BudgetPurpose { Winner, CreativeTest, AudienceTest, Retargeting, Canary, Baseline }
public enum ConversionState { Observed, Verified, Attributed, Delivered, Unattributed, Suppressed, Corrected, DeliveryFailed }
public enum ConsentState { Unknown, Granted, Denied, NotRequired }
public enum ReferralIdentifierState { CtwaClid, OpaquePayloadOnly, Missing, Invalid }
public enum AttributionState { Pending, Attributed, Unattributed, Expired, Conflict }
public enum CorrectionState { None, PendingBase, Corrected }
public enum ConversionDeliveryState { Pending, Accepted, RetryScheduled, Suppressed, FailedTerminal }
public enum WebhookSourceState { Active, Rotating, Revoked }
public enum TruthSource { FirstPartyVerified, MetaReported, Crm, AiClassification, Unattributed }
public enum TrackingHealthState { Unknown, Healthy, Degraded, Unsafe }
public enum DecisionVerdict { Approve, Reject, Wait, Escalate }
public enum DecisionState { Proposed, Reviewing, Waiting, Rejected, Approved, Executing, Executed, Failed, Superseded }
public enum AiWorkState { Pending, Completed, Failed, Expired, Stale }
public enum AiWorkCompletionDecision { Accept, RejectState, RejectOwner, RejectVersion, RejectHash, RejectExpired }
public enum DecisionImpactLabel { Positive, Negative, Inconclusive, Reverted }
public enum AutopilotDisableMode { PauseManaged, LeaveRunning }
public enum CommandState { Pending, Claimed, Sent, Succeeded, Failed, Unknown, Reconciling, Stale, Blocked, Cancelled }
public enum IncidentState { Open, Acknowledged, Recovered }
public enum EmergencyTrigger { Manual, AbnormalSpend, CapRisk, CrossProjectGuard, Provider, TrackingUnsafe, RepeatedFinancialCommands, LostAuthorization }
public enum ManagedOwnershipKind { AutopilotCreated, ImportedWithAuthority, ManualUnowned }
public enum PeriodCapKind { Total, Monthly }
public enum AudienceSourceType { CustomAudience, CustomerList, Engagement, Retargeting, LookalikeSeed }
public enum ProviderOperationState { Pending, Claimed, Sent, Succeeded, Failed, Unknown, Reconciling, Stale, Blocked, Cancelled }
public enum ProviderReconciliationState { Draft, Creating, Partial, PausedUnverified, VerifiedPaused, ActivationQueued, Active, Rejected, Unknown, Reconciling, Drifted, Paused, LegacyUnverified, Archived }
public enum ProviderCreativeVerificationState { Unverified, Verified, Rejected, Drifted }
public enum AutonomousActionType
{
    CreatePlan,
    ValidatePlan,
    ProvisionPlan,
    ActivatePlan,
    PauseDelivery,
    ResumeDelivery,
    ReplaceCreative,
    StartExperiment,
    StopExperiment,
    AdjustAudienceSuggestion,
    ReserveBudget,
    ReallocateBudget,
    ReleaseBudget,
    ScaleWinner,
    ChangeOptimizationOutcome,
    RepairProviderDrift,
    Wait,
    Escalate
}

public enum InvariantSeverity { Info, Warning, Blocking }

public sealed record WhatsAppDeliveryEvidence(
    Guid DestinationId,
    string PageExternalId,
    string PhoneExternalId,
    string DestinationType,
    string CallToAction,
    string AppDestination);

public sealed record InvariantViolation(
    string Field,
    string Code,
    InvariantSeverity Severity,
    string Message);

public sealed record InvariantResult(IReadOnlyList<InvariantViolation> Violations)
{
    public bool IsValid => Violations.All(violation => violation.Severity != InvariantSeverity.Blocking);
}

public sealed record AdvertisingAiWorkItemSnapshot(
    Guid Id,
    Guid ProjectId,
    Guid OwnerId,
    long OwnerVersion,
    string InputHash,
    AiWorkState State,
    DateTime DeadlineUtc);

public sealed record AdvertisingAiWorkCompletion(
    Guid WorkItemId,
    Guid ProjectId,
    Guid OwnerId,
    long OwnerVersion,
    string InputHash,
    string ResultJson);

public static class AdvertisingInvariants
{
    public static InvariantResult ValidateWhatsAppDestination(
        WhatsAppDeliveryEvidence planned,
        WhatsAppDeliveryEvidence effective)
    {
        var violations = new List<InvariantViolation>();
        AddMismatch(violations, "destinationId", planned.DestinationId, effective.DestinationId);
        AddMismatch(violations, "page_id", planned.PageExternalId, effective.PageExternalId);
        AddMismatch(violations, "promoted_object.whatsapp_phone_number", planned.PhoneExternalId, effective.PhoneExternalId);
        RequireExact(violations, "destination_type", effective.DestinationType, "WHATSAPP");
        RequireExact(violations, "call_to_action.type", effective.CallToAction, "WHATSAPP_MESSAGE");
        RequireExact(violations, "call_to_action.value.app_destination", effective.AppDestination, "WHATSAPP");
        return new(violations);
    }

    private static void AddMismatch<T>(List<InvariantViolation> violations, string field, T planned, T effective)
    {
        if (!EqualityComparer<T>.Default.Equals(planned, effective))
            violations.Add(Blocking(field, "ADS_WHATSAPP_DESTINATION_DRIFT", "Effective WhatsApp destination differs from the approved plan."));
    }

    private static void RequireExact(List<InvariantViolation> violations, string field, string effective, string expected)
    {
        if (!string.Equals(effective, expected, StringComparison.Ordinal))
            violations.Add(Blocking(field, "ADS_WHATSAPP_INVARIANT_FAILED", $"{field} must be {expected}."));
    }

    private static InvariantViolation Blocking(string field, string code, string message) =>
        new(field, code, InvariantSeverity.Blocking, message);
}

public static class FacebookPlacementPolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "feed", "story", "facebook_reels", "marketplace", "search"
    };

    public static IReadOnlyCollection<string> AllowedPositions => Allowed;

    public static bool IsAllowed(string publisherPlatform, IEnumerable<string> positions) =>
        string.Equals(publisherPlatform, "facebook", StringComparison.OrdinalIgnoreCase)
        && positions.Any()
        && positions.All(Allowed.Contains);
}
