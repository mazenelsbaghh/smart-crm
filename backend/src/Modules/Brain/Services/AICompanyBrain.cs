using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Modules.AI.Services;
using Modules.Brain.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Modules.Brain.Services
{
    public interface IAICompanyBrain
    {
        Task SyncBrainAsync(Guid projectId);
        Task<List<KnowledgeChunkSearchDto>> SearchBrainAsync(Guid projectId, string query, int limit = 3);
    }

    public class KnowledgeChunkSearchDto
    {
        public Guid ChunkId { get; set; }
        public Guid DocumentId { get; set; }
        public string ChunkText { get; set; } = string.Empty;
        public double SimilarityScore { get; set; }
    }

    public class AICompanyBrain : IAICompanyBrain
    {
        private readonly AppDbContext _dbContext;
        private readonly IGeminiClient _geminiClient;

        public AICompanyBrain(AppDbContext dbContext, IGeminiClient geminiClient)
        {
            _dbContext = dbContext;
            _geminiClient = geminiClient;
        }

        private async Task GenerateChunksAndEmbeddingsAsync(KnowledgeDocument doc)
        {
            var oldChunks = await _dbContext.KnowledgeChunks
                .Where(c => c.KnowledgeDocumentId == doc.Id)
                .ToListAsync();
            _dbContext.KnowledgeChunks.RemoveRange(oldChunks);
            await _dbContext.SaveChangesAsync();

            var paragraphs = doc.Content.Split(new[] { "\r\n\r\n", "\n\n", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var currentChunk = new System.Text.StringBuilder();
            var chunks = new List<string>();

            foreach (var p in paragraphs)
            {
                var clean = p.Trim();
                if (string.IsNullOrEmpty(clean)) continue;

                if (currentChunk.Length + clean.Length > 800 && currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }
                currentChunk.AppendLine(clean);
            }
            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }

            foreach (var chunkText in chunks)
            {
                var embeddingFloats = await _geminiClient.GenerateEmbeddingAsync(chunkText);
                var embeddingVector = new Vector(embeddingFloats);

                var chunk = new KnowledgeChunk
                {
                    KnowledgeDocumentId = doc.Id,
                    ChunkText = chunkText,
                    Embedding = embeddingVector
                };

                _dbContext.KnowledgeChunks.Add(chunk);
            }
        }

        public async Task SyncBrainAsync(Guid projectId)
        {
            // Check if there are already any documents for this project
            var hasDocs = await _dbContext.KnowledgeDocuments
                .AnyAsync(d => d.ProjectId == projectId);

            if (!hasDocs)
            {
                // Only seed default templates if the project has no documents
                var syncPayloads = new[]
                {
                    new { Title = "Pricing Policy 2026", Content = "Our default subscription is $49/month with a 10% discount for annual payments.", Source = "https://example.com/pricing" },
                    new { Title = "Shipping Policy", Content = "We offer free shipping on all orders over $50. Standard shipping takes 3-5 business days.", Source = "https://example.com/shipping" },
                    new { Title = "Refund Policy", Content = "Refunds are processed within 5-7 business days of request. Items must be returned in original condition.", Source = "https://example.com/refunds" }
                };

                foreach (var payload in syncPayloads)
                {
                    var doc = new KnowledgeDocument
                    {
                        ProjectId = projectId,
                        Title = payload.Title,
                        Content = payload.Content,
                        SourceUrl = payload.Source,
                        Status = "Published", // Auto-publish synced records
                        Version = 1
                    };

                    _dbContext.KnowledgeDocuments.Add(doc);
                    await _dbContext.SaveChangesAsync(); // Generates doc.Id

                    // Split content into chunks
                    await GenerateChunksAndEmbeddingsAsync(doc);
                }
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                // If there are existing documents, make sure their chunks and embeddings are up-to-date
                var documents = await _dbContext.KnowledgeDocuments
                    .Where(d => d.ProjectId == projectId)
                    .ToListAsync();

                foreach (var doc in documents)
                {
                    // Check if this document already has chunks. If not, generate them.
                    var hasChunks = await _dbContext.KnowledgeChunks
                        .AnyAsync(c => c.KnowledgeDocumentId == doc.Id);

                    if (!hasChunks)
                    {
                        await GenerateChunksAndEmbeddingsAsync(doc);
                    }
                }
                await _dbContext.SaveChangesAsync();
            }
        }

        public async Task<List<KnowledgeChunkSearchDto>> SearchBrainAsync(Guid projectId, string query, int limit = 3)
        {
            var queryEmbeddingFloats = await _geminiClient.GenerateEmbeddingAsync(query);
            var queryVector = new Vector(queryEmbeddingFloats);

            // Npgsql pgvector CosineDistance query
            var results = await _dbContext.KnowledgeChunks
                .Include(c => c.KnowledgeDocument)
                .Where(c => c.KnowledgeDocument!.ProjectId == projectId && 
                        (c.KnowledgeDocument.Status == "Published" || c.KnowledgeDocument.Status == "Approved"))
                .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
                .Take(limit)
                .Select(c => new KnowledgeChunkSearchDto
                {
                    ChunkId = c.Id,
                    DocumentId = c.KnowledgeDocumentId,
                    ChunkText = c.ChunkText,
                    SimilarityScore = 1.0 - (double)c.Embedding.CosineDistance(queryVector)
                })
                .ToListAsync();

            return results;
        }
    }
}
