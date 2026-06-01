using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Modules.AI.Services;
using Modules.Brain.Domain;
using System;
using System.Linq;
using System.Threading.Tasks;
using Pgvector;

namespace Modules.Brain.Services
{
    public interface IKnowledgeBaseService
    {
        Task<KnowledgeDocument> CreateDocumentAsync(Guid projectId, string title, string content, string? sourceUrl);
        Task<KnowledgeDocument> UpdateDocumentAsync(Guid id, string title, string content, string? sourceUrl);
        Task<KnowledgeDocument> ApproveDocumentAsync(Guid id);
        Task<KnowledgeDocument> RejectDocumentAsync(Guid id);
        Task DeleteDocumentAsync(Guid id);
    }

    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly AppDbContext _dbContext;
        private readonly IGeminiClient _geminiClient;

        public KnowledgeBaseService(AppDbContext dbContext, IGeminiClient geminiClient)
        {
            _dbContext = dbContext;
            _geminiClient = geminiClient;
        }

        public async Task<KnowledgeDocument> CreateDocumentAsync(Guid projectId, string title, string content, string? sourceUrl)
        {
            var doc = new KnowledgeDocument
            {
                ProjectId = projectId,
                Title = title,
                Content = content,
                SourceUrl = sourceUrl,
                Status = "Draft", // Default to Draft
                Version = 1
            };

            _dbContext.KnowledgeDocuments.Add(doc);
            await _dbContext.SaveChangesAsync();

            // Generate chunks and embeddings
            await GenerateChunksAndEmbeddingsAsync(doc);

            return doc;
        }

        public async Task<KnowledgeDocument> UpdateDocumentAsync(Guid id, string title, string content, string? sourceUrl)
        {
            var doc = await _dbContext.KnowledgeDocuments.FindAsync(id);
            if (doc == null) throw new ArgumentException("Document not found");

            doc.Title = title;
            doc.Content = content;
            doc.SourceUrl = sourceUrl;
            doc.Version += 1;
            doc.Status = "Draft"; // Set back to draft so user publishes it again

            await _dbContext.SaveChangesAsync();

            // Generate chunks and embeddings
            await GenerateChunksAndEmbeddingsAsync(doc);

            return doc;
        }

        public async Task<KnowledgeDocument> ApproveDocumentAsync(Guid id)
        {
            var doc = await _dbContext.KnowledgeDocuments.FindAsync(id);
            if (doc == null) throw new ArgumentException("Document not found");

            doc.Status = "Published";
            await _dbContext.SaveChangesAsync();
            return doc;
        }

        public async Task<KnowledgeDocument> RejectDocumentAsync(Guid id)
        {
            var doc = await _dbContext.KnowledgeDocuments.FindAsync(id);
            if (doc == null) throw new ArgumentException("Document not found");

            doc.Status = "Draft";
            await _dbContext.SaveChangesAsync();
            return doc;
        }

        public async Task DeleteDocumentAsync(Guid id)
        {
            var doc = await _dbContext.KnowledgeDocuments
                .Include(d => d.Chunks)
                .FirstOrDefaultAsync(d => d.Id == id);
            
            if (doc == null) throw new ArgumentException("Document not found");

            _dbContext.KnowledgeChunks.RemoveRange(doc.Chunks);
            _dbContext.KnowledgeDocuments.Remove(doc);
            await _dbContext.SaveChangesAsync();
        }

        private async Task GenerateChunksAndEmbeddingsAsync(KnowledgeDocument doc)
        {
            var oldChunks = await _dbContext.KnowledgeChunks
                .Where(c => c.KnowledgeDocumentId == doc.Id)
                .ToListAsync();
            _dbContext.KnowledgeChunks.RemoveRange(oldChunks);
            await _dbContext.SaveChangesAsync();

            var chunksText = doc.Content.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var chunkText in chunksText)
            {
                var cleanText = chunkText.Trim();
                if (!cleanText.EndsWith(".") && !string.IsNullOrEmpty(cleanText))
                {
                    cleanText += ".";
                }

                if (string.IsNullOrEmpty(cleanText)) continue;

                var embeddingFloats = await _geminiClient.GenerateEmbeddingAsync(cleanText);
                var embeddingVector = new Vector(embeddingFloats);

                var chunk = new KnowledgeChunk
                {
                    KnowledgeDocumentId = doc.Id,
                    ChunkText = cleanText,
                    Embedding = embeddingVector
                };

                _dbContext.KnowledgeChunks.Add(chunk);
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}
