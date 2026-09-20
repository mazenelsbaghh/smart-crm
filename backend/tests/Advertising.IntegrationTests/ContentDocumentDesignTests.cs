using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Content.API;
using Modules.Content.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ContentDocumentDesignTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Finished_deck_accepts_a_new_slide_and_reordering_preserves_saved_designs()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var document = new ContentDocument { ProjectId = tenant.ProjectId, Title = "Finished deck", Status = ContentDocumentStatus.Ready, RequestedPageCount = 2 };
        var cover = new ContentDocumentPage { ProjectId = tenant.ProjectId, DocumentId = document.Id, PageIndex = 0,
            Title = "Cover", Status = ContentDocumentPageStatus.Ready, ImageObjectKey = "cover.png" };
        var original = new ContentDocumentPage { ProjectId = tenant.ProjectId, DocumentId = document.Id, PageIndex = 1,
            Body = "Original content", Status = ContentDocumentPageStatus.Ready, ImageObjectKey = "original.png" };
        db.AddRange(document, cover, original);
        await db.SaveChangesAsync();
        var jobs = new RecordingJobs();
        var controller = Controller(db, tenant, jobs);

        Assert.IsType<OkObjectResult>(await controller.AddPage(document.Id, new("New section", ""), CancellationToken.None));
        db.ChangeTracker.Clear();
        var added = await db.ContentDocumentPages.SingleAsync(page => page.DocumentId == document.Id && page.PageIndex == 2);
        Assert.Equal("New section", added.Title);
        Assert.Equal(ContentDocumentPageStatus.Planned, added.Status);
        var savedDocument = await db.ContentDocuments.SingleAsync(doc => doc.Id == document.Id);
        Assert.Equal(3, savedDocument.RequestedPageCount);
        Assert.Equal(ContentDocumentStatus.AwaitingDesign, savedDocument.Status);

        Assert.IsType<NoContentResult>(await controller.ReorderPages(document.Id, new([added.Id, original.Id, cover.Id]), CancellationToken.None));
        db.ChangeTracker.Clear();
        var reordered = await db.ContentDocumentPages.Where(page => page.DocumentId == document.Id).OrderBy(page => page.PageIndex).ToListAsync();
        Assert.Equal([added.Id, original.Id, cover.Id], reordered.Select(page => page.Id));
        Assert.Equal([0, 1, 2], reordered.Select(page => page.PageIndex));
        Assert.Equal("original.png", reordered[1].ImageObjectKey);
        Assert.Equal("cover.png", reordered[2].ImageObjectKey);
        Assert.All(reordered.Skip(1), page => Assert.Equal(ContentDocumentPageStatus.Ready, page.Status));
        Assert.Empty(jobs.Created);

        Assert.IsType<AcceptedResult>(await controller.RegenerateImage(document.Id, added.Id, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.Equal(ContentDocumentPageStatus.Queued, (await db.ContentDocumentPages.SingleAsync(page => page.Id == added.Id)).Status);
        Assert.Single(jobs.Created);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Inserting_a_slide_preserves_existing_designs_and_returns_its_saved_position(int position)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var document = new ContentDocument { ProjectId = tenant.ProjectId, Status = ContentDocumentStatus.Ready, RequestedPageCount = 2 };
        var pages = Enumerable.Range(0, 2).Select(index => new ContentDocumentPage
        { ProjectId = tenant.ProjectId, DocumentId = document.Id, PageIndex = index, Body = $"Original {index}",
            Status = ContentDocumentPageStatus.Ready, ImageObjectKey = $"original-{index}.png" }).ToArray();
        var originalDesigns = pages.Select(page => (page.Id, page.Body, page.ImageObjectKey, page.Status)).ToArray();
        db.Add(document);
        db.AddRange(pages);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await Controller(db, tenant, new RecordingJobs()).AddPage(document.Id,
            new("Inserted", "New content", position < pages.Length ? pages[position].Id : null), CancellationToken.None));

        db.ChangeTracker.Clear();
        var saved = await db.ContentDocumentPages.Where(page => page.DocumentId == document.Id).OrderBy(page => page.PageIndex).ToListAsync();
        var response = JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(position, response.GetProperty("pageIndex").GetInt32());
        Assert.Equal(response.GetProperty("id").GetGuid(), saved[position].Id);
        Assert.Equal("New content", saved[position].Body);
        Assert.Equal(ContentDocumentPageStatus.Planned, saved[position].Status);
        Assert.Equal([0, 1, 2], saved.Select(page => page.PageIndex));
        Assert.Equal(originalDesigns,
            saved.Where(page => page.Id != saved[position].Id).Select(page => (page.Id, page.Body, page.ImageObjectKey, page.Status)));
        Assert.Equal(3, (await db.ContentDocuments.SingleAsync(candidate => candidate.Id == document.Id)).RequestedPageCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inserting_before_a_missing_or_foreign_slide_leaves_the_deck_unchanged(bool foreign)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var document = new ContentDocument { ProjectId = tenant.ProjectId, Status = ContentDocumentStatus.Ready, RequestedPageCount = 1 };
        var original = new ContentDocumentPage { ProjectId = tenant.ProjectId, DocumentId = document.Id, Body = "Original" };
        var other = new ContentDocument { ProjectId = tenant.ProjectId };
        var anchor = new ContentDocumentPage { ProjectId = tenant.ProjectId, DocumentId = other.Id };
        db.AddRange(document, original, other, anchor);
        await db.SaveChangesAsync();

        Assert.IsType<ConflictObjectResult>(await Controller(db, tenant, new RecordingJobs()).AddPage(document.Id,
            new("Rejected", "", foreign ? anchor.Id : Guid.NewGuid()), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(original.Id, (await db.ContentDocumentPages.SingleAsync(page => page.DocumentId == document.Id)).Id);
        var savedDocument = await db.ContentDocuments.SingleAsync(candidate => candidate.Id == document.Id);
        Assert.Equal(1, savedDocument.RequestedPageCount);
        Assert.Equal(ContentDocumentStatus.Ready, savedDocument.Status);
    }

    [Fact]
    public async Task Concurrent_additions_get_distinct_positions_and_an_accurate_page_count()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var seed = postgres.CreateContext(tenant);
        await seed.Database.MigrateAsync();
        var document = new ContentDocument { ProjectId = tenant.ProjectId, Status = ContentDocumentStatus.Ready, RequestedPageCount = 1 };
        seed.AddRange(document, new ContentDocumentPage { ProjectId = tenant.ProjectId, DocumentId = document.Id, Title = "Cover" });
        await seed.SaveChangesAsync();
        await using var firstDb = postgres.CreateContext(tenant);
        await using var secondDb = postgres.CreateContext(tenant);
        var jobs = new RecordingJobs();

        var responses = await Task.WhenAll(
            Controller(firstDb, tenant, jobs).AddPage(document.Id, new("First addition", ""), CancellationToken.None),
            Controller(secondDb, tenant, jobs).AddPage(document.Id, new("Second addition", ""), CancellationToken.None));

        Assert.All(responses, response => Assert.IsType<OkObjectResult>(response));
        seed.ChangeTracker.Clear();
        var positions = await seed.ContentDocumentPages.Where(page => page.DocumentId == document.Id).OrderBy(page => page.PageIndex).Select(page => page.PageIndex).ToArrayAsync();
        Assert.Equal([0, 1, 2], positions);
        Assert.Equal(3, (await seed.ContentDocuments.SingleAsync(doc => doc.Id == document.Id)).RequestedPageCount);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("foreign")]
    public async Task Invalid_reordering_cannot_drop_or_import_a_slide(string scenario)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var document = new ContentDocument { ProjectId = tenant.ProjectId, Status = ContentDocumentStatus.Ready, RequestedPageCount = 2 };
        var pages = Enumerable.Range(0, 2).Select(index => new ContentDocumentPage
        { ProjectId = tenant.ProjectId, DocumentId = document.Id, PageIndex = index, Body = $"Slide {index}" }).ToArray();
        db.Add(document);
        db.AddRange(pages);
        await db.SaveChangesAsync();
        List<Guid> order = scenario switch
        {
            "duplicate" => [pages[0].Id, pages[0].Id],
            "missing" => [pages[0].Id],
            _ => [pages[0].Id, Guid.NewGuid()]
        };

        Assert.IsType<ConflictObjectResult>(await Controller(db, tenant, new RecordingJobs()).ReorderPages(document.Id, new(order), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(pages.Select(page => page.Id), await db.ContentDocumentPages.Where(page => page.DocumentId == document.Id).OrderBy(page => page.PageIndex).Select(page => page.Id).ToArrayAsync());
    }

    [Theory]
    [InlineData("generating")]
    [InlineData("other-project")]
    [InlineData("reader")]
    public async Task Slide_edits_require_an_idle_document_in_an_authorized_project(string scenario)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var document = new ContentDocument { ProjectId = tenant.ProjectId, Status = scenario == "generating" ? ContentDocumentStatus.GeneratingImages : ContentDocumentStatus.Ready, RequestedPageCount = 1 };
        var page = new ContentDocumentPage { ProjectId = tenant.ProjectId, DocumentId = document.Id, Title = "Untouched" };
        db.AddRange(document, page);
        await db.SaveChangesAsync();
        if (scenario == "other-project") tenant.SetProjectId(Guid.NewGuid());
        var controller = Controller(db, tenant, new RecordingJobs());
        if (scenario == "reader") controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("ProjectId", tenant.ProjectId.ToString()), new Claim(ClaimTypes.Role, "Agent")], "test"));
        var expectedType = scenario == "generating" ? typeof(ConflictObjectResult)
            : scenario == "other-project" ? typeof(NotFoundObjectResult) : typeof(ForbidResult);

        Assert.IsType(expectedType, await controller.AddPage(document.Id, new("New slide", ""), CancellationToken.None));
        Assert.IsType(expectedType, await controller.ReorderPages(document.Id, new([page.Id]), CancellationToken.None));
        Assert.IsType(expectedType, await controller.UpdatePage(document.Id, page.Id, new("Changed", ""), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal("Untouched", (await db.ContentDocumentPages.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == page.Id)).Title);
        Assert.Equal(1, await db.ContentDocumentPages.IgnoreQueryFilters().CountAsync(candidate => candidate.DocumentId == document.Id));
    }

    [Fact]
    public async Task Editing_a_finished_slide_keeps_its_image_but_requires_a_new_design()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var document = new ContentDocument { ProjectId = tenant.ProjectId, Status = ContentDocumentStatus.Ready,
            RequestedPageCount = 1, CompletedAtUtc = DateTime.UtcNow };
        var page = new ContentDocumentPage { ProjectId = tenant.ProjectId, DocumentId = document.Id,
            Title = "Before", ImageObjectKey = "saved.png", Status = ContentDocumentPageStatus.Ready };
        db.AddRange(document, page);
        await db.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await Controller(db, tenant, new RecordingJobs())
            .UpdatePage(document.Id, page.Id, new("After", "Updated content"), CancellationToken.None));

        db.ChangeTracker.Clear();
        var saved = await db.ContentDocumentPages.SingleAsync(candidate => candidate.Id == page.Id);
        Assert.Equal("After", saved.Title);
        Assert.Equal("Updated content", saved.Body);
        Assert.Equal("saved.png", saved.ImageObjectKey);
        Assert.Equal(ContentDocumentPageStatus.Planned, saved.Status);
        var savedDocument = await db.ContentDocuments.SingleAsync(candidate => candidate.Id == document.Id);
        Assert.Equal(ContentDocumentStatus.AwaitingDesign, savedDocument.Status);
        Assert.Null(savedDocument.CompletedAtUtc);
    }

    [Fact]
    public async Task Adding_past_the_deck_limit_leaves_the_finished_deck_unchanged()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var document = new ContentDocument { ProjectId = tenant.ProjectId, Status = ContentDocumentStatus.Ready, RequestedPageCount = 200 };
        db.Add(document);
        db.AddRange(Enumerable.Range(0, 200).Select(index => new ContentDocumentPage
        { ProjectId = tenant.ProjectId, DocumentId = document.Id, PageIndex = index, Title = $"Slide {index}" }));
        await db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await Controller(db, tenant, new RecordingJobs())
            .AddPage(document.Id, new("Overflow", ""), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(200, await db.ContentDocumentPages.CountAsync(page => page.DocumentId == document.Id));
        Assert.Equal(ContentDocumentStatus.Ready, (await db.ContentDocuments.SingleAsync(candidate => candidate.Id == document.Id)).Status);
    }

    [Fact]
    public async Task Concurrent_slide_design_requests_queue_one_image_and_preserve_the_other_slide()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        await using var seed = postgres.CreateContext(tenant);
        await seed.Database.MigrateAsync();
        var document = new ContentDocument { ProjectId = projectId, Title = "Session 5", Status = ContentDocumentStatus.AwaitingDesign, RequestedPageCount = 2 };
        var pages = Enumerable.Range(0, 2).Select(index => new ContentDocumentPage
        { ProjectId = projectId, DocumentId = document.Id, PageIndex = index, Body = $"Topic {index}", Status = ContentDocumentPageStatus.Planned }).ToArray();
        seed.Add(document);
        seed.AddRange(pages);
        await seed.SaveChangesAsync();
        var jobs = new RecordingJobs();
        await using var firstDb = postgres.CreateContext(tenant);
        await using var secondDb = postgres.CreateContext(tenant);
        var first = Controller(firstDb, tenant, jobs);
        var second = Controller(secondDb, tenant, jobs);

        var responses = await Task.WhenAll(
            first.RegenerateImage(document.Id, pages[0].Id, CancellationToken.None),
            second.RegenerateImage(document.Id, pages[1].Id, CancellationToken.None));

        Assert.Single(responses.OfType<AcceptedResult>());
        Assert.Single(responses.OfType<ConflictObjectResult>());
        Assert.Single(jobs.Created);
        seed.ChangeTracker.Clear();
        var savedPages = await seed.ContentDocumentPages.Where(page => page.DocumentId == document.Id).ToListAsync();
        Assert.Single(savedPages, page => page.Status == ContentDocumentPageStatus.Queued);
        Assert.Single(savedPages, page => page.Status == ContentDocumentPageStatus.Planned);
        Assert.Equal(ContentDocumentStatus.GeneratingImages, (await seed.ContentDocuments.SingleAsync(doc => doc.Id == document.Id)).Status);

        var otherTenant = new TenantContext();
        otherTenant.SetProjectId(Guid.NewGuid());
        await using var otherDb = postgres.CreateContext(otherTenant);
        Assert.IsType<NotFoundObjectResult>(await Controller(otherDb, otherTenant, jobs)
            .RegenerateImage(document.Id, pages[0].Id, CancellationToken.None));
        Assert.Single(jobs.Created);
    }

    [Fact]
    public async Task Queue_failure_leaves_the_slide_retryable_and_keeps_its_saved_image()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var document = new ContentDocument { ProjectId = tenant.ProjectId, Title = "Session 4", Status = ContentDocumentStatus.Ready, RequestedPageCount = 1 };
        var page = new ContentDocumentPage { ProjectId = tenant.ProjectId, DocumentId = document.Id,
            Title = "Session 4", Status = ContentDocumentPageStatus.Ready, ImageObjectKey = "saved-slide.png" };
        db.AddRange(document, page);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Controller(db, tenant, new UnavailableJobs())
            .RegenerateImage(document.Id, page.Id, CancellationToken.None));

        db.ChangeTracker.Clear();
        var saved = await db.ContentDocumentPages.SingleAsync(candidate => candidate.Id == page.Id);
        Assert.Equal(ContentDocumentPageStatus.ImageFailed, saved.Status);
        Assert.Equal("saved-slide.png", saved.ImageObjectKey);
        Assert.NotEmpty(saved.Error!);
        Assert.Equal(ContentDocumentStatus.AwaitingDesign, (await db.ContentDocuments.SingleAsync(doc => doc.Id == document.Id)).Status);
    }

    private static ContentDocumentsController Controller(AppDbContext db, TenantContext tenant, IBackgroundJobClient jobs) =>
        new(db, tenant, new ProjectAuthorizationService(), jobs, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("ProjectId", tenant.ProjectId.ToString()), new Claim(ClaimTypes.Role, "Owner")], "test")) } }
        };

    private sealed class RecordingJobs : IBackgroundJobClient
    {
        public ConcurrentBag<Job> Created { get; } = [];
        public string Create(Job job, IState state) { Created.Add(job); return Guid.NewGuid().ToString(); }
        public bool ChangeState(string jobId, IState state, string? expectedState) => throw new NotSupportedException();
    }

    private sealed class UnavailableJobs : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => throw new InvalidOperationException("Queue unavailable");
        public bool ChangeState(string jobId, IState state, string? expectedState) => throw new NotSupportedException();
    }
}
