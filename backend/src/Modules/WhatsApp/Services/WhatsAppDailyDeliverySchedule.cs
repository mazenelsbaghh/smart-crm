using System;

namespace Modules.WhatsApp.Services
{
    public static class WhatsAppDailyDeliverySchedule
    {
        public static DateTime NextOccurrenceAfter(
            DateTime originalDueUtc,
            DateTime afterUtc,
            TimeZoneInfo? timezone = null)
        {
            originalDueUtc = AsUtc(originalDueUtc);
            afterUtc = AsUtc(afterUtc);
            return OccurrenceAtBoundary(
                originalDueUtc,
                afterUtc,
                timezone ?? TimeZoneInfo.Utc,
                includeBoundary: false);
        }

        public static bool IsDueInCurrentConnection(
            DateTime originalDueUtc,
            DateTime nowUtc,
            DateTimeOffset connectedAt,
            TimeZoneInfo? timezone = null)
        {
            nowUtc = AsUtc(nowUtc);
            var scheduledFor = ScheduledOccurrenceInCurrentConnection(
                originalDueUtc,
                nowUtc,
                connectedAt,
                timezone);
            return scheduledFor <= nowUtc;
        }

        public static DateTime ScheduledOccurrenceInCurrentConnection(
            DateTime originalDueUtc,
            DateTime nowUtc,
            DateTimeOffset connectedAt,
            TimeZoneInfo? timezone = null)
        {
            originalDueUtc = AsUtc(originalDueUtc);
            nowUtc = AsUtc(nowUtc);
            var resolvedTimezone = timezone ?? TimeZoneInfo.Utc;
            var firstEligibleOccurrence = OccurrenceAtBoundary(
                originalDueUtc,
                connectedAt.UtcDateTime,
                resolvedTimezone,
                includeBoundary: true);
            if (firstEligibleOccurrence > nowUtc) return firstEligibleOccurrence;

            var latestOccurrence = OccurrenceAtOrBefore(
                originalDueUtc,
                nowUtc,
                resolvedTimezone);
            return latestOccurrence < firstEligibleOccurrence
                ? firstEligibleOccurrence
                : latestOccurrence;
        }

        private static DateTime OccurrenceAtOrBefore(
            DateTime originalDueUtc,
            DateTime boundaryUtc,
            TimeZoneInfo timezone)
        {
            if (originalDueUtc >= boundaryUtc) return originalDueUtc;

            var originalLocal = TimeZoneInfo.ConvertTimeFromUtc(originalDueUtc, timezone);
            var boundaryLocal = TimeZoneInfo.ConvertTimeFromUtc(boundaryUtc, timezone);
            var daysToAdvance = Math.Max(0, (boundaryLocal.Date - originalLocal.Date).Days);
            var candidateLocal = DateTime.SpecifyKind(
                originalLocal.AddDays(daysToAdvance),
                DateTimeKind.Unspecified);
            var candidateUtc = ConvertLocalToUtc(candidateLocal, timezone);

            while (candidateUtc > boundaryUtc)
            {
                candidateLocal = candidateLocal.AddDays(-1);
                candidateUtc = ConvertLocalToUtc(candidateLocal, timezone);
            }
            return candidateUtc;
        }

        private static DateTime OccurrenceAtBoundary(
            DateTime originalDueUtc,
            DateTime boundaryUtc,
            TimeZoneInfo timezone,
            bool includeBoundary)
        {
            if (includeBoundary ? originalDueUtc >= boundaryUtc : originalDueUtc > boundaryUtc)
                return originalDueUtc;

            var originalLocal = TimeZoneInfo.ConvertTimeFromUtc(originalDueUtc, timezone);
            var boundaryLocal = TimeZoneInfo.ConvertTimeFromUtc(boundaryUtc, timezone);
            var daysToAdvance = Math.Max(0, (boundaryLocal.Date - originalLocal.Date).Days);
            var candidateLocal = DateTime.SpecifyKind(originalLocal.AddDays(daysToAdvance), DateTimeKind.Unspecified);
            var candidateUtc = ConvertLocalToUtc(candidateLocal, timezone);

            while (includeBoundary ? candidateUtc < boundaryUtc : candidateUtc <= boundaryUtc)
            {
                candidateLocal = candidateLocal.AddDays(1);
                candidateUtc = ConvertLocalToUtc(candidateLocal, timezone);
            }
            return candidateUtc;
        }

        private static DateTime ConvertLocalToUtc(DateTime local, TimeZoneInfo timezone)
        {
            while (timezone.IsInvalidTime(local)) local = local.AddMinutes(1);
            return TimeZoneInfo.ConvertTimeToUtc(local, timezone);
        }

        private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
