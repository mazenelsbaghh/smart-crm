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

        public async Task SyncBrainAsync(Guid projectId)
        {
            // Clear existing knowledge documents for this project to simulate clean sync
            var oldDocs = await _dbContext.KnowledgeDocuments
                .Where(d => d.ProjectId == projectId)
                .ToListAsync();
            _dbContext.KnowledgeDocuments.RemoveRange(oldDocs);
            await _dbContext.SaveChangesAsync();

            // Mock synchronized external data
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

                // Split content into chunks (for simplicity, we create one chunk per sentence or paragraph)
                var chunks = payload.Content.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var chunkText in chunks)
                {
                    var cleanText = chunkText.Trim();
                    if (!cleanText.EndsWith(".") && !string.IsNullOrEmpty(cleanText))
                    {
                        cleanText += ".";
                    }

                    if (string.IsNullOrEmpty(cleanText)) continue;

                    // Generate embeddings via Gemini
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
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task<List<KnowledgeChunkSearchDto>> SearchBrainAsync(Guid projectId, string query, int limit = 3)
        {
            var queryEmbeddingFloats = await _geminiClient.GenerateEmbeddingAsync(query);
            var queryVector = new Vector(queryEmbeddingFloats);

            // Npgsql pgvector CosineDistance query
            var results = await _dbContext.KnowledgeChunks
                .Include(c => c.KnowledgeDocument)
                .Where(c => c.KnowledgeDocument!.ProjectId == projectId && c.KnowledgeDocument.Status == "Published")
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
