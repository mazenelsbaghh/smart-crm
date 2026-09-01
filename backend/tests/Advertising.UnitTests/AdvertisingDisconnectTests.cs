using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingDisconnectTests
{
    [Fact]
    public void Pause_managed_is_the_default_and_credentials_are_disposed_last()
    {
        Assert.Equal(DisconnectMode.PauseManaged, AdvertisingDisconnectPolicy.NormalizeMode(null));
        Assert.Equal(DisconnectPhase.AuthoritySuspended, AdvertisingDisconnectPolicy.Next(DisconnectPhase.Requested, allTargetsPaused: false));
        Assert.Equal(DisconnectPhase.ReconcilingPauses, AdvertisingDisconnectPolicy.Next(DisconnectPhase.ProtectiveStopQueued, allTargetsPaused: false));
        Assert.Equal(DisconnectPhase.DisposingCredential, AdvertisingDisconnectPolicy.Next(DisconnectPhase.ReconcilingPauses, allTargetsPaused: true));
    }

    [Fact]
    public void Leave_running_requires_a_fresh_explicit_acknowledgement()
    {
        var now = DateTime.UtcNow;
        Assert.False(AdvertisingDisconnectPolicy.CanLeaveRunning(null, now));
        Assert.False(AdvertisingDisconnectPolicy.CanLeaveRunning(now.AddMinutes(-16), now));
        Assert.True(AdvertisingDisconnectPolicy.CanLeaveRunning(now.AddMinutes(-1), now));
    }
}
