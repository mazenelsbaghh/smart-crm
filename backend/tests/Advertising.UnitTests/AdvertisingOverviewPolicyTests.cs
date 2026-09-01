using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingOverviewPolicyTests
{
    [Theory]
    [InlineData(false, 0, 0, false, false, "PausingManaged", "PausingManaged", true)]
    [InlineData(true, 2, 2, false, false, "PausingManaged", "Completed", false)]
    [InlineData(true, 2, 2, false, true, "PausingManaged", "NeedsAttention", true)]
    [InlineData(true, 2, 1, false, true, "PausingManaged", "PausingManaged", true)]
    [InlineData(true, 2, 1, true, true, "PausingManaged", "NeedsAttention", true)]
    public void Pause_status_reflects_live_commands_and_preserves_no_command_state(
        bool hasCommands,
        int total,
        int succeeded,
        bool needsAttention,
        bool managedDeliveryMayContinue,
        string persistedState,
        string expectedState,
        bool expectedPauseOngoing)
    {
        var disableRequest = new AutopilotDisableRequest
        {
            Mode = AutopilotDisableMode.PauseManaged,
            State = persistedState
        };
        var commands = hasCommands
            ? new AdvertisingOverviewDisableCommandMetrics(total, succeeded, needsAttention)
            : null;

        var status = AdvertisingOverviewQuery.DisableStatus(
            disableRequest, commands, managedDeliveryMayContinue);

        Assert.Equal(expectedState, status.State);
        Assert.Equal(expectedPauseOngoing, status.PauseOngoing);
    }
}
