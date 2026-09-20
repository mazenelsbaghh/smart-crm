using Microsoft.EntityFrameworkCore;
using Modules.Content.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Shared.Storage;

namespace Modules.Content.Services;

public sealed class ContentCardGameDesignService(
    AppDbContext dbContext,
    GeminiImageClient imageClient,
    IProjectSecretVault secretVault,
    IObjectStorage objectStorage,
    ILogger<ContentCardGameDesignService> logger)
{
    public async Task GenerateAsync(Guid projectId, Guid gameId, CancellationToken cancellationToken)
    {
        var game = await dbContext.ContentCardGames.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.ProjectId == projectId && item.Id == gameId, cancellationToken)
            ?? throw new InvalidOperationException("اللعبة غير موجودة.");
        var claimed = await dbContext.ContentCardGames.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.Id == gameId && item.DesignStatus == ContentCardGameDesignStatus.Queued)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.DesignStatus, ContentCardGameDesignStatus.Generating), cancellationToken);
        if (claimed == 0) return;
        try
        {
            await dbContext.Entry(game).ReloadAsync(cancellationToken);
            await GenerateDeckArtworkAsync(game, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!await StopRequestedAsync(game, CancellationToken.None))
                await SetFinalStatusAsync(game, ContentCardGameDesignStatus.Queued, null);
            throw;
        }
        catch (Exception exception)
        {
            if (await StopRequestedAsync(game, CancellationToken.None)) return;
            logger.LogError(exception, "Card-game design failed for game {GameId}", gameId);
            await SetFinalStatusAsync(game, ContentCardGameDesignStatus.Failed,
                SafeError(exception, "تعذر توليد تصاميم اللعبة. راجع الإعدادات وحاول مرة أخرى."));
            await StopRequestedAsync(game, CancellationToken.None);
        }
    }

    private async Task GenerateDeckArtworkAsync(ContentCardGame game, CancellationToken cancellationToken)
    {
        var resources = await LoadResourcesAsync(game, cancellationToken);
        game.DesignError = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (await StopRequestedAsync(game, cancellationToken)) return;
        await GenerateBackAsync(game, resources, cancellationToken);
        foreach (var card in resources.Cards)
        {
            if (await StopRequestedAsync(game, cancellationToken)) return;
            await GenerateCardAsync(game, card, resources, cancellationToken);
        }
        if (await StopRequestedAsync(game, cancellationToken)) return;
        await CompleteDesignAsync(game, resources.Cards, cancellationToken);
    }

    private async Task<bool> StopRequestedAsync(ContentCardGame game, CancellationToken cancellationToken)
    {
        var status = await dbContext.ContentCardGames.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.ProjectId == game.ProjectId && item.Id == game.Id)
            .Select(item => item.DesignStatus).SingleAsync(cancellationToken);
        if (status != ContentCardGameDesignStatus.Stopping && status != ContentCardGameDesignStatus.Cancelled) return false;
        await dbContext.ContentCardGames.IgnoreQueryFilters()
            .Where(item => item.ProjectId == game.ProjectId && item.Id == game.Id && item.DesignStatus == ContentCardGameDesignStatus.Stopping)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.DesignStatus, ContentCardGameDesignStatus.Cancelled), cancellationToken);
        return true;
    }

    private async Task<DesignResources> LoadResourcesAsync(ContentCardGame game, CancellationToken cancellationToken)
    {
        var settings = await dbContext.ProjectSettings.IgnoreQueryFilters().SingleAsync(item => item.ProjectId == game.ProjectId, cancellationToken);
        var apiKey = secretVault.Unprotect(game.ProjectId, settings.GeminiApiKey);
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("أضف مفتاح Gemini في إعدادات المشروع أولاً.");
        var cards = await dbContext.ContentGameCards.IgnoreQueryFilters().Where(card => card.ProjectId == game.ProjectId && card.GameId == game.Id)
            .OrderBy(card => card.CardIndex).ToListAsync(cancellationToken);
        if (cards.Count != game.CardCount) throw new InvalidOperationException("بيانات كروت اللعبة غير مكتملة.");
        await using var logoStream = await objectStorage.DownloadAsync(game.BrandLogoObjectKey, cancellationToken);
        using var logoBuffer = new MemoryStream();
        await logoStream.CopyToAsync(logoBuffer, cancellationToken);
        return new DesignResources(apiKey, cards, new GeminiReferenceImage(logoBuffer.ToArray(), LogoMimeType(game.BrandLogoObjectKey)));
    }

    private async Task GenerateBackAsync(ContentCardGame game, DesignResources resources, CancellationToken cancellationToken)
    {
        if (ContentCardArtwork.IsCompleteImage(game.BackImageObjectKey)) return;
        var back = await imageClient.GenerateAsync(new GeminiImageRequest(
            ContentCardGameService.BuildCardBackImagePrompt(game), resources.ApiKey, resources.Logo, GeminiImageClient.PortraitAspectRatio), cancellationToken);
        game.BackImageObjectKey = await UploadAsync(game.ProjectId, game.Id, "back", back, cancellationToken);
        game.BackImageMimeType = back.MimeType;
        game.ImageModel = back.Model;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task GenerateCardAsync(ContentCardGame game, ContentGameCard card, DesignResources resources, CancellationToken cancellationToken)
    {
        if (ContentCardArtwork.IsCompleteImage(card.ImageObjectKey)) return;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var image = await imageClient.GenerateAsync(new GeminiImageRequest(ContentCardGameService.BuildCardFaceImagePrompt(game, card), resources.ApiKey, resources.Logo, GeminiImageClient.PortraitAspectRatio), cancellationToken);
            card.ImageObjectKey = await UploadAsync(game.ProjectId, game.Id, $"card-{card.CardIndex + 1}", image, cancellationToken);
            card.ImageMimeType = image.MimeType;
            card.ImageError = null;
            game.ImageModel ??= image.Model;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Card artwork failed for game {GameId}, card {CardId}", game.Id, card.Id);
            card.ImageError = SafeError(exception, "تعذر تصميم هذا الكارت. يمكنك إعادة المحاولة.");
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteDesignAsync(ContentCardGame game, IReadOnlyList<ContentGameCard> cards, CancellationToken cancellationToken)
    {
        var missingArtwork = !ContentCardArtwork.IsCompleteImage(game.BackImageObjectKey) || cards.Any(card => !ContentCardArtwork.IsCompleteImage(card.ImageObjectKey));
        await SetFinalStatusAsync(game, missingArtwork ? ContentCardGameDesignStatus.Failed : ContentCardGameDesignStatus.Ready,
            missingArtwork ? "تعذر توليد بعض تصميمات الكروت. يمكنك إعادة المحاولة." : null);
        await StopRequestedAsync(game, cancellationToken);
    }

    private Task<int> SetFinalStatusAsync(ContentCardGame game, string status, string? error) =>
        dbContext.ContentCardGames.IgnoreQueryFilters()
            .Where(item => item.ProjectId == game.ProjectId && item.Id == game.Id && item.DesignStatus == ContentCardGameDesignStatus.Generating)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.DesignStatus, status).SetProperty(item => item.DesignError, error));

    private async Task<string> UploadAsync(Guid projectId, Guid gameId, string name, GeneratedImage image, CancellationToken cancellationToken)
    {
        var extension = image.MimeType.Contains("webp", StringComparison.OrdinalIgnoreCase) ? "webp" : "png";
        var objectKey = $"content/{projectId:N}/card-games/{gameId:N}/{ContentCardArtwork.Folder}/{name}-{Guid.NewGuid():N}.{extension}";
        await using var stream = new MemoryStream(image.Bytes);
        await objectStorage.UploadAsync(objectKey, stream, image.MimeType, cancellationToken);
        return objectKey;
    }

    private static string LogoMimeType(string objectKey) => Path.GetExtension(objectKey).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => "image/png"
    };

    private static string SafeError(Exception exception, string fallback)
    {
        var value = exception is InvalidOperationException ? exception.Message : fallback;
        var printable = new string(value.Where(character => !char.IsControl(character)).ToArray());
        return printable[..Math.Min(printable.Length, 1_000)];
    }

    private sealed record DesignResources(string ApiKey, List<ContentGameCard> Cards, GeminiReferenceImage Logo);
}
