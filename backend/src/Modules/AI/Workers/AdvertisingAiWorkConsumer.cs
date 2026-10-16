using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Modules.Advertising.Domain;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;

namespace Modules.AI.Workers;

public sealed class AdvertisingAiWorkConsumer(
    AppDbContext db,
    IGeminiClient gemini,
    IProjectSecretVault secretVault) : IIntegrationEventHandler<AdvertisingAiWorkRequested>
{
    public async Task HandleAsync(AdvertisingAiWorkRequested message)
    {
        await using var transaction = db.Database.IsNpgsql()
            ? await db.Database.BeginTransactionAsync() : null;
        if (transaction is not null)
        {
            var lockKey = $"advertising-ai:{message.ProjectId:N}:{message.RequestId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");
        }
        var work = await db.AdvertisingAiWorkItems.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == message.ProjectId && candidate.Id == message.RequestId);
        if (!CanGenerate(work, message)) return;
        var completion = await GenerateCompletionAsync(message);
        // The response and its delivery are committed together so redelivery never regenerates saved work.
        work!.ResultJson = completion.StructuredResultJson;
        work.ModelVersion = completion.ModelVersion;
        work.FailureCode = completion.FailureCode;
        IntegrationOutbox.Enqueue(db, completion);
        await db.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
    }

    private static bool CanGenerate(AdvertisingAiWorkItem? work, AdvertisingAiWorkRequested message) =>
        work is not null && work.State == AiWorkState.Pending && work.ResultJson is null
        && work.DeadlineUtc > DateTime.UtcNow && message.DeadlineUtc > DateTime.UtcNow
        && work.OwnerId == message.OwnerId && work.OwnerVersion == message.OwnerVersion
        && work.InputHash == message.InputHash && work.Purpose == message.Purpose
        && work.InputJson == message.SourcedInputJson;

    private async Task<AdvertisingAiWorkCompleted> GenerateCompletionAsync(AdvertisingAiWorkRequested message)
    {
        var settings = await db.ProjectSettings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == message.ProjectId);
        var model = settings?.ResolveGeminiModel(DateTime.UtcNow);
        var failureCode = string.Empty;
        var result = "{}";
        try
        {
            result = await gemini.GenerateReplyAsync(message.SourcedInputJson,
                secretVault.Unprotect(message.ProjectId, settings?.GeminiApiKey),
                string.IsNullOrWhiteSpace(model) ? null : model);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            failureCode = ex.GetType().Name;
        }
        return new AdvertisingAiWorkCompleted
        {
            ProjectId = message.ProjectId,
            RequestId = message.RequestId,
            OwnerId = message.OwnerId,
            OwnerVersion = message.OwnerVersion,
            InputHash = message.InputHash,
            StructuredResultJson = result,
            FailureCode = failureCode,
            ModelVersion = model ?? "system-default",
            PromptVersion = "advertising-review.v1",
            SourceAggregateType = "AdvertisingAiWorkItem",
            SourceAggregateId = message.RequestId,
            SourceVersion = message.SourceVersion
        };
    }
}
