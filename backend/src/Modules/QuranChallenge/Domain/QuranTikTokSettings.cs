using Shared.Domain;

namespace Modules.QuranChallenge.Domain;

public class QuranTikTokSettings : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string? OpenId { get; set; }
    public string? DisplayName { get; set; }
    public string? ProtectedAccessToken { get; set; }
    public string? ProtectedRefreshToken { get; set; }
    public DateTime? AccessTokenExpiresAtUtc { get; set; }
    public DateTime? RefreshTokenExpiresAtUtc { get; set; }
    public string? GrantedScopes { get; set; }
    public DateTime? LastPublishedAtUtc { get; set; }
    public string? LastPublishId { get; set; }
    public string? LastPublishStatus { get; set; }
    public string? LastError { get; set; }
}
