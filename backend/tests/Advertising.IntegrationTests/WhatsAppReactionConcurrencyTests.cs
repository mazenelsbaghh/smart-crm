using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.AI.Workers;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Shared.Infrastructure;
using Shared.Queue;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class WhatsAppReactionConcurrencyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Concurrent_reaction_persistence_keeps_one_provider_message_and_broadcast()
    {
        var seed = await SeedConversationAsync();
        var precheckBarrier = new ReactionPrecheckBarrierInterceptor();
        await using var firstDb = postgres.CreateContext(null, precheckBarrier);
        await using var secondDb = postgres.CreateContext(null, precheckBarrier);
        var firstConversation = await ConversationAsync(firstDb, seed.ConversationId);
        var secondConversation = await ConversationAsync(secondDb, seed.ConversationId);
        var broadcasts = new ConcurrentBag<string>();

        var outcomes = await Task.WhenAll(
            PersistAsync(firstDb, firstConversation, seed.AccountId, broadcasts),
            PersistAsync(secondDb, secondConversation, seed.AccountId, broadcasts));

        Assert.Single(outcomes, created => created);
        Assert.Single(outcomes, created => !created);
        await using var verification = postgres.CreateContext();
        var message = Assert.Single(await verification.Messages.IgnoreQueryFilters()
            .Where(candidate => candidate.ConversationId == seed.ConversationId
                && candidate.ExternalMessageId == "provider-race-winner")
            .ToListAsync());
        Assert.Equal("Reaction", message.MessageType);
        Assert.Equal("[تفاعل] 👍", message.Content);
        Assert.Single(broadcasts, method => method == "ReceiveMessage");
    }

    private async Task<ReactionSeed> SeedConversationAsync()
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        db.Projects.Add(new Project { Id = projectId, Name = "Reaction concurrency" });
        db.WhatsAppAccounts.Add(new WhatsAppAccount
        {
            Id = accountId,
            ProjectId = projectId,
            Name = "Reactions",
            IsDefault = true
        });
        db.Customers.Add(new Customer
        {
            Id = customerId,
            ProjectId = projectId,
            PhoneNumber = "201000000001",
            Name = "عميل",
            City = string.Empty
        });
        db.Conversations.Add(new Conversation
        {
            Id = conversationId,
            ProjectId = projectId,
            CustomerId = customerId,
            WhatsAppAccountId = accountId,
            Channel = "WhatsApp",
            Status = "Open"
        });
        await db.SaveChangesAsync();
        return new ReactionSeed(accountId, conversationId);
    }

    private static Task<Conversation> ConversationAsync(AppDbContext db, Guid conversationId) =>
        db.Conversations.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == conversationId);

    private static Task<bool> PersistAsync(
        AppDbContext db,
        Conversation conversation,
        Guid accountId,
        ConcurrentBag<string> broadcasts) => Worker().PersistWhatsAppReactionAsync(
            db,
            Hub(broadcasts),
            new AIReplyWorker.WhatsAppReactionPersistence(
                conversation,
                accountId,
                "provider-race-winner",
                "👍"));

    private static AIReplyWorker Worker()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new AIReplyWorker(
            services,
            RejectingMarketingBrain.Instance,
            new InMemoryEventBus(services),
            NullLogger<AIReplyWorker>.Instance);
    }

    private static IHubContext<NotificationHub> Hub(ConcurrentBag<string> sentMethods)
    {
        var client = Proxy<IClientProxy>((method, arguments) =>
        {
            if (method.Name == nameof(IClientProxy.SendCoreAsync))
                sentMethods.Add((string)arguments![0]!);
            return Task.CompletedTask;
        });
        var clients = Proxy<IHubClients>((method, _) =>
            method.Name == nameof(IHubClients.Group)
                ? client
                : throw new InvalidOperationException($"Unexpected hub client call: {method.Name}"));
        var groups = Proxy<IGroupManager>((method, _) =>
            method.ReturnType == typeof(Task)
                ? Task.CompletedTask
                : throw new InvalidOperationException($"Unexpected group call: {method.Name}"));
        return Proxy<IHubContext<NotificationHub>>((method, _) => method.Name switch
        {
            "get_Clients" => clients,
            "get_Groups" => groups,
            _ => throw new InvalidOperationException($"Unexpected hub call: {method.Name}")
        });
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private sealed class ReactionPrecheckBarrierInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (IsReactionPrecheck(command.CommandText))
            {
                if (Interlocked.Increment(ref _arrivals) == 2) _release.TrySetResult();
                await _release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsReactionPrecheck(string commandText) =>
            commandText.Contains("FROM \"Messages\"", StringComparison.Ordinal)
            && commandText.Contains("\"ConversationId\"", StringComparison.Ordinal)
            && commandText.Contains("\"ExternalMessageId\"", StringComparison.Ordinal);
    }

    private class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod is null ? null : Handler(targetMethod, args);
    }

    private sealed class RejectingMarketingBrain : IAIMarketingBrain
    {
        public static RejectingMarketingBrain Instance { get; } = new();

        public Task<MarketingAnalysisResult> AnalyzeAndGenerateReplyAsync(
            string messageContent,
            string apiKeyOverride = null!,
            string brainContext = null!,
            string chatHistory = null!,
            string customerMemory = null!,
            string[] existingLabels = null!,
            string customerProfile = null!,
            byte[] fileBytes = null!,
            string mimeType = null!,
            string aiTonePreference = null!,
            string aiTargetAudience = null!,
            CustomerReplyRuntime? customerReply = null,
            string? systemPromptOverride = null,
            AIBehaviorSettings? aiBehaviorSettings = null,
            string channel = "WhatsApp") => throw UnexpectedUse();

        public string BuildStaticPrompt(
            string agentName,
            string tonePref,
            string targetAud,
            string approvedKnowledgeBaseText,
            string? systemPromptOverride = null,
            AIBehaviorSettings? aiBehaviorSettings = null,
            string channel = "WhatsApp") => throw UnexpectedUse();

        public string GetCurrentAgentName(string? agentInstructions = null) => throw UnexpectedUse();

        public Task<string> RewriteFollowUpNotesAsync(
            string customerName,
            string notes,
            bool hasAttended,
            string? tone = null,
            string? apiKeyOverride = null,
            string? modelOverride = null) => throw UnexpectedUse();

        private static InvalidOperationException UnexpectedUse() =>
            new("Reaction persistence must not invoke the AI boundary.");
    }

    private sealed record ReactionSeed(Guid AccountId, Guid ConversationId);
}
