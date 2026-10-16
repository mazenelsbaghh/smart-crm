using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Brain.Domain;
using Modules.Brain.Services;
using Modules.Projects.Domain;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class KnowledgeIndexingCostTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Creating_editing_and_synchronizing_searchable_knowledge_requires_no_paid_embedding_requests()
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = isolated.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var legacy = new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = "Hours",
            Content = "Opening hours are nine to five", Status = "Published" };
        db.AddRange(new Project { Id = tenant.ProjectId, Name = "Knowledge indexing" }, legacy);
        await db.SaveChangesAsync();
        var provider = new CountProviderRequests();
        using var http = new HttpClient(provider);
        var client = Client(http);
        var knowledge = new KnowledgeBaseService(db, client);
        var brain = new AICompanyBrain(db, client);

        var document = await knowledge.CreateDocumentAsync(tenant.ProjectId, "Course", "Course price is 100 EGP", null);
        await brain.SyncBrainAsync(tenant.ProjectId);
        await brain.SyncBrainAsync(tenant.ProjectId);
        Assert.Single(await brain.SearchBrainAsync(tenant.ProjectId, "hours"));
        await knowledge.ApproveDocumentAsync(document.Id);
        Assert.Contains(await brain.SearchBrainAsync(tenant.ProjectId, "Course"), chunk => chunk.DocumentId == document.Id);

        await knowledge.UpdateDocumentAsync(document.Id, "Course", "Course price is 200 EGP", null);
        Assert.DoesNotContain(await brain.SearchBrainAsync(tenant.ProjectId, "Course"), chunk => chunk.DocumentId == document.Id);
        await knowledge.ApproveDocumentAsync(document.Id);
        Assert.Equal("Course price is 200 EGP", (await brain.SearchBrainAsync(tenant.ProjectId, "Course"))
            .Single(chunk => chunk.DocumentId == document.Id).ChunkText);
        Assert.Equal(2, await db.KnowledgeChunks.CountAsync());
        Assert.All(await db.KnowledgeChunks.ToListAsync(), chunk => Assert.Null(chunk.Embedding));
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task Rejected_replacement_chunk_keeps_previous_document_chunks_and_publication_together()
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = isolated.CreateContext(tenant);
        await db.Database.MigrateAsync();
        db.Projects.Add(new Project { Id = tenant.ProjectId, Name = "Atomic knowledge" });
        await db.SaveChangesAsync();
        using var http = new HttpClient(new CountProviderRequests());
        var knowledge = new KnowledgeBaseService(db, Client(http));
        var document = await knowledge.CreateDocumentAsync(tenant.ProjectId, "Original", "Verified original content", null);
        await knowledge.ApproveDocumentAsync(document.Id);
        var originalVersion = document.Version;
        var originalChunk = await db.KnowledgeChunks.SingleAsync();
        var publications = await db.IntegrationOutboxMessages.CountAsync();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "KnowledgeChunks" ADD CONSTRAINT "RejectKnowledgeTestReplacement"
            CHECK ("ChunkText" <> 'Rejected replacement');
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            knowledge.UpdateDocumentAsync(document.Id, "Changed", "Rejected replacement", null));

        await using var verification = isolated.CreateContext(tenant);
        var saved = await verification.KnowledgeDocuments.SingleAsync();
        Assert.Equal("Original", saved.Title);
        Assert.Equal("Verified original content", saved.Content);
        Assert.Equal("Approved", saved.Status);
        Assert.Equal(originalVersion, saved.Version);
        Assert.Equal(originalChunk.Id, (await verification.KnowledgeChunks.SingleAsync()).Id);
        Assert.Equal(publications, await verification.IntegrationOutboxMessages.CountAsync());
    }

    private static GeminiClient Client(HttpClient http) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Gemini:ApiKey"] = "test-only-key" }).Build(),
        new GeminiMockHandler(new HostingEnvironment { EnvironmentName = "Production" }),
        NullLogger<GeminiClient>.Instance, http);

    private sealed class CountProviderRequests : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
