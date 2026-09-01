using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;

namespace Modules.AI.Workers;

public sealed class AdvertisingAiWorkConsumer(
    AppDbContext db,
    IGeminiClient gemini,
    IEventBus eventBus,
    IProjectSecretVault secretVault) : IIntegrationEventHandler<AdvertisingAiWorkRequested>
{
    public async Task HandleAsync(AdvertisingAiWorkRequested message)
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
        catch (Exception ex)
        {
            failureCode = ex.GetType().Name;
        }
        await eventBus.PublishAsync(new AdvertisingAiWorkCompleted
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
        });
    }
}
