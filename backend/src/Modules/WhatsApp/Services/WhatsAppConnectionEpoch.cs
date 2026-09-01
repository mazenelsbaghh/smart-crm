using System;

namespace Modules.WhatsApp.Services
{
    public static class WhatsAppConnectionEpoch
    {
        public static bool Includes(DateTime eventTimestampUtc, DateTimeOffset connectedAt)
        {
            var utcTimestamp = DateTime.SpecifyKind(eventTimestampUtc, DateTimeKind.Utc);
            return new DateTimeOffset(utcTimestamp).ToUnixTimeSeconds()
                >= connectedAt.ToUnixTimeSeconds();
        }

        public static bool Matches(DateTimeOffset expected, DateTimeOffset actual) =>
            expected.ToUnixTimeMilliseconds() == actual.ToUnixTimeMilliseconds();
    }
}
