using Shared.Domain;

namespace Modules.Content.Domain;

public enum ContentDocumentKind { Presentation = 0, A4 = 1 }
public enum ContentDocumentStatus { Planning = 0, GeneratingImages = 1, Ready = 2, Failed = 3, AwaitingDesign = 4 }
public enum ContentDocumentPageStatus { Planned = 0, GeneratingImage = 1, Ready = 2, ImageFailed = 3, Queued = 4 }

public sealed class ContentDocument : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public ContentDocumentKind Kind { get; set; }
    public ContentDocumentStatus Status { get; set; } = ContentDocumentStatus.Planning;
    public string Title { get; set; } = string.Empty;
    public string SourceContent { get; set; } = string.Empty;
    public int RequestedPageCount { get; set; }
    public string BrandLogoObjectKey { get; set; } = string.Empty;
    public string BrandColorsJson { get; set; } = "[]";
    public string BrandStylePrompt { get; set; } = string.Empty;
    public string PlannerModel { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class ContentDocumentPage : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid DocumentId { get; set; }
    public int PageIndex { get; set; }
    public ContentDocumentPageStatus Status { get; set; } = ContentDocumentPageStatus.Planned;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string ImagePrompt { get; set; } = string.Empty;
    public string? ImageObjectKey { get; set; }
    public string ImageMimeType { get; set; } = "image/png";
    public string? Error { get; set; }
}
