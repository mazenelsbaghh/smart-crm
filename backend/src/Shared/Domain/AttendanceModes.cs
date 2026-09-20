namespace Shared.Domain;

public static class AttendanceModes
{
    public const string Unknown = "Unknown";
    public const string Online = "Online";
    public const string Offline = "Offline";
    public const string Either = "Either";

    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "online" => Online,
        "offline" => Offline,
        "either" => Either,
        _ => Unknown
    };
}
