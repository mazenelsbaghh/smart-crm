namespace Modules.Content.Services;

internal static class ContentCardArtwork
{
    // Version the stored asset format so legacy text-free backgrounds are never presented as complete cards.
    internal const string Folder = "full-card-v1";

    internal static bool IsCompleteImage(string? objectKey) =>
        objectKey?.Contains($"/{Folder}/", StringComparison.Ordinal) == true;
}
