using Shared.Domain;

namespace Modules.Content.Domain;

public static class ContentCardGameDesignStatus
{
    public const string Queued = "Queued";
    public const string Generating = "Generating";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
}

public sealed class ContentCardGame : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Brief { get; set; } = string.Empty;
    public string Mechanic { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public int CardCount { get; set; }
    public string BrandLogoObjectKey { get; set; } = string.Empty;
    public string BrandColorsJson { get; set; } = "[]";
    public string BrandStylePrompt { get; set; } = string.Empty;
    public string PlannerModel { get; set; } = string.Empty;
    public string DesignStatus { get; set; } = ContentCardGameDesignStatus.Queued;
    public string? DesignError { get; set; }
    public string? BackImageObjectKey { get; set; }
    public string? BackImageMimeType { get; set; }
    public string? ImageModel { get; set; }
}

public sealed class ContentGameCard : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid GameId { get; set; }
    public int CardIndex { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public string? ImageObjectKey { get; set; }
    public string? ImageMimeType { get; set; }
    public string? ImageError { get; set; }
}
