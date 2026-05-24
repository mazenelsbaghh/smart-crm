using Shared.Domain;
using System;
using System.Collections.Generic;

namespace Modules.Brain.Domain
{
    public class KnowledgeDocument : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
        public int Version { get; set; } = 1;
        public string Status { get; set; } = "Draft"; // Draft, Published, Archived

        public List<KnowledgeChunk> Chunks { get; set; } = new();
    }
}
