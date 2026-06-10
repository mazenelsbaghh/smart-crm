using System;

namespace Shared.Infrastructure
{
    public static class TimezoneHelper
    {
        public static TimeZoneInfo GetTimeZone(string? timezoneId)
        {
            if (!string.IsNullOrWhiteSpace(timezoneId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                }
                catch
                {
                    // Fall back to Cairo and then UTC
                }
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
                }
                catch
                {
                    return TimeZoneInfo.Utc;
                }
            }
        }
    }
}
