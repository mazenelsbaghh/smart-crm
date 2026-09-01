namespace Modules.Advertising.Domain;

public static class AdvertisingStateMachine
{
    private static readonly IReadOnlyDictionary<ProviderReconciliationState, HashSet<ProviderReconciliationState>> AllowedTransitions =
        new Dictionary<ProviderReconciliationState, HashSet<ProviderReconciliationState>>
        {
            [ProviderReconciliationState.Draft] = [ProviderReconciliationState.Creating, ProviderReconciliationState.Rejected],
            [ProviderReconciliationState.Creating] = [ProviderReconciliationState.Partial, ProviderReconciliationState.PausedUnverified, ProviderReconciliationState.Unknown, ProviderReconciliationState.Rejected],
            [ProviderReconciliationState.Partial] = [ProviderReconciliationState.Reconciling, ProviderReconciliationState.Paused],
            [ProviderReconciliationState.PausedUnverified] = [ProviderReconciliationState.VerifiedPaused, ProviderReconciliationState.Drifted, ProviderReconciliationState.Rejected],
            [ProviderReconciliationState.VerifiedPaused] = [ProviderReconciliationState.ActivationQueued, ProviderReconciliationState.Paused, ProviderReconciliationState.Drifted],
            [ProviderReconciliationState.ActivationQueued] = [ProviderReconciliationState.Active, ProviderReconciliationState.Unknown, ProviderReconciliationState.Drifted, ProviderReconciliationState.Paused],
            [ProviderReconciliationState.Active] = [ProviderReconciliationState.Paused, ProviderReconciliationState.Drifted, ProviderReconciliationState.Unknown],
            [ProviderReconciliationState.Unknown] = [ProviderReconciliationState.Reconciling],
            [ProviderReconciliationState.Reconciling] = [ProviderReconciliationState.VerifiedPaused, ProviderReconciliationState.Active, ProviderReconciliationState.Paused, ProviderReconciliationState.Drifted, ProviderReconciliationState.Rejected],
            [ProviderReconciliationState.Drifted] = [ProviderReconciliationState.Reconciling, ProviderReconciliationState.Paused],
            [ProviderReconciliationState.Paused] = [ProviderReconciliationState.VerifiedPaused, ProviderReconciliationState.Reconciling],
            [ProviderReconciliationState.LegacyUnverified] = [ProviderReconciliationState.Reconciling],
            [ProviderReconciliationState.Rejected] = [],
            [ProviderReconciliationState.Archived] = []
        };

    public static bool CanTransition(ProviderReconciliationState from, ProviderReconciliationState to) =>
        AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static void RequireTransition(ProviderReconciliationState from, ProviderReconciliationState to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"ADS_STATE_TRANSITION_INVALID: {from} cannot transition to {to}.");
    }
}
