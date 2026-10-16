using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Modules.Advertising.Workers;
using Modules.Brain.Services;
using Shared.Infrastructure;
using Shared.Events;
using Shared.Queue;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class KnowledgeRevocationTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("Edit", "Draft")]
    [InlineData("Reject", "Rejected")]
    [InlineData("Delete", "Deleted")]
    public async Task Withdrawing_approved_knowledge_invalidates_its_advertising_profile_and_late_approval_cannot_restore_it(string operation, string state)
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = isolated.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var knowledge = new KnowledgeBaseService(db, new Embeddings());
        var document = await knowledge.CreateDocumentAsync(projectId, "Course", "كورس بسعر 100 جنيه للحجز عبر واتساب", null);
        await knowledge.ApproveDocumentAsync(document.Id);
        var published = await LatestSnapshot(db, document.Id);
        var projection = new KnowledgeProjectionConsumer(db);
        // Full snapshots also cover legacy documents or delayed creation events.
        await projection.HandleAsync(published);
        var profile = await db.AdvertisingProfiles.SingleAsync();
        Assert.Equal("Ready", profile.Status);

        switch (operation)
        {
            case "Edit": await knowledge.UpdateDocumentAsync(document.Id, "Course", "كورس بسعر 200 جنيه للحجز عبر واتساب", null); break;
            case "Reject": await knowledge.RejectDocumentAsync(document.Id); break;
            case "Delete": await knowledge.DeleteDocumentAsync(document.Id); break;
        }
        var withdrawal = await LatestSnapshot(db, document.Id);
        Assert.True(withdrawal.SourceVersion > published.SourceVersion);
        Assert.Equal(state, withdrawal.State);
        Assert.Equal(operation == "Delete", withdrawal.IsTombstone);
        await projection.HandleAsync(withdrawal);
        await projection.HandleAsync(new AdvertisingKnowledgeChanged
        {
            ProjectId = projectId, KnowledgeDocumentId = document.Id, State = "Approved",
            SourceAggregateType = published.SourceAggregateType, SourceAggregateId = document.Id,
            SourceVersion = published.SourceVersion, SafeFactsJson = published.SafeFactsJson,
            RevisionHash = published.RevisionHash, AffectedOfferKeysJson = published.AffectedOfferKeysJson
        });
        await projection.HandleAsync(new KnowledgePublishedChangedEvent
        {
            ProjectId = projectId, DocumentId = document.Id, Version = checked((int)published.SourceVersion),
            Status = "Published", Title = "Course", Content = "كورس بسعر 100 جنيه للحجز عبر واتساب 01012345678"
        });

        db.ChangeTracker.Clear();
        var stale = await db.AdvertisingProfiles.SingleAsync(row => row.Id == profile.Id);
        Assert.Equal("Stale", stale.Status);
        Assert.NotNull(stale.StaleAtUtc);
        Assert.False(await db.AdvertisingProfiles.AnyAsync(row => row.Status == "Ready"));
        var snapshot = await db.AdvertisingKnowledgeProjections.SingleAsync();
        Assert.Equal(state, snapshot.State);
        Assert.Equal(withdrawal.SourceVersion, snapshot.DocumentVersion);
        if (operation == "Edit")
        {
            await knowledge.ApproveDocumentAsync(document.Id);
            await projection.HandleAsync(await LatestSnapshot(db, document.Id));
            Assert.Single(await db.AdvertisingProfiles.Where(row => row.Status == "Ready").ToListAsync());
            Assert.Equal(200m, (await db.AdvertisingOffers.SingleAsync(row => row.ProfileId != profile.Id)).Price);
        }
    }

    [Fact]
    public async Task Latest_tombstone_applies_across_a_missing_revision_and_other_documents_stay_ready()
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = isolated.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var knowledge = new KnowledgeBaseService(db, new Embeddings());
        var projection = new KnowledgeProjectionConsumer(db);
        var first = await knowledge.CreateDocumentAsync(projectId, "First", "منتج 100 جنيه واتساب", null);
        await knowledge.ApproveDocumentAsync(first.Id);
        await projection.HandleAsync(await LatestSnapshot(db, first.Id));
        var second = await knowledge.CreateDocumentAsync(projectId, "Second", "منتج 200 جنيه واتساب", null);
        await projection.HandleAsync(await LatestSnapshot(db, second.Id));
        Assert.Single(await db.AdvertisingProfiles.Where(row => row.Status == "Ready").ToListAsync());
        await knowledge.ApproveDocumentAsync(second.Id);
        await projection.HandleAsync(await LatestSnapshot(db, second.Id));
        await knowledge.UpdateDocumentAsync(first.Id, "First", "منتج 300 جنيه واتساب", null);
        var delayedDraft = await LatestSnapshot(db, first.Id);
        await knowledge.DeleteDocumentAsync(first.Id);

        await projection.HandleAsync(await LatestSnapshot(db, first.Id));
        await projection.HandleAsync(delayedDraft);

        db.ChangeTracker.Clear();
        var active = Assert.Single(await db.AdvertisingProfiles.Where(row => row.Status == "Ready").ToListAsync());
        Assert.Equal("Second", (await db.AdvertisingOffers.SingleAsync(row => row.ProfileId == active.Id)).Name);
        Assert.True((await db.AdvertisingKnowledgeProjections.SingleAsync(row => row.DocumentId == first.Id)).IsTombstoned);
    }

    private static async Task<AdvertisingKnowledgeChanged> LatestSnapshot(AppDbContext db, Guid documentId)
    {
        var snapshots = await db.IntegrationOutboxMessages.Where(row => row.EventType == "KnowledgePublishedChanged.v2").ToListAsync();
        return snapshots.Select(row => JsonSerializer.Deserialize<AdvertisingKnowledgeChanged>(row.PayloadJson)!)
            .Where(snapshot => snapshot.KnowledgeDocumentId == documentId).OrderByDescending(snapshot => snapshot.SourceVersion).First();
    }

    private sealed class Embeddings : IGeminiClient
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) => Task.FromResult(Enumerable.Repeat(0.01f, 768).ToArray());
        public Task<string> GenerateReplyAsync(string text, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => throw new NotSupportedException();
        public Task<string> GenerateReplyAsync(string text, byte[] bytes, string mimeType, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string text, string apiKeyOverride = null!, string modelOverride = null!) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string text, string model, int ttlSeconds, string apiKeyOverride = null!) => throw new NotSupportedException();
    }
}
