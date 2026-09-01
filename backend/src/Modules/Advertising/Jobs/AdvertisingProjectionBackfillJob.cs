using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Jobs;

public sealed class AdvertisingProjectionBackfillJob(AppDbContext db, ILogger<AdvertisingProjectionBackfillJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (await db.AdvertisingProjectionBackfillRuns.AsNoTracking()
                .AnyAsync(x => x.State == "Completed", cancellationToken))
            return;
        var run = await db.AdvertisingProjectionBackfillRuns
            .Where(x => x.State == "Running" || x.State == "Pending")
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            run = new AdvertisingProjectionBackfillRun();
            db.AdvertisingProjectionBackfillRuns.Add(run);
        }
        run.State = "Running";
        run.StartedAtUtc ??= DateTime.UtcNow;
        run.AttemptCount++;
        run.LastFailureCode = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            run.Phase = "ProjectContext";
            await db.Database.ExecuteSqlRawAsync(ProjectContextSql, cancellationToken);
            run.Phase = "Knowledge";
            await db.Database.ExecuteSqlRawAsync(KnowledgeSql, cancellationToken);
            run.Phase = "Media";
            await db.Database.ExecuteSqlRawAsync(MediaSql, cancellationToken);
            run.Phase = "WhatsAppRoutes";
            await db.Database.ExecuteSqlRawAsync(WhatsAppRoutesSql, cancellationToken);
            run.Phase = "LegacyAdvertisingOwnership";
            await BackfillLegacyAdvertisingOwnershipAsync(cancellationToken);

            var parity = await ReadParityAsync(cancellationToken);
            run.ParityJson = JsonSerializer.Serialize(parity);
            run.CursorJson = JsonSerializer.Serialize(new { completedAtUtc = DateTime.UtcNow });
            run.State = parity.IsComplete ? "Completed" : "ParityFailed";
            run.Phase = "Parity";
            run.CompletedAtUtc = parity.IsComplete ? DateTime.UtcNow : null;
            run.LastFailureCode = parity.IsComplete ? null : "PROJECTION_PARITY_FAILED";
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            run.State = "Failed";
            run.LastFailureCode = ex.GetType().Name;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError(ex, "Advertising projection backfill failed in phase {Phase}", run.Phase);
            throw;
        }
    }

    private async Task BackfillLegacyAdvertisingOwnershipAsync(CancellationToken cancellationToken)
    {
        var legacy = await db.ManagedAdvertisements.IgnoreQueryFilters()
            .Where(ad => ad.OwnershipRecordId == null && ad.AdExternalId != null)
            .OrderBy(ad => ad.ProjectId).ThenBy(ad => ad.CampaignExternalId).ThenBy(ad => ad.Id)
            .ToListAsync(cancellationToken);
        foreach (var group in legacy.GroupBy(ad => new { ad.ProjectId, CampaignId = ad.CampaignExternalId ?? $"legacy:{ad.Id:N}" }))
        {
            var connectionId = group.Select(ad => ad.ConnectionId).FirstOrDefault(id => id != null)
                ?? await db.AdvertisingConnections.IgnoreQueryFilters().Where(connection => connection.ProjectId == group.Key.ProjectId)
                    .Select(connection => (Guid?)connection.Id).FirstOrDefaultAsync(cancellationToken)
                ?? Guid.Empty;
            var ownership = new ManagedOwnershipRecord
            {
                ProjectId = group.Key.ProjectId, ConnectionId = connectionId,
                ProviderCampaignExternalId = group.Key.CampaignId,
                OwnershipKind = ManagedOwnershipKind.ManualUnowned,
                ImportEvidenceJson = JsonSerializer.Serialize(new { source = "LegacyBackfill", providerMutation = false }),
                AllowedMutationScopeJson = "[]"
            };
            db.AdvertisingManagedOwnership.Add(ownership);
            foreach (var ad in group)
            {
                ad.OwnershipRecordId = ownership.Id;
                ad.ReconciliationState = ProviderReconciliationState.LegacyUnverified;
                ad.ManagementSource = "LegacyUnverified";
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProjectionParity> ReadParityAsync(CancellationToken cancellationToken)
    {
        var sourceProjects = await ScalarAsync("SELECT COUNT(*) FROM \"Projects\"", cancellationToken);
        var projectedProjects = await db.ProjectAdvertisingContextProjections.IgnoreQueryFilters().LongCountAsync(cancellationToken);
        var sourceKnowledge = await ScalarAsync("SELECT COUNT(*) FROM \"KnowledgeDocuments\"", cancellationToken);
        var projectedKnowledge = await db.AdvertisingKnowledgeProjections.IgnoreQueryFilters().LongCountAsync(cancellationToken);
        var sourceMedia = await ScalarAsync("SELECT COUNT(*) FROM \"Assets\"", cancellationToken);
        var projectedMedia = await db.AdvertisingMediaProjections.IgnoreQueryFilters().LongCountAsync(cancellationToken);
        var sourceRoutes = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters().LongCountAsync(cancellationToken);
        var projectedRoutes = await db.WhatsAppInboundRouteProjections.IgnoreQueryFilters().LongCountAsync(cancellationToken);
        return new(sourceProjects, projectedProjects, sourceKnowledge, projectedKnowledge, sourceMedia, projectedMedia, sourceRoutes, projectedRoutes);
    }

    private async Task<long> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync(cancellationToken);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private sealed record ProjectionParity(long SourceProjects, long ProjectedProjects, long SourceKnowledge,
        long ProjectedKnowledge, long SourceMedia, long ProjectedMedia, long SourceRoutes, long ProjectedRoutes)
    {
        public bool IsComplete => ProjectedProjects >= SourceProjects && ProjectedKnowledge >= SourceKnowledge &&
                                  ProjectedMedia >= SourceMedia && ProjectedRoutes >= SourceRoutes;
    }

    private const string ProjectContextSql = """
        INSERT INTO "ProjectAdvertisingContextProjections"
            ("Id", "ProjectId", "LifecycleState", "ReportingTimezoneIana", "AiConfigurationVersion", "AllowedAiModel", "AiSettingsHash", "UpdatedFromEventId", "SourceVersion", "UpdatedAtUtc", "CreatedAt", "UpdatedAt")
        SELECT gen_random_uuid(), p."Id", 'Active', COALESCE(NULLIF(s."Timezone", ''), 'Africa/Cairo'), 1,
               COALESCE(s."GeminiModel", ''), '', '00000000-0000-0000-0000-000000000000', 1, NOW(), NOW(), NOW()
        FROM "Projects" p LEFT JOIN "ProjectSettings" s ON s."ProjectId" = p."Id"
        ON CONFLICT ("ProjectId") DO NOTHING;
        """;

    private const string KnowledgeSql = """
        INSERT INTO "AdvertisingKnowledgeProjections"
            ("Id", "ProjectId", "DocumentId", "DocumentVersion", "RevisionHash", "State", "SafeFactsJson", "AffectedOfferKeysJson", "UpdatedFromEventId", "IsTombstoned", "CreatedAt", "UpdatedAt")
        SELECT gen_random_uuid(), k."ProjectId", k."Id", k."Version", md5(COALESCE(k."Content", '')), k."Status", '{{}}', '[]',
               '00000000-0000-0000-0000-000000000000', k."Status" = 'Archived', NOW(), NOW()
        FROM "KnowledgeDocuments" k
        ON CONFLICT ("ProjectId", "DocumentId") DO NOTHING;
        """;

    private const string MediaSql = """
        INSERT INTO "AdvertisingMediaProjections"
            ("Id", "ProjectId", "AssetId", "AssetVersion", "ContentType", "FileHash", "ObjectReference", "FileSize", "RightsState", "BrandMetadataJson", "UpdatedFromEventId", "IsTombstoned", "CreatedAt", "UpdatedAt")
        SELECT gen_random_uuid(), a."ProjectId", a."Id", 1, a."ContentType", a."FileHash", a."StoragePath", a."FileSize", 'Owned', '{{}}',
               '00000000-0000-0000-0000-000000000000', FALSE, NOW(), NOW()
        FROM "Assets" a
        ON CONFLICT ("ProjectId", "AssetId") DO NOTHING;
        """;

    private const string WhatsAppRoutesSql = """
        INSERT INTO "WhatsAppInboundRouteProjections"
            ("Id", "ProjectId", "DestinationId", "DestinationVersion", "Provider", "WabaExternalId", "PhoneNumberExternalId", "IntegrationMode", "SourceEventId", "SourceAggregateVersion", "State", "UpdatedAtUtc", "CreatedAt", "UpdatedAt")
        SELECT gen_random_uuid(), d."ProjectId", d."Id", d."Version", 'Meta', d."WabaExternalId", d."PhoneNumberExternalId",
               CASE d."WhatsAppIntegrationMode" WHEN 0 THEN 'CloudApi' WHEN 1 THEN 'CloudApiCoexistence' ELSE 'BaileysObservedExperimental' END,
               '00000000-0000-0000-0000-000000000000', d."Version", CASE WHEN d."State" = 1 THEN 'Active' ELSE 'Revoked' END, NOW(), NOW(), NOW()
        FROM "AdvertisingWhatsAppDestinations" d
        ON CONFLICT ("ProjectId", "DestinationId", "DestinationVersion") DO NOTHING;
        """;
}
