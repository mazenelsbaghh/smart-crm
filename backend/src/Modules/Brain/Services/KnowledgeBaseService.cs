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
        Task<KnowledgeDocument> SuggestDocumentAsync(Guid projectId, string title, string content, string? sourceUrl);
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

            doc.Status = "Approved";
            await _dbContext.SaveChangesAsync();
            return doc;
        }

        public async Task<KnowledgeDocument> RejectDocumentAsync(Guid id)
        {
            var doc = await _dbContext.KnowledgeDocuments.FindAsync(id);
            if (doc == null) throw new ArgumentException("Document not found");

            doc.Status = "Rejected";
            await _dbContext.SaveChangesAsync();
            return doc;
        }

        public async Task<KnowledgeDocument> SuggestDocumentAsync(Guid projectId, string title, string content, string? sourceUrl)
        {
            var doc = new KnowledgeDocument
            {
                ProjectId = projectId,
                Title = title,
                Content = content,
                SourceUrl = sourceUrl,
                Status = "PendingApproval",
                Version = 1
            };

            _dbContext.KnowledgeDocuments.Add(doc);
            await _dbContext.SaveChangesAsync();

            // Generate chunks and embeddings
            await GenerateChunksAndEmbeddingsAsync(doc);

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

            var chunks = new System.Collections.Generic.List<string>();
            bool isQaFormat = doc.Content.Contains("س:") && doc.Content.Contains("ج:");

            if (isQaFormat)
            {
                var lines = doc.Content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var currentBlock = new System.Text.StringBuilder();
                var blocks = new System.Collections.Generic.List<string>();

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("س:") || trimmedLine.StartsWith("س "))
                    {
                        if (currentBlock.Length > 0)
                        {
                            blocks.Add(currentBlock.ToString().Trim());
                            currentBlock.Clear();
                        }
                    }
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        currentBlock.AppendLine(trimmedLine);
                    }
                }
                if (currentBlock.Length > 0)
                {
                    blocks.Add(currentBlock.ToString().Trim());
                }

                var currentChunk = new System.Text.StringBuilder();
                foreach (var block in blocks)
                {
                    if (currentChunk.Length + block.Length > 800 && currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                    }
                    if (currentChunk.Length > 0)
                    {
                        currentChunk.AppendLine();
                        currentChunk.AppendLine();
                    }
                    currentChunk.Append(block);
                }
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                }
            }
            else
            {
                var paragraphs = doc.Content.Split(new[] { "\r\n\r\n", "\n\n", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var currentChunk = new System.Text.StringBuilder();

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

            await _dbContext.SaveChangesAsync();
        }
    }
}
