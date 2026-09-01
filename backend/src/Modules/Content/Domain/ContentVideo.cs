using Shared.Domain;

namespace Modules.Content.Domain;

public enum ContentVideoStatus
{
    Planning = 0,
    AwaitingApproval = 1,
    Generating = 2,
    Assembling = 3,
    Ready = 4,
    PlanningFailed = 5,
    GenerationFailed = 6,
    AssemblyFailed = 7
}

public enum ContentVideoSceneStatus
{
    Planned = 0,
    Queued = 1,
    Submitted = 2,
    Generating = 3,
    Completed = 4,
    Failed = 5,
    Submitting = 6,
    SubmissionUncertain = 7,
    RecoveryRequired = 8
}

public sealed class ContentVideo : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public ContentVideoStatus Status { get; set; } = ContentVideoStatus.Planning;
    public string? Brief { get; set; }
    public string IdeaTitle { get; set; } = string.Empty;
    public string Hook { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "9:16";
    public string Resolution { get; set; } = "720p";
    public int RequestedSceneCount { get; set; } = 4;
    public int RequestedSceneDurationSeconds { get; set; } = 6;
    public int KnowledgeDocumentCount { get; set; }
    public bool KnowledgeWasTruncated { get; set; }
    public string KnowledgeSnapshotHash { get; set; } = string.Empty;
    public string PlannerModel { get; set; } = string.Empty;
    public string VideoModel { get; set; } = "gemini-omni-1.1-flash-preview";
    public string? FinalVideoObjectKey { get; set; }
    public string FinalVideoMimeType { get; set; } = "video/mp4";
    public string? Error { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public List<ContentVideoScene> Scenes { get; set; } = [];
}

public sealed class ContentVideoScene : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ContentVideoId { get; set; }
    public int SceneIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Narrative { get; set; } = string.Empty;
    public string VisualPrompt { get; set; } = string.Empty;
    public string AudioPrompt { get; set; } = string.Empty;
    public string TransitionPrompt { get; set; } = string.Empty;
    public int DurationSeconds { get; set; } = 6;
    public ContentVideoSceneStatus Status { get; set; } = ContentVideoSceneStatus.Planned;
    public string? ProviderInteractionId { get; set; }
    public string? ProviderProjectId { get; set; }
    public Guid? SubmissionClaimToken { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? GenerationStartedAtUtc { get; set; }
    public int TransientRetryCount { get; set; }
    public DateTime? ProviderSubmittedAtUtc { get; set; }
    public DateTime? ProviderPolledAtUtc { get; set; }
    public string? VideoObjectKey { get; set; }
    public string VideoMimeType { get; set; } = "video/mp4";
    public string? Error { get; set; }
    public ContentVideo? Video { get; set; }
}
