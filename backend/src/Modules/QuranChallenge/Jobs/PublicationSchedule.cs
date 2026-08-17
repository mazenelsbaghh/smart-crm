namespace Modules.QuranChallenge.Jobs;

internal static class PublicationSchedule
{
    public static DateTime NextSlot(DateTime? currentSlot, int intervalHours, DateTime now)
    {
        var nextSlot = (currentSlot ?? now).AddHours(intervalHours);
        while (nextSlot <= now) nextSlot = nextSlot.AddHours(intervalHours);
        return nextSlot;
    }
}
