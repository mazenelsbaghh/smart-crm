using Modules.Campaigns.Jobs;
using Xunit;

namespace Advertising.UnitTests;

public class CampaignDispatchScheduleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(49, 2205)]
    [InlineData(50, 7200)]
    [InlineData(200, 86400)]
    public void Recipient_position_respects_batch_and_daily_boundaries(int recipientIndex, int expectedSeconds)
    {
        var delay = CampaignDispatchSchedule.DelayFor(
            recipientIndex,
            CampaignDispatchLimits.SafeDefaults,
            jitterFraction: 0);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void Jitter_stays_within_the_configured_message_window()
    {
        var limits = CampaignDispatchLimits.SafeDefaults;

        var delayWithoutJitter = CampaignDispatchSchedule.DelayFor(0, limits, 0);
        var delayWithMaximumJitter = CampaignDispatchSchedule.DelayFor(0, limits, 1);

        Assert.Equal(limits.MessageJitter, delayWithMaximumJitter - delayWithoutJitter);
    }
}
