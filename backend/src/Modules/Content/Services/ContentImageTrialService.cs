using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Content.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Shared.Storage;

namespace Modules.Content.Services;

public sealed record ContentImageTrialRequest([Required, MaxLength(6000)] string Prompt,
    string ImageProvider = "Gemini", string OpenAiImageModel = OpenAiImageClient.HighestQualityModel,
    [MaxLength(2048)] string? OpenAiImageApiKey = null);

public sealed record ContentImageTrialResponse(string Image, string Model, string Size)
{
    public static ContentImageTrialResponse From(GeneratedImage generated) =>
        new($"data:{generated.MimeType};base64,{Convert.ToBase64String(generated.Bytes)}", generated.Model, generated.Size);
}

public sealed class ContentImageTrialService(AppDbContext db, ContentImageClient images,
    IProjectSecretVault secrets, IObjectStorage storage)
{
    public async Task<ContentImageTrialResponse> GenerateAsync(Guid projectId, ContentImageTrialRequest input, CancellationToken cancellationToken)
    {
        if (input.ImageProvider is not ("Gemini" or "OpenAI") || !OpenAiImageClient.SupportsModel(input.OpenAiImageModel))
            throw new ArgumentException("اختر Gemini أو أحد موديلات صور ChatGPT المتاحة.");
        var stored = await db.ContentAutomationSettings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);
        var project = await db.ProjectSettings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.ProjectId == projectId, cancellationToken);
        var imageSettings = new ContentAutomationSettings { ProjectId = projectId, ImageProvider = input.ImageProvider,
            OpenAiImageModel = input.OpenAiImageModel, OpenAiImageApiKey = string.IsNullOrWhiteSpace(input.OpenAiImageApiKey)
                ? stored?.OpenAiImageApiKey ?? string.Empty : secrets.Protect(projectId, input.OpenAiImageApiKey.Trim()) };
        var logo = await LogoAsync(stored, cancellationToken);
        var prompt = $"Create one finished flat image. Brief: {JsonSerializer.Serialize(input.Prompt.Trim())}. "
            + $"Brand visual style: {JsonSerializer.Serialize(stored?.StylePrompt)}. "
            + "If a logo is supplied, preserve its identity and use it clearly. No mockup or watermark.";
        var generated = await images.GenerateAsync(imageSettings, new(prompt,
            secrets.Unprotect(projectId, project?.GeminiApiKey), logo, GeminiImageClient.SquareAspectRatio,
            ImageSize: GeminiImageClient.PreviewImageSize), cancellationToken);
        return ContentImageTrialResponse.From(generated);
    }

    private async Task<GeminiReferenceImage?> LogoAsync(ContentAutomationSettings? settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings?.LogoObjectKey)) return null;
        await using var stream = await storage.DownloadAsync(settings.LogoObjectKey, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return new(buffer.ToArray(), settings.LogoMimeType ?? "image/png");
    }
}
