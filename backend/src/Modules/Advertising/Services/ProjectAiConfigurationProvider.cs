using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record ProjectAiConfiguration(string? ApiKey, string? Model);

public interface IProjectAiConfigurationProvider
{
    Task<ProjectAiConfiguration> GetAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public sealed class ProjectAiConfigurationProvider(AppDbContext db) : IProjectAiConfigurationProvider
{
    public async Task<ProjectAiConfiguration> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var settings = await db.ProjectSettings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId, cancellationToken);
        return new(
            string.IsNullOrWhiteSpace(settings?.GeminiApiKey) ? null : settings.GeminiApiKey,
            string.IsNullOrWhiteSpace(settings?.GeminiModel) ? null : settings.GeminiModel);
    }
}
