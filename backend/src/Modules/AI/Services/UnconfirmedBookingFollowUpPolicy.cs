namespace Modules.AI.Services;

internal static class UnconfirmedBookingFollowUpPolicy
{
    public static void Apply(SuggestedFollowUpResult? followUp)
    {
        if (followUp is not { Needed: true }) return;
        followUp.AppointmentTime = null;
        if (string.Equals(followUp.Type, "AppointmentReminder", StringComparison.OrdinalIgnoreCase))
            followUp.Needed = false;
        else
            followUp.Notes = "تحب نوضح لحضرتك أي تفاصيل عن الكورس أو الحصة المجانية؟";
    }
}
