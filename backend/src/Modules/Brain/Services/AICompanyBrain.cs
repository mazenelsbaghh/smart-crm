using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Modules.AI.Services;
using Modules.Brain.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

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
        // Match the complete, untouched factory payload; similarly named or edited documents belong to the user.
        private static readonly Expression<Func<KnowledgeDocument, bool>> LegacyFactoryDocument = document =>
            document.Status == "Published" && document.Version == 1 &&
            (document.Title == "Pricing Policy 2026"
                && document.Content == "Our default subscription is $49/month with a 10% discount for annual payments."
                && document.SourceUrl == "https://example.com/pricing"
            || document.Title == "Shipping Policy"
                && document.Content == "We offer free shipping on all orders over $50. Standard shipping takes 3-5 business days."
                && document.SourceUrl == "https://example.com/shipping"
            || document.Title == "Refund Policy"
                && document.Content == "Refunds are processed within 5-7 business days of request. Items must be returned in original condition."
                && document.SourceUrl == "https://example.com/refunds");

        private readonly AppDbContext _dbContext;
        private readonly IGeminiClient _geminiClient;

        public AICompanyBrain(AppDbContext dbContext, IGeminiClient geminiClient)
        {
            _dbContext = dbContext;
            _geminiClient = geminiClient;
        }

        private async Task GenerateChunksAsync(KnowledgeDocument doc)
        {
            var oldChunks = await _dbContext.KnowledgeChunks
                .Where(c => c.KnowledgeDocumentId == doc.Id)
                .ToListAsync();
            _dbContext.KnowledgeChunks.RemoveRange(oldChunks);

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
                var chunk = new KnowledgeChunk
                {
                    KnowledgeDocumentId = doc.Id,
                    ChunkText = chunkText
                };

                _dbContext.KnowledgeChunks.Add(chunk);
            }
        }

        public async Task SyncBrainAsync(Guid projectId)
        {
            await QuarantineFactoryDocumentsAsync(projectId);
            var documents = await _dbContext.KnowledgeDocuments
                .Where(d => d.ProjectId == projectId)
                .ToListAsync();
            foreach (var document in documents)
            {
                var hasChunks = await _dbContext.KnowledgeChunks
                    .AnyAsync(chunk => chunk.KnowledgeDocumentId == document.Id);
                if (!hasChunks) await GenerateChunksAsync(document);
            }
            await _dbContext.SaveChangesAsync();
        }

        private async Task QuarantineFactoryDocumentsAsync(Guid projectId)
        {
            var factoryDocuments = await _dbContext.KnowledgeDocuments
                .Where(document => document.ProjectId == projectId)
                .Where(LegacyFactoryDocument).ToListAsync();
            var knowledge = new KnowledgeBaseService(_dbContext, _geminiClient);
            foreach (var document in factoryDocuments)
                await knowledge.RejectDocumentAsync(document.Id);
        }

        internal static IQueryable<KnowledgeDocument> ApprovedDocuments(AppDbContext dbContext, Guid projectId)
        {
            var factoryDocumentIds = dbContext.KnowledgeDocuments
                .Where(document => document.ProjectId == projectId)
                .Where(LegacyFactoryDocument).Select(document => document.Id);
            return dbContext.KnowledgeDocuments.Where(document => document.ProjectId == projectId
                && (document.Status == "Published" || document.Status == "Approved")
                && !factoryDocumentIds.Contains(document.Id));
        }

        public async Task<List<KnowledgeChunkSearchDto>> SearchBrainAsync(Guid projectId, string query, int limit = 3)
        {
            var approvedDocumentIds = ApprovedDocuments(_dbContext, projectId).Select(document => document.Id);
            var chunks = await _dbContext.KnowledgeChunks
                .Where(chunk => approvedDocumentIds.Contains(chunk.KnowledgeDocumentId))
                .Select(c => new KnowledgeChunkSearchDto
                {
                    ChunkId = c.Id,
                    DocumentId = c.KnowledgeDocumentId,
                    ChunkText = c.ChunkText,
                    SimilarityScore = 0
                })
                .ToListAsync();

            var terms = (query ?? string.Empty)
                .ToLowerInvariant()
                .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', '،', '?', '؟', '!', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(term => term.Length > 2)
                .Distinct()
                .ToArray();

            return chunks
                .Select(chunk =>
                {
                    var text = chunk.ChunkText.ToLowerInvariant();
                    chunk.SimilarityScore = terms.Length == 0 ? 0 : terms.Count(text.Contains);
                    return chunk;
                })
                .OrderByDescending(chunk => chunk.SimilarityScore)
                .ThenBy(chunk => chunk.ChunkText.Length)
                .Take(limit)
                .ToList();
        }
    }
}
