using Shared.Domain;

namespace Modules.QuranChallenge.Domain;

public class QuranYouTubeSettings : AuditableEntity, ITenantEntity
{
    public const string DefaultCaption = "هل عرفت الكلمة الناقصة من {surah}، الآية {ayah}؟ ✨\nاكتب إجابتك قبل ظهور النتيجة.\nصلِّ على النبي ﷺ، ولا تنسَ الإعجاب والاشتراك.\n#أكمل_الآية #القرآن_الكريم #ياسر_الدوسري #Shorts";

    public Guid ProjectId { get; set; }
    public string? ChannelId { get; set; }
    public string? ChannelTitle { get; set; }
    public string? ProtectedRefreshToken { get; set; }
    public bool IsEnabled { get; set; }
    public int IntervalHours { get; set; } = 4;
    public string PrivacyStatus { get; set; } = "public";
    public string CaptionTemplate { get; set; } = DefaultCaption;
    public DateTime? NextPublishAtUtc { get; set; }
    public DateTime? LastPublishedAtUtc { get; set; }
    public string? LastVideoId { get; set; }
    public string? LastError { get; set; }
}
