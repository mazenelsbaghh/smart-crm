using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Modules.Customers.Domain;
using Modules.Customers.Services;
using Modules.Projects.Domain;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CustomerMemoryCostTests(CustomerMemoryCostDatabase database)
    : IClassFixture<CustomerMemoryCostDatabase>
{
    private readonly ProjectSecretVault vault = new(new EphemeralDataProtectionProvider());

    [Theory]
    [InlineData(false, "[AI_ERROR] unavailable")]
    [InlineData(true, "[AI_ERROR] unavailable")]
    [InlineData(false, "{\"facts\":[],\"triggers\":[],\"objections\":[],\"summary\":123}")]
    [InlineData(true, "{\"facts\":[],\"triggers\":[],\"objections\":[],\"summary\":123}")]
    public async Task Unusable_generation_preserves_verified_memory_and_customer(bool manual, string response)
    {
        var scenario = await SeedAsync();
        using var http = new HttpClient(new MemoryProvider(response));
        await using (var db = database.Database.CreateContext(scenario.Tenant))
        {
            var service = new CustomerMemoryService(db, Client(http), vault);
            await Assert.ThrowsAsync<DeferredAiReplyException>(() => manual
                ? service.GenerateCompleteProfileAsync(scenario.Tenant.ProjectId, scenario.CustomerId)
                : service.UpdateMemoryFromConversationAsync(scenario.Tenant.ProjectId, scenario.CustomerId, scenario.ConversationId));
        }
        await using var verification = database.Database.CreateContext(scenario.Tenant);
        var memory = await verification.CustomerMemories.SingleAsync();
        Assert.Equal("Verified previous summary", memory.LongTermSummary);
        Assert.Equal(new[] { "لا يفضل المكالمات" }, JsonSerializer.Deserialize<string[]>(memory.FactsJson));
        Assert.Equal("Customer original", (await verification.Customers.SingleAsync()).Name);
        Assert.Empty(await verification.Deals.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_updates_consolidate_memory_and_use_configured_model_without_duplicating_summary(bool manual)
    {
        var scenario = await SeedAsync();
        const string summary = "يفضل التواصل بالرسائل والدراسة مساء الجمعة.";
        var provider = new MemoryProvider(JsonSerializer.Serialize(new
        {
            facts = new[] { "لا يفضل المكالمات", "يفضل الدراسة مساء الجمعة", "يفضل الدراسة مساء الجمعة" },
            triggers = Array.Empty<string>(), objections = Array.Empty<string>(), summary
        }));
        using var http = new HttpClient(provider);
        await using (var db = database.Database.CreateContext(scenario.Tenant))
        {
            var service = new CustomerMemoryService(db, Client(http), vault);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (manual) await service.GenerateCompleteProfileAsync(scenario.Tenant.ProjectId, scenario.CustomerId);
                else await service.UpdateMemoryFromConversationAsync(scenario.Tenant.ProjectId, scenario.CustomerId, scenario.ConversationId);
            }
        }
        await using var verification = database.Database.CreateContext(scenario.Tenant);
        var memory = await verification.CustomerMemories.SingleAsync();
        Assert.Equal(summary, memory.LongTermSummary);
        Assert.Equal(new[] { "لا يفضل المكالمات", "يفضل الدراسة مساء الجمعة" }, JsonSerializer.Deserialize<string[]>(memory.FactsJson));
        Assert.All(provider.Prompts, prompt => Assert.Contains("لا يفضل المكالمات", prompt));
        Assert.All(provider.Paths, path => Assert.EndsWith("/gemini-2.5-flash-lite:generateContent", path));
    }

    [Fact]
    public async Task Large_history_and_legacy_memory_have_bounded_context_while_retaining_recent_request_and_facts()
    {
        var scenario = await SeedAsync();
        await using (var seed = database.Database.CreateContext(scenario.Tenant))
        {
            var memory = await seed.CustomerMemories.SingleAsync();
            memory.LongTermSummary = string.Concat(Enumerable.Repeat("ملخص سابق مكرر\n", 2000));
            seed.Messages.AddRange(Enumerable.Range(0, 250).Select(index => new Message
            {
                ConversationId = scenario.ConversationId, Direction = "Incoming", MessageType = "Text",
                ExternalMessageId = Guid.NewGuid().ToString(), Content = new string('س', 500),
                Timestamp = DateTime.UtcNow.AddHours(-1).AddSeconds(index)
            }));
            await seed.SaveChangesAsync();
        }
        var provider = new MemoryProvider(JsonSerializer.Serialize(new
        {
            facts = new[] { "لا يفضل المكالمات", "يفضل الدراسة مساء الجمعة" },
            triggers = Array.Empty<string>(), objections = Array.Empty<string>(), summary = "يفضل الرسائل والدراسة مساء الجمعة."
        }));
        using var http = new HttpClient(provider);
        await using (var db = database.Database.CreateContext(scenario.Tenant))
            await new CustomerMemoryService(db, Client(http), vault)
                .UpdateMemoryFromConversationAsync(scenario.Tenant.ProjectId, scenario.CustomerId, scenario.ConversationId);
        var prompt = Assert.Single(provider.Prompts);
        Assert.True(prompt.Length < 40000, $"Unexpected prompt length: {prompt.Length}");
        Assert.Contains("لا يفضل المكالمات", prompt);
        Assert.Contains("يفضل الدراسة مساء الجمعة", prompt);
        await using var verification = database.Database.CreateContext(scenario.Tenant);
        Assert.Equal("يفضل الرسائل والدراسة مساء الجمعة.", (await verification.CustomerMemories.SingleAsync()).LongTermSummary);
    }

    [Fact]
    public async Task Bounded_context_does_not_delete_existing_facts_omitted_by_the_new_extraction()
    {
        var scenario = await SeedAsync();
        var previousFacts = Enumerable.Range(1, 65).Select(index => $"verified fact {index}").ToList();
        previousFacts[40] = "Important confirmed customer constraint";
        previousFacts[0] = new string('ح', 500);
        await using (var seed = database.Database.CreateContext(scenario.Tenant))
        {
            (await seed.CustomerMemories.SingleAsync()).FactsJson = JsonSerializer.Serialize(previousFacts);
            await seed.SaveChangesAsync();
        }
        using var http = new HttpClient(new MemoryProvider(JsonSerializer.Serialize(new
        {
            facts = new[] { "new confirmed preference" }, triggers = Array.Empty<string>(),
            objections = Array.Empty<string>(), summary = "Updated concise summary"
        })));
        await using (var db = database.Database.CreateContext(scenario.Tenant))
            await new CustomerMemoryService(db, Client(http), vault)
                .GenerateCompleteProfileAsync(scenario.Tenant.ProjectId, scenario.CustomerId);
        await using var verification = database.Database.CreateContext(scenario.Tenant);
        var savedFacts = JsonSerializer.Deserialize<string[]>((await verification.CustomerMemories.SingleAsync()).FactsJson)!;
        Assert.Equal(previousFacts.Append("new confirmed preference"), savedFacts);
    }

    [Fact]
    public async Task Concurrent_close_and_manual_profile_refresh_preserve_both_new_memories()
    {
        var scenario = await SeedAsync();
        var firstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new MemoryProvider(JsonSerializer.Serialize(new
        {
            facts = new[] { "verified travel constraint" }, triggers = Array.Empty<string>(),
            objections = Array.Empty<string>(), summary = "Verified travel constraint"
        }), async () => { firstRequest.SetResult(); await releaseFirst.Task; });
        var second = new MemoryProvider(JsonSerializer.Serialize(new
        {
            facts = new[] { "verified evening preference" }, triggers = Array.Empty<string>(),
            objections = Array.Empty<string>(), summary = "Verified travel constraint and evening preference"
        }));
        using var firstHttp = new HttpClient(first);
        using var secondHttp = new HttpClient(second);
        await using var firstDb = database.Database.CreateContext(scenario.Tenant);
        await using var secondDb = database.Database.CreateContext(scenario.Tenant);
        // A caller can already have the old memory tracked before entering the serialized operation.
        _ = await secondDb.CustomerMemories.SingleAsync();
        var close = new CustomerMemoryService(firstDb, Client(firstHttp), vault)
            .UpdateMemoryFromConversationAsync(scenario.Tenant.ProjectId, scenario.CustomerId, scenario.ConversationId);
        await firstRequest.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var refresh = new CustomerMemoryService(secondDb, Client(secondHttp), vault)
            .GenerateCompleteProfileAsync(scenario.Tenant.ProjectId, scenario.CustomerId);
        releaseFirst.SetResult();
        await Task.WhenAll(close, refresh);
        await using var verification = database.Database.CreateContext(scenario.Tenant);
        var memory = await verification.CustomerMemories.SingleAsync();
        Assert.Contains("verified travel constraint", JsonSerializer.Deserialize<string[]>(memory.FactsJson)!);
        Assert.Contains("verified evening preference", JsonSerializer.Deserialize<string[]>(memory.FactsJson)!);
        Assert.Contains("verified travel constraint", Assert.Single(second.Prompts));
        Assert.Equal("Verified travel constraint and evening preference", memory.LongTermSummary);
    }

    private async Task<MemoryScenario> SeedAsync()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = database.Database.CreateContext(tenant);
        var customer = new Customer { ProjectId = tenant.ProjectId, Name = "Customer original", PhoneNumber = Guid.NewGuid().ToString(), City = "Cairo" };
        var conversation = new Conversation { ProjectId = tenant.ProjectId, CustomerId = customer.Id, Status = "Closed" };
        db.AddRange(new Project { Id = tenant.ProjectId, Name = "Memory cost regression" },
            new ProjectSettings { ProjectId = tenant.ProjectId, GeminiModel = "gemini-2.5-flash-lite",
                GeminiApiKey = vault.Protect(tenant.ProjectId, "provider-boundary-test-key") }, customer, conversation,
            new Message { ConversationId = conversation.Id, Direction = "Incoming", MessageType = "Text",
                ExternalMessageId = Guid.NewGuid().ToString(), Content = "يفضل الدراسة مساء الجمعة", Timestamp = DateTime.UtcNow },
            new CustomerMemory { ProjectId = tenant.ProjectId, CustomerId = customer.Id,
                LongTermSummary = "Verified previous summary", FactsJson = JsonSerializer.Serialize(new[] { "لا يفضل المكالمات" }) });
        await db.SaveChangesAsync();
        return new(tenant, customer.Id, conversation.Id);
    }

    private static GeminiClient Client(HttpClient http) => new(new ConfigurationBuilder().Build(),
        new GeminiMockHandler(new HostingEnvironment { EnvironmentName = "Production" }), NullLogger<GeminiClient>.Instance, http);

    private sealed record MemoryScenario(TenantContext Tenant, Guid CustomerId, Guid ConversationId);

    private sealed class MemoryProvider(string output, Func<Task>? beforeResponse = null) : HttpMessageHandler
    {
        public List<string> Prompts { get; } = [];
        public List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Prompts.Add(body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (beforeResponse is not null) await beforeResponse();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = output } } } } } })) };
        }
    }
}

public sealed class CustomerMemoryCostDatabase(PostgresFixture postgres) : IAsyncLifetime
{
    public IsolatedPostgresDatabase Database { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        Database = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = Database.CreateContext();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Database.DisposeAsync().AsTask();
}
