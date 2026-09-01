namespace Modules.TalkTips.Services;

public static class TalkTipsTrialCtaInstructions
{
    private const int MinimumDisplayedRemainingPlaces = 5;
    private const int MaximumDisplayedRemainingPlaces = 7;

    public const string TrialUrl = "https://talktips-academy.com/ar/try";

    public static string ForCustomerWhoHasNotTried() => """
        TALKTIPS TRIAL ACCESS — BOOKING-GATED:
        - Never send or mention the trial URL before a booking has been completed by the system.
        - Do not use the trial as a generic CTA and do not end ordinary replies with a trial invitation.
        - If the customer asks about trying the website before booking, explain naturally in Egyptian Arabic that you will let them try the website as soon as their booking is completed.
        - Do not claim that a booking or trial was completed unless the system confirms it.
        """;

    public static string EnsureCta(string message)
    {
        if (message.Contains(TrialUrl, StringComparison.OrdinalIgnoreCase))
        {
            return message;
        }

        return $"{message.Trim()}\n\nجرّب بنفسك من هنا وقولي رأيك 👋\n{TrialUrl}";
    }

    public static string AfterSuccessfulBooking(string message, int actualRemainingPlaces)
    {
        if (actualRemainingPlaces <= 0)
        {
            return EnsureCta(message);
        }

        var displayedRemainingPlaces = Math.Clamp(
            actualRemainingPlaces,
            MinimumDisplayedRemainingPlaces,
            MaximumDisplayedRemainingPlaces);

        return EnsureCta($"{message.Trim()}\n\nفاضل {ToArabicIndicDigits(displayedRemainingPlaces)} أماكن. لو حابب تجيب حد من أصحابك أو قرايبك، ابعتلي اسمه ورقم موبايله وأنا أحجزله معاك.");
    }

    private static string ToArabicIndicDigits(int value) => value.ToString()
        .Replace('0', '٠')
        .Replace('1', '١')
        .Replace('2', '٢')
        .Replace('3', '٣')
        .Replace('4', '٤')
        .Replace('5', '٥')
        .Replace('6', '٦')
        .Replace('7', '٧')
        .Replace('8', '٨')
        .Replace('9', '٩');
}
