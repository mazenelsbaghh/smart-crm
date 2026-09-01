using Shared.Domain;

namespace Modules.QuranChallenge.Domain;

public class QuranTikTokSettings : AuditableEntity, ITenantEntity
{
    public const string DefaultCaption = "هل عرفت الكلمة الناقصة من {surah}، الآية {ayah}؟ ✨\nاكتب إجابتك قبل ظهور النتيجة.\nصلِّ على النبي ﷺ، ولا تنسَ المتابعة والإعجاب.\n#أكمل_الآية #القرآن_الكريم #ياسر_الدوسري";

    public Guid ProjectId { get; set; }
    public string? OpenId { get; set; }
    public string? DisplayName { get; set; }
    public string? ProtectedAccessToken { get; set; }
    public string? ProtectedRefreshToken { get; set; }
    public DateTime? AccessTokenExpiresAtUtc { get; set; }
    public DateTime? RefreshTokenExpiresAtUtc { get; set; }
    public string? GrantedScopes { get; set; }
    public bool IsEnabled { get; set; }
    public int IntervalHours { get; set; } = 4;
    public string PrivacyLevel { get; set; } = "PUBLIC_TO_EVERYONE";
    public bool AllowComment { get; set; } = true;
    public bool AllowDuet { get; set; }
    public bool AllowStitch { get; set; }
    public string CaptionTemplate { get; set; } = DefaultCaption;
    public DateTime? NextPublishAtUtc { get; set; }
    public DateTime? LastPublishedAtUtc { get; set; }
    public string? LastPublishId { get; set; }
    public string? LastPublishStatus { get; set; }
    public string? LastError { get; set; }
}
