using System;

namespace Modules.Campaigns.Jobs
{
    public sealed record CampaignDispatchLimits(
        int BatchSize,
        int BatchesPerDay,
        TimeSpan BatchGap,
        TimeSpan MinimumMessageGap,
        TimeSpan MessageJitter)
    {
        public static CampaignDispatchLimits SafeDefaults { get; } = new(
            BatchSize: 50,
            BatchesPerDay: 4,
            BatchGap: TimeSpan.FromHours(2),
            MinimumMessageGap: TimeSpan.FromSeconds(45),
            MessageJitter: TimeSpan.FromSeconds(45));
    }

    public static class CampaignDispatchSchedule
    {
        public static TimeSpan DelayFor(int recipientIndex, CampaignDispatchLimits limits, double jitterFraction)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(recipientIndex);
            Validate(limits);

            if (jitterFraction is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(jitterFraction));
            }

            var batchIndex = recipientIndex / limits.BatchSize;
            var batchPosition = recipientIndex % limits.BatchSize;
            var dayIndex = batchIndex / limits.BatchesPerDay;
            var batchIndexWithinDay = batchIndex % limits.BatchesPerDay;
            var jitter = TimeSpan.FromTicks((long)(limits.MessageJitter.Ticks * jitterFraction));

            return TimeSpan.FromDays(dayIndex)
                + limits.BatchGap * batchIndexWithinDay
                + limits.MinimumMessageGap * batchPosition
                + jitter;
        }

        private static void Validate(CampaignDispatchLimits limits)
        {
            if (limits.BatchSize <= 0 || limits.BatchesPerDay <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limits));
            }

            if (limits.BatchGap < TimeSpan.Zero || limits.MinimumMessageGap < TimeSpan.Zero || limits.MessageJitter < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(limits));
            }
        }
    }
}
