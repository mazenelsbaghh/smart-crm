using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Content.Domain;
using Modules.Content.Jobs;
using Modules.Content.Services;
using Shared.Infrastructure;
using Shared.Security;
using Shared.Storage;

namespace Modules.Content.API;

[ApiController]
[Authorize]
[Route("api/content/card-games")]
public sealed class ContentCardGamesController(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    IProjectAuthorizationService authorization,
    IObjectStorage objectStorage,
    IBackgroundJobClient jobs,
    ContentCardGameService games) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var items = await dbContext.ContentCardGames.IgnoreQueryFilters()
            .Where(game => game.ProjectId == projectId)
            .OrderByDescending(game => game.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        return Ok(new { games = items.Select(Summary) });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var game = await FindGame(projectId, id, cancellationToken);
        if (game is null) return NotFound(new { error = "اللعبة غير موجودة." });
        var cards = await dbContext.ContentGameCards.IgnoreQueryFilters()
            .Where(card => card.ProjectId == projectId && card.GameId == id)
            .OrderBy(card => card.CardIndex)
            .ToListAsync(cancellationToken);
        return Ok(new
        {
            game = new
            {
                id = game.Id,
                title = game.Title,
                brief = game.Brief,
                mechanic = game.Mechanic,
                instructions = game.Instructions,
                cardCount = game.CardCount,
                brandColors = DeserializeColors(game.BrandColorsJson),
                logoUrl = LogoRoute(game.Id, game.UpdatedAt),
                backImageUrl = string.IsNullOrWhiteSpace(game.BackImageObjectKey) ? null : BackImageRoute(game.Id, game.UpdatedAt),
                designStatus = game.DesignStatus,
                designError = game.DesignError,
                plannerModel = game.PlannerModel,
                createdAt = game.CreatedAt,
                updatedAt = game.UpdatedAt
            },
            cards = cards.Select(card => new
            {
                id = card.Id,
                cardIndex = card.CardIndex,
                category = card.Category,
                title = card.Title,
                prompt = card.Prompt,
                instruction = card.Instruction,
                imageUrl = string.IsNullOrWhiteSpace(card.ImageObjectKey) ? null : CardImageRoute(game.Id, card.Id, card.UpdatedAt),
                imageError = card.ImageError
            })
        });
    }

    [HttpGet("{id:guid}/logo")]
    public async Task<IActionResult> Logo(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var game = await FindGame(projectId, id, cancellationToken);
        if (game is null || string.IsNullOrWhiteSpace(game.BrandLogoObjectKey)) return NotFound();
        var stream = await objectStorage.DownloadAsync(game.BrandLogoObjectKey, cancellationToken);
        Response.Headers.CacheControl = "private, max-age=604800, immutable";
        return File(stream, ContentDocumentAssetRoutes.MimeFromKey(game.BrandLogoObjectKey), enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/back")]
    public async Task<IActionResult> Back(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var game = await FindGame(projectId, id, cancellationToken);
        if (game is null || string.IsNullOrWhiteSpace(game.BackImageObjectKey)) return NotFound();
        var stream = await objectStorage.DownloadAsync(game.BackImageObjectKey, cancellationToken);
        Response.Headers.CacheControl = "private, max-age=604800, immutable";
        return File(stream, game.BackImageMimeType ?? ContentDocumentAssetRoutes.MimeFromKey(game.BackImageObjectKey), enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/cards/{cardId:guid}/image")]
    public async Task<IActionResult> CardImage(Guid id, Guid cardId, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var card = await dbContext.ContentGameCards.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.GameId == id && item.Id == cardId, cancellationToken);
        if (card is null || string.IsNullOrWhiteSpace(card.ImageObjectKey)) return NotFound();
        var stream = await objectStorage.DownloadAsync(card.ImageObjectKey, cancellationToken);
        Response.Headers.CacheControl = "private, max-age=604800, immutable";
        return File(stream, card.ImageMimeType ?? ContentDocumentAssetRoutes.MimeFromKey(card.ImageObjectKey), enableRangeProcessing: true);
    }

    [HttpPost("ideas")]
    public async Task<IActionResult> SuggestIdeas(CardGameIdeasRequest request, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        if ((request.WorkshopBrief?.Length ?? 0) > 2_000)
            return BadRequest(new { error = "وصف الورشة يجب ألا يتجاوز 2,000 حرف." });
        try { return Ok(new { ideas = await games.SuggestIdeasAsync(projectId, request.WorkshopBrief, cancellationToken) }); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return StatusCode(502, new { error = exception.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateCardGameRequest request, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        try
        {
            var game = await games.CreateAsync(projectId,
                new CreateCardGameInput(request.Title, request.Brief ?? string.Empty, request.Mechanic, request.CardCount),
                cancellationToken);
            var queued = await QueueDesignAsync(game);
            return CreatedAtAction(nameof(Get), new { id = game.Id }, new
            {
                id = game.Id,
                message = queued
                    ? $"تم إنشاء «{game.Title}» بعدد {game.CardCount} كارت بالإنجليزية. جارٍ تصميم الوجوه والظهر بهوية المشروع."
                    : $"تم إنشاء «{game.Title}» بالإنجليزية. المحتوى محفوظ؛ يمكنك بدء التصميم مرة أخرى من اللعبة."
            });
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return StatusCode(502, new { error = exception.Message }); }
    }

    [HttpPost("{id:guid}/design")]
    public async Task<IActionResult> GenerateDesign(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        var game = await FindGame(projectId, id, cancellationToken);
        if (game is null) return NotFound(new { error = "اللعبة غير موجودة." });
        if (game.DesignStatus == ContentCardGameDesignStatus.Generating)
            return Accepted(new { message = "تصاميم اللعبة قيد التنفيذ بالفعل." });

        game.DesignStatus = ContentCardGameDesignStatus.Queued;
        game.DesignError = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        var queued = await QueueDesignAsync(game);
        return Accepted(new { message = queued ? "بدأنا تصميم الوجوه والظهر بالذكاء الاصطناعي." : "المحتوى محفوظ؛ تعذر بدء التصميم الآن. حاول مرة أخرى." });
    }

    private static object Summary(ContentCardGame game) => new
    {
        id = game.Id,
        title = game.Title,
        mechanic = game.Mechanic,
        cardCount = game.CardCount,
        designStatus = game.DesignStatus,
        createdAt = game.CreatedAt,
        updatedAt = game.UpdatedAt
    };

    private async Task<bool> QueueDesignAsync(ContentCardGame game)
    {
        try
        {
            jobs.Enqueue<ContentCardGameDesignJob>(job => job.GenerateAsync(game.ProjectId, game.Id));
            return true;
        }
        catch (Exception)
        {
            game.DesignStatus = ContentCardGameDesignStatus.Failed;
            game.DesignError = "تعذر بدء التصميم. يمكنك إعادة المحاولة.";
            await dbContext.SaveChangesAsync(CancellationToken.None);
            return false;
        }
    }

    private Task<ContentCardGame?> FindGame(Guid projectId, Guid id, CancellationToken cancellationToken) =>
        dbContext.ContentCardGames.IgnoreQueryFilters().SingleOrDefaultAsync(
            game => game.ProjectId == projectId && game.Id == id, cancellationToken);

    private static IReadOnlyList<string> DeserializeColors(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private static string LogoRoute(Guid gameId, DateTime updatedAt) =>
        $"/api/content/card-games/{gameId:D}/logo?v={updatedAt.Ticks}";

    private static string BackImageRoute(Guid gameId, DateTime updatedAt) =>
        $"/api/content/card-games/{gameId:D}/back?v={updatedAt.Ticks}";

    private static string CardImageRoute(Guid gameId, Guid cardId, DateTime updatedAt) =>
        $"/api/content/card-games/{gameId:D}/cards/{cardId:D}/image?v={updatedAt.Ticks}";

    private Guid ActiveProjectId() => tenantContext.ProjectId != Guid.Empty
        ? tenantContext.ProjectId
        : throw new UnauthorizedAccessException("Active project context is required.");
}

public sealed record CardGameIdeasRequest(string? WorkshopBrief);
public sealed record CreateCardGameRequest(string? Title, string? Brief, string? Mechanic, int CardCount);
