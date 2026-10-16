using Modules.Content.Domain;
using Shared.Security;

namespace Modules.Content.Services;

public sealed class ContentImageClient(
    GeminiImageClient gemini, OpenAiImageClient openAi, IProjectSecretVault secretVault)
{
    public Task<GeneratedImage> GenerateAsync(
        ContentAutomationSettings settings, GeminiImageRequest imageRequest, CancellationToken cancellationToken)
    {
        if (settings.ImageProvider == "Gemini") return gemini.GenerateAsync(imageRequest, cancellationToken);
        if (settings.ImageProvider != "OpenAI") throw new InvalidOperationException("مزود الصور غير مدعوم.");
        var references = new List<GeminiReferenceImage>();
        if (imageRequest.Logo is not null) references.Add(imageRequest.Logo);
        if (imageRequest.References is not null) references.AddRange(imageRequest.References);
        var apiKey = secretVault.Unprotect(settings.ProjectId, settings.OpenAiImageApiKey);
        return openAi.GenerateAsync(new OpenAiImageRequest(imageRequest.Prompt, apiKey, references, imageRequest.AspectRatio,
            settings.OpenAiImageModel, imageRequest.ImageSize), cancellationToken);
    }
}
