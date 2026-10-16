using System.Text.Json;
using Modules.Content.Domain;

namespace Modules.Content.Services;

public sealed record ContentBrandReference(string Id, string Kind, string FileName, string ObjectKey, string MimeType);

public static class ContentBrandReferences
{
    public static IReadOnlyList<ContentBrandReference> Images(ContentAutomationSettings settings) =>
        JsonSerializer.Deserialize<List<ContentBrandReference>>(settings.ReferenceImagesJson) ?? [];

    public static IReadOnlyList<string> Colors(ContentAutomationSettings settings) =>
        JsonSerializer.Deserialize<List<string>>(settings.ReferenceColorsJson) ?? [];

    public static IReadOnlyList<string> ActiveColors(ContentAutomationSettings settings) =>
        settings.DesignSource == "references"
            ? Colors(settings)
            : JsonSerializer.Deserialize<List<string>>(settings.BrandColorsJson) ?? [];

    public static string Identity(ContentAutomationSettings settings) =>
        settings.DesignSource == "references"
            ? settings.MatchReferenceTone
                ? $"{settings.StylePrompt}\n[references-tone:{settings.ReferenceColorsJson}:{settings.ReferenceImagesJson}]"
                : $"{settings.StylePrompt}\n[references:{settings.ReferenceColorsJson}:{settings.ReferenceImagesJson}]"
            : settings.StylePrompt;
}
