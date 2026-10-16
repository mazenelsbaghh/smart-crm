using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Brain.Domain;
using Modules.Brain.Services;
using Modules.Conversations.Domain;
using Modules.Customers.Domain;
using Modules.Customers.Services;
using Modules.Projects.Domain;
using Pgvector;
using Shared.Queue;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CustomerKnowledgeAiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private IsolatedPostgresDatabase database = null!;
    private readonly TenantContext tenant = new();
    private readonly ProjectSecretVault vault = new(new EphemeralDataProtectionProvider());

    public async Task InitializeAsync()
    {
        database = await postgres.CreateIsolatedDatabaseAsync();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = database.CreateContext(tenant);
        await db.Database.MigrateAsync();
        db.Projects.Add(new Project { Id = tenant.ProjectId, Name = "AI regression" });
        db.ProjectSettings.Add(new ProjectSettings { ProjectId = tenant.ProjectId,
            GeminiApiKey = vault.Protect(tenant.ProjectId, "test-provider-key") });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => database.DisposeAsync().AsTask();

    [Fact]
    public async Task Synchronizing_empty_knowledge_never_creates_approved_commercial_facts()
    {
        await using var db = database.CreateContext(tenant);
        using var http = new HttpClient(new GeminiResponse("{}"));

        await new AICompanyBrain(db, Client(http)).SyncBrainAsync(tenant.ProjectId);

        Assert.Empty(await db.KnowledgeDocuments.ToListAsync());
        Assert.Empty(await db.KnowledgeChunks.ToListAsync());
    }

    [Fact]
    public async Task Synchronizing_existing_knowledge_indexes_the_real_document_without_publishing_drafts()
    {
        await using var db = database.CreateContext(tenant);
        var document = new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = "مواعيد العمل",
            Content = "مواعيد العمل من التاسعة صباحاً حتى الخامسة مساءً", Status = "Draft" };
        db.KnowledgeDocuments.Add(document);
        await db.SaveChangesAsync();
        using var http = new HttpClient(new GeminiResponse("{}"));
        var brain = new AICompanyBrain(db, Client(http));

        await brain.SyncBrainAsync(tenant.ProjectId);
        await brain.SyncBrainAsync(tenant.ProjectId);

        Assert.Equal(document.Content, (await db.KnowledgeChunks.SingleAsync()).ChunkText);
        Assert.Equal("Draft", (await db.KnowledgeDocuments.SingleAsync()).Status);
        Assert.Empty(await brain.SearchBrainAsync(tenant.ProjectId, "مواعيد"));
    }

    [Theory]
    [InlineData("Pricing Policy 2026", "Our default subscription is $49/month with a 10% discount for annual payments.", "https://example.com/pricing")]
    [InlineData("Shipping Policy", "We offer free shipping on all orders over $50. Standard shipping takes 3-5 business days.", "https://example.com/shipping")]
    [InlineData("Refund Policy", "Refunds are processed within 5-7 business days of request. Items must be returned in original condition.", "https://example.com/refunds")]
    public async Task Untouched_factory_facts_are_hidden_before_sync_and_quarantined_without_deletion(string title, string content, string source)
    {
        await using var db = database.CreateContext(tenant);
        var document = new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = title, Content = content,
            SourceUrl = source, Status = "Published", Version = 1 };
        db.AddRange(document, Chunk(document));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new GeminiResponse("{}"));
        var brain = new AICompanyBrain(db, Client(http));

        Assert.Empty(await brain.SearchBrainAsync(tenant.ProjectId, title));
        await brain.SyncBrainAsync(tenant.ProjectId);

        db.ChangeTracker.Clear();
        var quarantined = await db.KnowledgeDocuments.SingleAsync();
        Assert.Equal("Rejected", quarantined.Status);
        Assert.Equal(2, quarantined.Version);
        Assert.Equal(content, quarantined.Content);
        Assert.Equal(content, (await db.KnowledgeChunks.SingleAsync()).ChunkText);
        var publication = await db.IntegrationOutboxMessages.SingleAsync(row => row.EventType == "KnowledgePublishedChanged.v2");
        var revocation = JsonSerializer.Deserialize<AdvertisingKnowledgeChanged>(publication.PayloadJson)!;
        Assert.Equal(document.Id, revocation.KnowledgeDocumentId);
        Assert.Equal("Rejected", revocation.State);
        Assert.Equal(2, revocation.SourceVersion);
    }

    [Fact]
    public async Task Quarantine_preserves_edited_documents_other_projects_and_repeated_sync_is_idempotent()
    {
        const string original = "Our default subscription is $49/month with a 10% discount for annual payments.";
        await using var db = database.CreateContext(tenant);
        var factory = new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = "Pricing Policy 2026",
            Content = original, SourceUrl = "https://example.com/pricing", Status = "Published" };
        var preserved = new[]
        {
            new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = factory.Title, Content = "Our actual subscription costs 200 EGP.", SourceUrl = factory.SourceUrl, Status = "Published" },
            new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = "Owner policy", Content = original, SourceUrl = factory.SourceUrl, Status = "Published" },
            new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = factory.Title, Content = original, SourceUrl = "https://company.test/pricing", Status = "Published" },
            new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = factory.Title, Content = original, SourceUrl = factory.SourceUrl, Status = "Published", Version = 2 },
            new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = factory.Title, Content = original, SourceUrl = factory.SourceUrl, Status = "Approved" }
        };
        var other = new KnowledgeDocument { ProjectId = Guid.NewGuid(), Title = factory.Title, Content = original,
            SourceUrl = factory.SourceUrl, Status = "Published" };
        foreach (var document in preserved.Append(factory).Append(other)) db.AddRange(document, Chunk(document));
        await db.SaveChangesAsync();
        using var http = new HttpClient(new GeminiResponse("{}"));
        var brain = new AICompanyBrain(db, Client(http));
        var visible = await brain.SearchBrainAsync(tenant.ProjectId, "subscription", 20);
        Assert.Equal(preserved.Select(document => document.Id).Order().ToArray(), visible.Select(chunk => chunk.DocumentId).Order().ToArray());

        await brain.SyncBrainAsync(tenant.ProjectId);
        await brain.SyncBrainAsync(tenant.ProjectId);

        db.ChangeTracker.Clear();
        Assert.Equal("Rejected", (await db.KnowledgeDocuments.SingleAsync(document => document.Id == factory.Id)).Status);
        foreach (var originalDocument in preserved.Append(other))
        {
            var unchanged = await db.KnowledgeDocuments.IgnoreQueryFilters().SingleAsync(document => document.Id == originalDocument.Id);
            Assert.Equal(originalDocument.Status, unchanged.Status);
            Assert.Equal(originalDocument.Version, unchanged.Version);
            Assert.Equal(originalDocument.Content, unchanged.Content);
        }
        Assert.Single(await db.IntegrationOutboxMessages.Where(row => row.EventType == "KnowledgePublishedChanged.v2").ToListAsync());
        Assert.Equal(preserved.Length + 2, await db.KnowledgeChunks.CountAsync());
    }

    private static KnowledgeChunk Chunk(KnowledgeDocument document) => new()
    {
        KnowledgeDocumentId = document.Id, ChunkText = document.Content,
        Embedding = new Vector(Enumerable.Repeat(.1f, 768).ToArray())
    };

    [Fact]
    public async Task Closing_an_arabic_conversation_persists_evidenced_memory_from_the_provider()
    {
        var (customerId, conversationId) = await SeedConversationAsync();
        var extraction = JsonSerializer.Serialize(new { facts = new[] { "يفضل الدراسة أونلاين يوم الجمعة" },
            triggers = Array.Empty<string>(), objections = Array.Empty<string>(), summary = "العميل يسافر كثيراً ويريد الدراسة يوم الجمعة." });
        using var http = new HttpClient(new GeminiResponse(extraction));
        await using (var db = database.CreateContext(tenant))
            await new CustomerMemoryService(db, Client(http), vault)
                .UpdateMemoryFromConversationAsync(tenant.ProjectId, customerId, conversationId);

        await using var verification = database.CreateContext(tenant);
        var memory = await verification.CustomerMemories.SingleAsync();
        Assert.Equal(customerId, memory.CustomerId);
        Assert.Equal(new[] { "يفضل الدراسة أونلاين يوم الجمعة" }, JsonSerializer.Deserialize<string[]>(memory.FactsJson));
        Assert.Equal("العميل يسافر كثيراً ويريد الدراسة يوم الجمعة.", memory.LongTermSummary);
    }

    [Theory]
    [InlineData("[AI_ERROR] unavailable")]
    [InlineData("{\"summary\":\"\",\"facts\":[]}")]
    [InlineData("{\"summary\":123}")]
    public async Task Unusable_memory_generation_preserves_previous_memory_for_retry(string response)
    {
        var (customerId, conversationId) = await SeedConversationAsync();
        await using (var seed = database.CreateContext(tenant))
        {
            seed.CustomerMemories.Add(new CustomerMemory { ProjectId = tenant.ProjectId, CustomerId = customerId,
                FactsJson = "[\"verified preference\"]", LongTermSummary = "Verified previous summary" });
            await seed.SaveChangesAsync();
        }
        using var http = new HttpClient(new GeminiResponse(response));
        await using (var db = database.CreateContext(tenant))
            await Assert.ThrowsAsync<DeferredAiReplyException>(() => new CustomerMemoryService(db, Client(http), vault)
                .UpdateMemoryFromConversationAsync(tenant.ProjectId, customerId, conversationId));

        await using var verification = database.CreateContext(tenant);
        var memory = await verification.CustomerMemories.SingleAsync();
        Assert.Equal("Verified previous summary", memory.LongTermSummary);
        Assert.Equal("[\"verified preference\"]", memory.FactsJson);
    }

    private async Task<(Guid CustomerId, Guid ConversationId)> SeedConversationAsync()
    {
        await using var db = database.CreateContext(tenant);
        var customer = new Customer { ProjectId = tenant.ProjectId, PhoneNumber = "201011112222", Name = "أحمد", City = "القاهرة" };
        var conversation = new Conversation { ProjectId = tenant.ProjectId, CustomerId = customer.Id, Status = "Closed" };
        db.AddRange(customer, conversation, new Message { ConversationId = conversation.Id, Direction = "Incoming",
            Content = "أنا بسافر كتير ومتاح لي الدراسة أونلاين يوم الجمعة بس", MessageType = "Text", ExternalMessageId = Guid.NewGuid().ToString() });
        await db.SaveChangesAsync();
        return (customer.Id, conversation.Id);
    }

    private static GeminiClient Client(HttpClient http) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Gemini:ApiKey"] = "test-provider-key" }).Build(),
        new GeminiMockHandler(new HostingEnvironment { EnvironmentName = "Production" }),
        NullLogger<GeminiClient>.Instance, http);

    private sealed class GeminiResponse(string text) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = request.RequestUri!.AbsolutePath.Contains("embedContent")
                ? JsonSerializer.Serialize(new { embedding = new { values = Enumerable.Repeat(.1f, 768).ToArray() } })
                : JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text } } } } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
        }
    }
}
