using System.Globalization;
using System.Text.RegularExpressions;
using Modules.GroupAppointments.Domain;

namespace Modules.GroupAppointments.Services;

internal static partial class GroupBookingTimeChoicePolicy
{
    public static bool Conflicts(GroupAppointment group, string? timezone, IEnumerable<string> customerMessages)
    {
        var session = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(group.FreeSessionDateTime ?? group.DateTime, DateTimeKind.Utc),
            Shared.Infrastructure.TimezoneHelper.GetTimeZone(timezone));
        foreach (var message in customerMessages.Reverse())
        {
            var normalized = Normalize(message);
            var matches = SelectedTime().Matches(normalized);
            if (matches.Count == 0) continue;
            // Several times or a declined time need clarification, never a guessed booking.
            if (matches.Count != 1 || Regex.IsMatch(normalized,
                @"(?<!\p{L})(?:[وف]?مش|مينفعش|لا يناسب|مشغوله?)(?!\p{L})|من\s+\d{1,2}(?:[:：]\d{1,2})?\s*(?:ل|الى|الي)\s*\d{1,2}")) return true;
            return !MatchesSession(matches[0], session);
        }
        return false;
    }

    private static bool MatchesSession(Match choice, DateTime session)
    {
        var hour = int.Parse(choice.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minute = choice.Groups["minute"].Success ? int.Parse(choice.Groups["minute"].Value, CultureInfo.InvariantCulture) : 0;
        var period = choice.Groups["period"].Value;
        if (hour > 23 || minute > 59 || period.Length > 0 && hour > 12) return false;
        if (period is "م" or "مساء" or "بالليل" or "pm") hour = hour % 12 + 12;
        else if (period is "ص" or "صباحا" or "الصبح" or "am") hour %= 12;
        return minute == session.Minute && (period.Length > 0 || hour is 0 or > 12
            ? hour == session.Hour : hour % 12 == session.Hour % 12);
    }

    private static string Normalize(string message) => string.Concat(message.ToLowerInvariant()
        .Replace('أ', 'ا').Replace('إ', 'ا').Replace('ة', 'ه')
        .Where(c => c is not (>= '\u064B' and <= '\u065F'))
        .Select(c => char.IsDigit(c) ? (char)('0' + char.GetNumericValue(c)) : c));

    [GeneratedRegex(@"(?:(?:الساعه|ساعه|معاد|ميعاد|موعد|(?:ف?هكون\s+)?موجوده?|هحضر|هاجي)\s*|^\s*(?=\d{1,2}\s*[.!]*$)|(?:^|\s)(?=\d{1,2}(?:[:：]|\s*(?:مساء|بالليل|صباحا|الصبح|am|pm))))(?<hour>\d{1,2})(?:[:：](?<minute>\d{1,2}))?\s*(?<period>مساء|بالليل|صباحا|الصبح|am|pm|م\b|ص\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex SelectedTime();
}
