using Shared.Domain;

namespace Modules.QuranChallenge.Domain;

public class QuranFacebookSettings : AuditableEntity, ITenantEntity
{
    public const string DefaultCaption = "هل عرفت الكلمة الناقصة من {surah}، الآية {ayah}؟ ✨\nاكتب إجابتك قبل ظهور النتيجة.\nصلِّ على النبي ﷺ، ولا تنسَ المتابعة والإعجاب.\n#أكمل_الآية #القرآن_الكريم #ياسر_الدوسري #Reels";

    public Guid ProjectId { get; set; }
    public string? FacebookPageId { get; set; }
    public string? PageName { get; set; }
    public bool IsEnabled { get; set; }
    public int IntervalHours { get; set; } = 4;
    public string CaptionTemplate { get; set; } = DefaultCaption;
    public DateTime? NextPublishAtUtc { get; set; }
    public DateTime? LastPublishedAtUtc { get; set; }
    public string? LastReelId { get; set; }
    public string? LastError { get; set; }
}
