using Shared.Domain;
using System;
using Pgvector;

namespace Modules.Brain.Domain
{
    public class KnowledgeChunk : Entity
    {
        public Guid KnowledgeDocumentId { get; set; }
        
        [System.Text.Json.Serialization.JsonIgnore]
        public KnowledgeDocument? KnowledgeDocument { get; set; }
        
        public string ChunkText { get; set; } = string.Empty;
        
        public Vector? Embedding { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
