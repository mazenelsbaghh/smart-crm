using Modules.Content.Services;

namespace Modules.Content.Jobs;

public sealed class ContentCardGameDesignJob(ContentCardGameDesignService design)
{
    public Task GenerateAsync(Guid projectId, Guid gameId) => design.GenerateAsync(projectId, gameId, CancellationToken.None);

    public Task GenerateAsync(Guid projectId, Guid gameId, CancellationToken cancellationToken) => design.GenerateAsync(projectId, gameId, cancellationToken);
}
