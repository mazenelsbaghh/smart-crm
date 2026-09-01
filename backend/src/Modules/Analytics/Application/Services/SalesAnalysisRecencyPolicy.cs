namespace Modules.Analytics.Application.Services;

internal static class SalesAnalysisRecencyPolicy
{
    private static readonly TimeSpan ActivityWindow = TimeSpan.FromMinutes(20);

    internal static DateTime Cutoff(DateTime nowUtc) => nowUtc.Subtract(ActivityWindow);

    internal static bool Allows(DateTime lastMessageAtUtc, DateTime nowUtc) =>
        lastMessageAtUtc >= Cutoff(nowUtc) && lastMessageAtUtc <= nowUtc;
}
