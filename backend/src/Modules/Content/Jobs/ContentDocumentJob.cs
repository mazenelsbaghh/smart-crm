using Modules.Content.Services;

namespace Modules.Content.Jobs;

public sealed class ContentDocumentJob(ContentDocumentGenerationService generation)
{
    public Task GenerateAsync(Guid projectId, Guid documentId) =>
        generation.GenerateAsync(projectId, documentId, CancellationToken.None);

    public Task GenerateAsync(Guid projectId, Guid documentId, CancellationToken cancellationToken) =>
        generation.GenerateAsync(projectId, documentId, cancellationToken);

    public Task RegenerateImageAsync(Guid projectId, Guid pageId) =>
        generation.RegenerateImageAsync(projectId, pageId, CancellationToken.None);

    public Task RegenerateImageAsync(Guid projectId, Guid pageId, CancellationToken cancellationToken) =>
        generation.RegenerateImageAsync(projectId, pageId, cancellationToken);

    public Task RegenerateImagesAsync(Guid projectId, Guid documentId) =>
        generation.RegenerateImagesAsync(projectId, documentId, CancellationToken.None);

    public Task RegenerateImagesAsync(Guid projectId, Guid documentId, CancellationToken cancellationToken) =>
        generation.RegenerateImagesAsync(projectId, documentId, cancellationToken);
}
