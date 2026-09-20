using Shared.Domain;

namespace Modules.AI.Domain;

public sealed class ReplyLesson : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class ReplyLessonEvidence : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid LessonId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
}
