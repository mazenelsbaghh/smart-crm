using System.Text.RegularExpressions;
using Shared.Domain;

namespace Modules.GroupAppointments.Services;

internal static class GroupBookingAttendancePolicy
{
    public static bool HasKnownCity(string? city) => NormalizeCity(city) is not
        ("" or "missing" or "unknown" or "n/a" or "غير محدد" or "غير معروف");

    public static bool IsAlexandria(string? city) => Regex.IsMatch(NormalizeCity(city),
        @"^(?:(?:محافظه|مدينه)\s+)?(?:ال)?اسكندري[ها](?:\s*[,،-]?\s*مصر)?$|^(?:alexandria|alex)(?:\s*[,،-]?\s*egypt)?$");

    public static string? EffectiveCity(string? currentCity, string? storedCity) =>
        HasKnownCity(currentCity) ? currentCity : storedCity;

    // Unknown location permits online; in-person attendance requires explicit Alexandria evidence.
    public static GroupBookingStatus? Rejection(string groupMode, string? city, string? attendanceMode)
    {
        var targetMode = AttendanceModes.Normalize(groupMode);
        var requestedMode = AttendanceModes.Normalize(attendanceMode);
        if (targetMode is not (AttendanceModes.Online or AttendanceModes.Offline))
            return GroupBookingStatus.InvalidRequest;
        if (HasKnownCity(city) && !IsAlexandria(city))
            return targetMode == AttendanceModes.Online ? null : GroupBookingStatus.OnlineRequired;
        if (targetMode == AttendanceModes.Offline)
        {
            if (requestedMode == AttendanceModes.Online)
                return GroupBookingStatus.OnlineRequired;
            if (!HasKnownCity(city)) return GroupBookingStatus.CityRequired;
        }
        return requestedMode == AttendanceModes.Offline && targetMode == AttendanceModes.Online
            ? GroupBookingStatus.AttendanceModeMismatch : null;
    }

    private static string NormalizeCity(string? city) =>
        Regex.Replace((city ?? "").Trim().ToLowerInvariant(), @"[\u064B-\u065F\u0670\u0640]", "")
            .Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ى', 'ي').Replace('ة', 'ه');
}
