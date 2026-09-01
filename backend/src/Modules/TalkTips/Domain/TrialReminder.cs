using Shared.Domain;

namespace Modules.TalkTips.Domain;

public sealed class TrialReminder : Entity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime FirstPromptedAtUtc { get; set; }
    public DateTime? OneMinuteReminderSentAtUtc { get; set; }
    public DateTime? FiveMinuteReminderSentAtUtc { get; set; }
    public DateTime? DayReminderSentAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
