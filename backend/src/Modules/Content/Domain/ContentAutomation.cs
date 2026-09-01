using Shared.Domain;

namespace Modules.Content.Domain;

public enum ContentPostStatus
{
    Generating = 0,
    AwaitingApproval = 1,
    Approved = 2,
    Publishing = 3,
    Published = 4,
    GenerationFailed = 5,
    PublishFailed = 6,
    Rejected = 7,
    PublishUnknown = 8
}

public enum ContentWeekPlanStatus
{
    Generating = 0,
    AwaitingApproval = 1,
    Approved = 2,
    Completed = 3,
    Rejected = 4,
    GenerationFailed = 5
}

public sealed class ContentAutomationSettings : AuditableEntity, ITenantEntity
{
    public const string DefaultStylePrompt =
        "هوية وكالة إبداعية حديثة: تصميم تحريري جريء، عنصر بصري نحتي أو ثلاثي الأبعاد، أشكال هندسية كبيرة، وتباين أسود وأبيض مع ألوان البراند كلون إبراز. الخلفيات تتنوع بين الفاتح والغامق من دون إطار أو موكاب أو شكل تقني نيون.";

    public Guid ProjectId { get; set; }
    public string? FacebookPageId { get; set; }
    public string? FacebookPageName { get; set; }
    public bool IsEnabled { get; set; }
    public bool HasApprovedStyle { get; set; }
    public TimeSpan DailyPublishTimeLocal { get; set; } = new(10, 0, 0);
    public string Timezone { get; set; } = "Africa/Cairo";
    public DateTime? NextPublishAtUtc { get; set; }
    public DateTime? LastPublishedAtUtc { get; set; }
    public string? LastError { get; set; }
    public string? LogoObjectKey { get; set; }
    public string? LogoMimeType { get; set; }
    public string? LogoFileName { get; set; }
    public string BrandColorsJson { get; set; } = "[]";
    public string StylePrompt { get; set; } = DefaultStylePrompt;
    public Guid? ApprovedSamplePostId { get; set; }
}

public sealed class ContentPost : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid SettingsId { get; set; }
    public ContentPostStatus Status { get; set; } = ContentPostStatus.Generating;
    public bool IsStyleSample { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string VisualHeadline { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string ImagePrompt { get; set; } = string.Empty;
    public string BrandLogoObjectKey { get; set; } = string.Empty;
    public string BrandStylePrompt { get; set; } = string.Empty;
    public string? ImageObjectKey { get; set; }
    public string ImageMimeType { get; set; } = "image/png";
    public string ImageModel { get; set; } = "gemini-3-pro-image";
    public string ImageSize { get; set; } = "4K";
    public int KnowledgeDocumentCount { get; set; }
    public DateTime? ScheduledForUtc { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? FacebookPostId { get; set; }
    public string? Error { get; set; }
}

public sealed class ContentWeekPlan : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public ContentWeekPlanStatus Status { get; set; } = ContentWeekPlanStatus.Generating;
    public DateOnly StartDateLocal { get; set; }
    public TimeSpan DailyPublishTimeLocal { get; set; }
    public string Timezone { get; set; } = "Africa/Cairo";
    public string BrandLogoObjectKey { get; set; } = string.Empty;
    public string BrandStylePrompt { get; set; } = string.Empty;
    public int KnowledgeDocumentCount { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Error { get; set; }
}

public sealed class ContentWeekPlanItem : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid PlanId { get; set; }
    public int DayIndex { get; set; }
    public DateTime ScheduledForUtc { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string VisualHeadline { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string ImagePrompt { get; set; } = string.Empty;
    public Guid? ContentPostId { get; set; }
}

internal enum ContentVisualDirection
{
    DarkEditorial,
    LightEditorial,
    DarkConceptual,
    LightConceptual
}
