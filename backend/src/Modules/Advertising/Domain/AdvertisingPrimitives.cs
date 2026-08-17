namespace Modules.Advertising.Domain;

public enum AdvertisingConnectionState { PendingSelection, Ready, Degraded, ReconnectRequired, Revoked }
public enum EnvelopeState { Draft, Active, Suspended, Revoked, Expired }
public enum PromotionState { Draft, Ready, Canary, Active, Paused, Completed, Blocked }
public enum ManagedDeliveryState { Draft, Paused, Active, Rejected, Archived }
public enum CreativeSourceType { ExistingPagePost, ProjectAsset }
public enum CreativeMediaType { Image, Carousel, Video }
public enum CreativeEligibility { Pending, Eligible, Ineligible, Stale }
public enum BudgetPurpose { Winner, CreativeTest, AudienceTest, Retargeting, Canary }
public enum ConversionState { Observed, Verified, Attributed, Delivered, Unattributed, Suppressed, Corrected, DeliveryFailed }
public enum ConsentState { Unknown, Granted, Denied, NotRequired }
public enum DecisionVerdict { Approve, Reject, Wait, Escalate }
public enum DecisionState { Proposed, Reviewing, Waiting, Rejected, Approved, Executing, Executed, Failed, Superseded }
public enum CommandState { Pending, Claimed, Sent, Succeeded, Failed, Unknown, Reconciling, Stale, Blocked, Cancelled }
public enum IncidentState { Open, Acknowledged, Recovered }
public enum EmergencyTrigger { Manual, AbnormalSpend, CapRisk, CrossProjectGuard, Provider }

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
