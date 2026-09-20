namespace Shared.Infrastructure;

internal static class ReplyMessagePacing
{
    public const int AverageDelayMs = 30_000;

    // Random.Next excludes its upper bound; include the full 25–35 second range.
    public static int NextDelayMs() => System.Random.Shared.Next(25_000, 35_001);
}
