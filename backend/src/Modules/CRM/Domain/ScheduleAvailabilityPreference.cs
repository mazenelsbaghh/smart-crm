using Shared.Domain;

namespace Modules.CRM.Domain;

public static class ScheduleAvailabilityWindows
{
    public const string NoonToFour = "12-16";
    public const string FourToEight = "16-20";
    public const string EightToMidnight = "20-24";

    public static bool IsValid(string? value) =>
        value is NoonToFour or FourToEight or EightToMidnight;

    public static bool ContainsHour(string value, int hour) => value switch
    {
        NoonToFour => hour is >= 12 and < 16,
        FourToEight => hour is >= 16 and < 20,
        EightToMidnight => hour is >= 20 and < 24,
        _ => false
    };

    public static string ArabicLabel(string value) => value switch
    {
        NoonToFour => "من ١٢ ظهرًا إلى ٤ عصرًا",
        FourToEight => "من ٤ عصرًا إلى ٨ مساءً",
        EightToMidnight => "من ٨ مساءً إلى ١٢ منتصف الليل",
        _ => value
    };
}

public static class ScheduleAvailabilityHorizons
{
    public const string NextWeek = "NextWeek";
    public const string NextMonth = "NextMonth";
    public const string NextThreeMonths = "NextThreeMonths";
    public const string AnyTime = "AnyTime";

    public static bool IsValid(string? value) =>
        value is NextWeek or NextMonth or NextThreeMonths or AnyTime;

    public static DateTime? DeadlineUtc(string value, DateTime fromUtc) => value switch
    {
        NextWeek => fromUtc.AddDays(7),
        NextMonth => fromUtc.AddMonths(1),
        NextThreeMonths => fromUtc.AddMonths(3),
        _ => null
    };
}

public sealed class ScheduleAvailabilityPreference : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? WhatsAppAccountId { get; set; }
    public string Channel { get; set; } = "WhatsApp";
    public string TimeWindow { get; set; } = string.Empty;
    public string AvailabilityHorizon { get; set; } = ScheduleAvailabilityHorizons.AnyTime;
    public string Status { get; set; } = "Waiting";
    public Guid? MatchedGroupAppointmentId { get; set; }
    public Guid? NotificationFollowUpId { get; set; }
}
