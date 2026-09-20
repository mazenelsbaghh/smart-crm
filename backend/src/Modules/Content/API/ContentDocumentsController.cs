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
[Route("api/content/documents")]
public sealed class ContentDocumentsController(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    IProjectAuthorizationService authorization,
    IBackgroundJobClient jobs,
    IObjectStorage objectStorage,
    ContentDocumentPlanningService planning) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var documents = await dbContext.ContentDocuments.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Title)
            .Take(50)
            .ToListAsync(cancellationToken);
        return Ok(new { documents = documents.Select(document => DocumentSummary(document)) });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var document = await FindDocument(projectId, id, cancellationToken);
        if (document is null) return NotFound(new { error = "المستند غير موجود." });
        var pages = await dbContext.ContentDocumentPages.IgnoreQueryFilters()
            .Where(page => page.ProjectId == projectId && page.DocumentId == id)
            .OrderBy(page => page.PageIndex)
            .ToListAsync(cancellationToken);
        return Ok(new
        {
            document = DocumentSummary(
                document,
                ContentDocumentAssetRoutes.Logo(document.Id, document.UpdatedAt)),
            pages = pages.Select(PageResponse)
        });
    }

    [HttpGet("{id:guid}/logo")]
    public async Task<IActionResult> GetDocumentLogo(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var document = await FindDocument(projectId, id, cancellationToken);
        if (document is null || string.IsNullOrWhiteSpace(document.BrandLogoObjectKey)) return NotFound();

        var stream = await objectStorage.DownloadAsync(document.BrandLogoObjectKey, cancellationToken);
        Response.Headers.CacheControl = "private, max-age=604800, immutable";
        return File(
            stream,
            ContentDocumentAssetRoutes.MimeFromKey(document.BrandLogoObjectKey),
            enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/pages/{pageId:guid}/image")]
    public async Task<IActionResult> GetPageImage(
        Guid id,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var page = await dbContext.ContentDocumentPages.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId
                && candidate.DocumentId == id
                && candidate.Id == pageId,
                cancellationToken);
        if (string.IsNullOrWhiteSpace(page?.ImageObjectKey)) return NotFound();

        var stream = await objectStorage.DownloadAsync(page.ImageObjectKey, cancellationToken);
        Response.Headers.CacheControl = "private, max-age=604800, immutable";
        return File(stream, page.ImageMimeType, enableRangeProcessing: true);
    }

    [HttpPost("suggest-page-count")]
    public IActionResult SuggestPageCount(CreateContentDocumentRequest request)
    {
        if (!authorization.CanManageProject(User, ActiveProjectId())) return Forbid();
        if (!ValidContent(request)) return BadRequest(new { error = "اختر نوع الملف واكتب محتوى بين 80 و60,000 حرف." });
        var kind = Enum.Parse<ContentDocumentKind>(request.Kind!, true);
        try
        {
            var suggestion = ContentDocumentSessions.Suggest(request.Content!, kind);
            return Ok(suggestion.SessionCount == 0 ? suggestion : suggestion with
            {
                FileSections = ContentDocumentSessions.Suggest(ContentDocumentSessions.ForSeparateFiles(request.Content!), kind).Sections
            });
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(CreateContentDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!authorization.CanManageProject(User, ActiveProjectId())) return Forbid();
        if (!ValidContent(request)) return BadRequest(new { error = "اختر نوع الملف واكتب محتوى بين 80 و60,000 حرف." });
        try
        {
            return Ok(await planning.PreviewAsync(ActiveProjectId(), PreviewInput(request), cancellationToken));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return StatusCode(502, new { error = exception.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateContentDocumentRequest request, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        if (!ValidContent(request)) return BadRequest(new { error = "اختر نوع الملف واكتب محتوى بين 80 و60,000 حرف." });
        var content = request.Content!;
        var kind = Enum.Parse<ContentDocumentKind>(request.Kind!, true);
        ContentDocumentPreview.Preview preview;
        try { preview = planning.Restore(projectId, PreviewInput(request), request.PreviewFingerprint); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }

        var brand = await dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.ProjectId == projectId, cancellationToken);
        if (brand is null || string.IsNullOrWhiteSpace(brand.LogoObjectKey))
            return BadRequest(new { error = "ارفع اللوجو في الصور والمنشورات أولاً حتى نستخدم نفس الهوية." });
        var projectAi = await dbContext.ProjectSettings.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId)
            .Select(item => item.GeminiApiKey)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(projectAi)) return BadRequest(new { error = "أضف مفتاح Gemini في إعدادات المشروع أولاً." });

        var files = preview.Documents ?? [new ContentDocumentPreview.PreviewFile(0, preview.Title, preview.Pages)];
        var documents = files.Select(file => new ContentDocument
        {
            ProjectId = projectId,
            Kind = kind,
            Status = ContentDocumentStatus.Planning,
            Title = file.Title,
            SourceContent = preview.Documents is null ? content : string.Join("\n\n", file.Pages.Skip(1).Select(page => page.Body)),
            RequestedPageCount = file.PageCount,
            BrandLogoObjectKey = brand.LogoObjectKey,
            BrandColorsJson = brand.BrandColorsJson,
            BrandStylePrompt = brand.StylePrompt
        }).ToList();
        dbContext.ContentDocuments.AddRange(documents);
        foreach (var (document, file) in documents.Zip(files))
            dbContext.ContentDocumentPages.AddRange(ContentDocumentPreview.CreatePages(document, file.Pages));
        await dbContext.SaveChangesAsync(cancellationToken);
        var queueFailures = await QueueDocuments(documents);
        var message = preview.Documents is null ? $"بدأ تصميم {preview.PageCount} صفحة بالمحتوى الكامل من دون تلخيص."
            : $"بدأ تصميم {documents.Count} ملفات مستقلة، كل ملف {request.PagesPerSession} صفحة شامل غلاف باسم السيشن.";
        if (queueFailures > 0) message = $"اتحفظت الملفات كلها. تعذر بدء تصميم {queueFailures} ملف؛ افتح الملف واضغط «صمّم السلايدات».";
        return Accepted(new { id = documents[0].Id, ids = documents.Select(document => document.Id), status = documents[0].Status.ToString(), message });
    }

    private async Task<int> QueueDocuments(List<ContentDocument> documents)
    {
        var failures = 0;
        foreach (var document in documents)
        {
            try { jobs.Enqueue<ContentDocumentJob>(job => job.GenerateAsync(document.ProjectId, document.Id, CancellationToken.None)); }
            catch (Exception)
            {
                failures++;
                document.Status = ContentDocumentStatus.AwaitingDesign;
                document.Error = "تعذر بدء التصميم. المحتوى محفوظ ويمكنك بدء التصميم مرة أخرى.";
            }
        }
        if (failures > 0) await dbContext.SaveChangesAsync(CancellationToken.None);
        return failures;
    }

    [HttpPost("{id:guid}/pages")]
    public async Task<IActionResult> AddPage(Guid id, AddContentDocumentPageRequest request, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        if (!ValidPageText(request.Title, request.Body)) return BadRequest(new { error = "اكتب عنوانًا أو نصًا للسلايد، بحد أقصى 300 حرف للعنوان و4,000 للنص." });
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var document = await LockDocument(projectId, id, cancellationToken);
        if (document is null) return NotFound(new { error = "المستند غير موجود." });
        if (IsGenerating(document)) return Conflict(new { error = "انتظر اكتمال التصميم الجاري قبل إضافة سلايد." });
        var pages = await DocumentPages(projectId, id).OrderBy(page => page.PageIndex).ToListAsync(cancellationToken);
        if (pages.Count >= 200) return BadRequest(new { error = "الحد الأقصى للعرض 200 سلايد." });
        var insertionIndex = request.BeforePageId is null ? pages.Count : pages.FindIndex(page => page.Id == request.BeforePageId);
        if (insertionIndex < 0) return Conflict(new { error = "السلايد المحدد للمكان مش موجود في العرض. حدّث العرض واختار المكان تاني." });
        var page = new ContentDocumentPage
        {
            ProjectId = projectId, DocumentId = id, PageIndex = pages.Count,
            Title = request.Title?.Trim() ?? string.Empty, Body = request.Body ?? string.Empty
        };
        dbContext.ContentDocumentPages.Add(page);
        document.RequestedPageCount = pages.Count + 1;
        MarkAwaitingDesign(document);
        if (insertionIndex < pages.Count)
        {
            pages.Insert(insertionIndex, page);
            await SavePageOrder(pages, pages.Select(candidate => candidate.Id).ToList(), cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new { id = page.Id, pageIndex = page.PageIndex, message = $"تمت إضافة السلايد {page.PageIndex + 1}." });
    }

    [HttpPut("{id:guid}/pages/order")]
    public async Task<IActionResult> ReorderPages(Guid id, ReorderContentDocumentPagesRequest request, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        if (request.PageIds is not { Count: > 0 and <= 200 })
            return BadRequest(new { error = "أرسل ترتيب السلايدات كاملًا." });
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var document = await LockDocument(projectId, id, cancellationToken);
        if (document is null) return NotFound(new { error = "المستند غير موجود." });
        if (IsGenerating(document)) return Conflict(new { error = "انتظر اكتمال التصميم الجاري قبل تغيير الترتيب." });
        var pages = await DocumentPages(projectId, id).ToListAsync(cancellationToken);
        if (request.PageIds.Count != pages.Count || !request.PageIds.ToHashSet().SetEquals(pages.Select(page => page.Id)))
            return Conflict(new { error = "السلايدات اتغيرت. حدّث العرض ثم حاول تغيير الترتيب مرة أخرى." });
        await SavePageOrder(pages, request.PageIds, cancellationToken);
        document.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/pages/{pageId:guid}")]
    public async Task<IActionResult> UpdatePage(Guid id, Guid pageId, UpdateContentDocumentPageRequest request, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var document = await LockDocument(projectId, id, cancellationToken);
        if (document is null) return NotFound(new { error = "المستند غير موجود." });
        if (IsGenerating(document)) return Conflict(new { error = "انتظر اكتمال التصميم الجاري قبل تعديل السلايد." });
        var page = await dbContext.ContentDocumentPages.IgnoreQueryFilters().SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.DocumentId == id && item.Id == pageId, cancellationToken);
        if (page is null) return NotFound(new { error = "الصفحة غير موجودة." });
        var title = request.Title?.Trim() ?? string.Empty;
        var body = request.Body ?? string.Empty;
        if (!ValidPageText(request.Title, request.Body))
            return BadRequest(new { error = "راجع طول عنوان ونص الصفحة." });
        page.Title = title;
        page.Body = body;
        page.Status = ContentDocumentPageStatus.Planned;
        page.Error = null;
        page.UpdatedAt = DateTime.UtcNow;
        MarkAwaitingDesign(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/pages/{pageId:guid}/regenerate-image")]
    public async Task<IActionResult> RegenerateImage(Guid id, Guid pageId, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        var page = await dbContext.ContentDocumentPages.IgnoreQueryFilters().SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.DocumentId == id && item.Id == pageId, cancellationToken);
        if (page is null) return NotFound(new { error = "الصفحة غير موجودة." });
        if (page.Status is ContentDocumentPageStatus.GeneratingImage or ContentDocumentPageStatus.Queued)
            return Conflict(new { error = "صورة الصفحة قيد التوليد بالفعل." });
        if (!await ClaimDocumentGenerationAsync(projectId, id, cancellationToken))
            return Conflict(new { error = "انتظر اكتمال التصميم الجاري في هذا العرض، ثم صمّم السلايد التالي." });
        page.Status = ContentDocumentPageStatus.Queued;
        page.Error = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        try { jobs.Enqueue<ContentDocumentJob>(job => job.RegenerateImageAsync(projectId, pageId, CancellationToken.None)); }
        catch
        {
            await ReleaseFailedQueueAsync(projectId, id, CancellationToken.None);
            throw;
        }
        return Accepted(new { message = $"تمت جدولة تصميم السلايد {page.PageIndex + 1}." });
    }

    [HttpPost("{id:guid}/regenerate-images")]
    public async Task<IActionResult> RegenerateImages(Guid id, CancellationToken cancellationToken)
    {
        var projectId = ActiveProjectId();
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        var document = await FindDocument(projectId, id, cancellationToken);
        if (document is null) return NotFound(new { error = "المستند غير موجود." });
        if (!await ClaimDocumentGenerationAsync(projectId, id, cancellationToken))
            return Conflict(new { error = "انتظر اكتمال التصميم الجاري أولاً." });
        await dbContext.ContentDocumentPages.IgnoreQueryFilters()
            .Where(page => page.ProjectId == projectId && page.DocumentId == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(page => page.Status, ContentDocumentPageStatus.Queued)
                .SetProperty(page => page.Error, (string?)null)
                .SetProperty(page => page.UpdatedAt, DateTime.UtcNow), cancellationToken);
        try { jobs.Enqueue<ContentDocumentJob>(job => job.RegenerateImagesAsync(projectId, id, CancellationToken.None)); }
        catch
        {
            await ReleaseFailedQueueAsync(projectId, id, CancellationToken.None);
            throw;
        }
        return Accepted(new { message = "بدأ إعادة تصميم كل الخلفيات من دون تغيير النص." });
    }

    private IQueryable<ContentDocumentPage> DocumentPages(Guid projectId, Guid id) =>
        dbContext.ContentDocumentPages.IgnoreQueryFilters().Where(page => page.ProjectId == projectId && page.DocumentId == id);

    private Task<ContentDocument?> LockDocument(Guid projectId, Guid id, CancellationToken cancellationToken) =>
        dbContext.ContentDocuments.FromSqlInterpolated($"""
            SELECT * FROM "ContentDocuments" WHERE "ProjectId" = {projectId} AND "Id" = {id} FOR UPDATE
            """).IgnoreQueryFilters().SingleOrDefaultAsync(cancellationToken);

    private async Task SavePageOrder(List<ContentDocumentPage> pages, List<Guid> pageIds, CancellationToken cancellationToken)
    {
        // Vacate the unique document/page-index slots before assigning the final permutation.
        foreach (var page in pages) page.PageIndex = -page.PageIndex - 1;
        await dbContext.SaveChangesAsync(cancellationToken);
        var pagesById = pages.ToDictionary(page => page.Id);
        for (var index = 0; index < pageIds.Count; index++) pagesById[pageIds[index]].PageIndex = index;
    }

    private static bool ValidPageText(string? title, string? body) =>
        (title?.Length ?? 0) <= 300 && (body?.Length ?? 0) <= 4_000
        && (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(body));

    private static bool IsGenerating(ContentDocument document) =>
        document.Status is ContentDocumentStatus.Planning or ContentDocumentStatus.GeneratingImages;

    private static void MarkAwaitingDesign(ContentDocument document)
    {
        document.Status = ContentDocumentStatus.AwaitingDesign;
        document.Error = null;
        document.CompletedAtUtc = null;
        document.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<bool> ClaimDocumentGenerationAsync(Guid projectId, Guid id, CancellationToken cancellationToken) =>
        await dbContext.ContentDocuments.IgnoreQueryFilters()
            .Where(document => document.ProjectId == projectId && document.Id == id
                && (document.Status == ContentDocumentStatus.Ready || document.Status == ContentDocumentStatus.AwaitingDesign
                    || document.Status == ContentDocumentStatus.Failed))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(document => document.Status, ContentDocumentStatus.GeneratingImages)
                .SetProperty(document => document.Error, (string?)null)
                .SetProperty(document => document.CompletedAtUtc, (DateTime?)null)
                .SetProperty(document => document.UpdatedAt, DateTime.UtcNow), cancellationToken) == 1;

    private async Task ReleaseFailedQueueAsync(Guid projectId, Guid id, CancellationToken cancellationToken)
    {
        await dbContext.ContentDocumentPages.IgnoreQueryFilters()
            .Where(page => page.ProjectId == projectId && page.DocumentId == id && page.Status == ContentDocumentPageStatus.Queued)
            .ExecuteUpdateAsync(setters => setters.SetProperty(page => page.Status, ContentDocumentPageStatus.ImageFailed)
                .SetProperty(page => page.Error, "تعذرت جدولة التصميم. جرّب مرة أخرى."), cancellationToken);
        await dbContext.ContentDocuments.IgnoreQueryFilters()
            .Where(document => document.ProjectId == projectId && document.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(document => document.Status, ContentDocumentStatus.AwaitingDesign), cancellationToken);
    }

    private static object PageResponse(ContentDocumentPage page) => new
    {
        id = page.Id,
        pageIndex = page.PageIndex,
        status = page.Status.ToString(),
        title = page.Title,
        body = page.Body,
        error = page.Error,
        imageUrl = string.IsNullOrWhiteSpace(page.ImageObjectKey)
            ? null
            : ContentDocumentAssetRoutes.PageImage(page.DocumentId, page.Id, page.UpdatedAt)
    };

    private static object DocumentSummary(ContentDocument document, string? logoUrl = null) => new
    {
        id = document.Id,
        kind = document.Kind.ToString(),
        status = document.Status.ToString(),
        title = document.Title,
        requestedPageCount = document.RequestedPageCount,
        error = document.Error,
        logoUrl,
        createdAt = document.CreatedAt,
        updatedAt = document.UpdatedAt
    };

    private Task<ContentDocument?> FindDocument(Guid projectId, Guid id, CancellationToken cancellationToken) =>
        dbContext.ContentDocuments.IgnoreQueryFilters().SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.Id == id, cancellationToken);

    private static ContentDocumentPlanningService.PreviewInput PreviewInput(CreateContentDocumentRequest request) =>
        new(request.Content!, Enum.Parse<ContentDocumentKind>(request.Kind!, true), request.PageCount, request.CoverTitle, request.SessionRanges, request.PagesPerSession);

    private static bool ValidContent(CreateContentDocumentRequest request) =>
        request.Content is { Length: <= 60_000 } && request.Content.Trim().Length >= 80
        && Enum.TryParse<ContentDocumentKind>(request.Kind, true, out var kind) && Enum.IsDefined(kind);

    private Guid ActiveProjectId() => tenantContext.ProjectId != Guid.Empty
        ? tenantContext.ProjectId
        : throw new UnauthorizedAccessException("Active project context is required.");
}

public sealed record CreateContentDocumentRequest(string? Kind, string? Content, int PageCount = 0,
    string? CoverTitle = null, string? PreviewFingerprint = null, List<ContentDocumentSessionRange>? SessionRanges = null, int? PagesPerSession = null);
public sealed record UpdateContentDocumentPageRequest(string? Title, string? Body);
public sealed record AddContentDocumentPageRequest(string? Title, string? Body, Guid? BeforePageId = null);
public sealed record ReorderContentDocumentPagesRequest(List<Guid>? PageIds);

internal static class ContentDocumentAssetRoutes
{
    internal static string Logo(Guid documentId, DateTime updatedAt) =>
        $"/api/content/documents/{documentId:D}/logo?v={updatedAt.Ticks}";

    internal static string PageImage(Guid documentId, Guid pageId, DateTime updatedAt) =>
        $"/api/content/documents/{documentId:D}/pages/{pageId:D}/image?v={updatedAt.Ticks}";

    internal static string MimeFromKey(string objectKey) =>
        objectKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
        : objectKey.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
        : "image/jpeg";
}
