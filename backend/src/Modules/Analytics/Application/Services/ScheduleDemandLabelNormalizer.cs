using System.Text.RegularExpressions;

namespace Modules.Analytics.Application.Services;

public static partial class ScheduleDemandLabelNormalizer
{
    public const string NoonToFour = "من ١٢ إلى ٤";
    public const string FourToEight = "من ٤ إلى ٨";
    public const string EightToMidnight = "من ٨ إلى ١٢";
    public const string NextWeek = "الأسبوع الجاي";
    public const string NextMonth = "الشهر الجاي";
    public const string NextThreeMonths = "خلال ٣ شهور";
    public const string AnyTime = "أي وقت";

    public static string Normalize(string? requestText, string? extractedLabel)
    {
        var source = NormalizeArabic($"{requestText} {extractedLabel}");
        if (MentionsThreeMonths(source)) return NextThreeMonths;
        if (source.Contains("اسبوع")) return NextWeek;
        if (MentionsMonth(source)) return NextMonth;
        if (source.Contains("صباح") || source.Contains("ظهر")) return NoonToFour;
        if (source.Contains("مساء") && !ClockHour().IsMatch(source)) return FourToEight;
        var hourMatch = ClockHour().Match(source);
        if (hourMatch.Success && int.TryParse(hourMatch.Groups[1].Value, out var hour))
            return HourWindow(hour);
        return AnyTime;
    }

    private static bool MentionsThreeMonths(string source) =>
        source.Contains("3 شه") || source.Contains("ثلاث شه");

    private static bool MentionsMonth(string source) =>
        source.Contains("شهر") || source.Contains("سبتمبر") || source.Contains("اكتوبر")
        || source.Contains("نوفمبر") || source.Contains("ديسمبر") || source.Contains("يناير")
        || source.Contains("فبراير") || source.Contains("مارس") || source.Contains("ابريل")
        || source.Contains("مايو") || source.Contains("يونيو") || source.Contains("يوليو")
        || source.Contains("اغسطس");

    private static string HourWindow(int hour)
    {
        var twelveHour = hour >= 12 ? hour % 12 : hour;
        if (twelveHour is >= 8 and <= 11) return EightToMidnight;
        if (twelveHour is >= 4 and <= 7) return FourToEight;
        return NoonToFour;
    }

    private static string NormalizeArabic(string source) => source.Trim().ToLowerInvariant()
        .Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ى', 'ي')
        .Replace('ة', 'ه').Replace('٠', '0').Replace('١', '1').Replace('٢', '2')
        .Replace('٣', '3').Replace('٤', '4').Replace('٥', '5').Replace('٦', '6')
        .Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');

    [GeneratedRegex(@"(?:الساعه|بعد|قبل)?\s*(\d{1,2})")]
    private static partial Regex ClockHour();
}
