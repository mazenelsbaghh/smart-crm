using System.Security.Claims;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Content.API;
using Modules.Content.Domain;
using Modules.Content.Services;
using Modules.Projects.Domain;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ContentDocumentBatchTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("Presentation", false)]
    [InlineData("A4", true)]
    public async Task One_count_creates_separate_reviewed_files_with_named_covers_and_retryable_queue_failures(string kind, bool failFirstJob)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        db.AddRange(new Project { Id = tenant.ProjectId, Name = "Session files" },
            new ProjectSettings { ProjectId = tenant.ProjectId, GeminiApiKey = "test-key" },
            new ContentAutomationSettings { ProjectId = tenant.ProjectId, LogoObjectKey = "logo.png", StylePrompt = "Saved brand" });
        await db.SaveChangesAsync();
        const string source = "مقدمة الدورة كاملة\nSession 11: البداية\nشرح السيشن الأول كاملًا\nمثال السيشن الأول كاملًا\nسيشن ١٢: التطبيق\nشرح السيشن الثاني كاملًا\nمثال السيشن الثاني كاملًا";
        var gemini = new OutlineResponses(
            """{"pages":[{"blocks":[{"type":"paragraph","ids":[0,1]}]},{"blocks":[{"type":"paragraph","ids":[2]}]},{"blocks":[{"type":"paragraph","ids":[3]}]},{"blocks":[{"type":"paragraph","ids":[4,5,6]}]}]}""",
            """{"pages":[{"blocks":[{"type":"heading","ids":[0]},{"type":"paragraph","ids":[1,2]}]},{"blocks":[{"type":"paragraph","ids":[3]}]},{"blocks":[{"type":"heading","ids":[4]},{"type":"paragraph","ids":[5]}]},{"blocks":[{"type":"paragraph","ids":[6]}]}]}""");
        var planning = new ContentDocumentPlanningService(db, gemini, new PlainSecrets(),
            new EphemeralDataProtectionProvider(), NullLogger<ContentDocumentPlanningService>.Instance);
        var jobs = new BatchJobs(failFirstJob);
        var controller = new ContentDocumentsController(db, tenant, new ProjectAuthorizationService(), jobs, null!, planning)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("ProjectId", tenant.ProjectId.ToString()), new Claim(ClaimTypes.Role, "Owner")], "test")) } }
        };
        var request = new CreateContentDocumentRequest(kind, source, PagesPerSession: 3);
        var suggestion = Assert.IsType<ContentDocumentSessions.Suggestion>(Assert.IsType<OkObjectResult>(controller.SuggestPageCount(request)).Value);
        Assert.Equal(["Session 11: البداية", "سيشن ١٢: التطبيق"], suggestion.FileSections!.Select(section => section.Title));
        var preview = Assert.IsType<ContentDocumentPreview.Preview>(Assert.IsType<OkObjectResult>(
            await controller.Preview(request, CancellationToken.None)).Value);
        Assert.Equal(6, preview.PageCount);
        Assert.Empty(preview.Pages);
        Assert.Equal(2, gemini.Calls);
        Assert.Equal(2, preview.Documents!.Count);
        Assert.All(preview.Documents, file =>
        {
            Assert.Equal(3, file.PageCount);
            Assert.Equal(file.Title, file.Pages[0].Title);
            Assert.Equal([0, 1, 2], file.Pages.Select(page => page.PageIndex));
        });
        Assert.Equal(source.Split('\n').Order(), preview.Documents.SelectMany(file => file.Pages)
            .SelectMany(page => page.Blocks).SelectMany(block => block.Items).Order());
        Assert.Empty(jobs.Created);
        var approved = request with { PreviewFingerprint = preview.Fingerprint };
        foreach (var stale in new[] { approved with { PagesPerSession = 4 }, approved with { Content = source + "تعديل" } })
            Assert.IsType<BadRequestObjectResult>(await controller.Create(stale, CancellationToken.None));
        Assert.Empty(await db.ContentDocuments.ToListAsync());

        var created = Assert.IsType<AcceptedResult>(await controller.Create(approved, CancellationToken.None));
        var ids = JsonSerializer.SerializeToElement(created.Value).GetProperty("ids").EnumerateArray().Select(id => id.GetGuid()).ToArray();
        db.ChangeTracker.Clear();
        var documents = await db.ContentDocuments.Where(document => ids.Contains(document.Id)).ToListAsync();
        Assert.Equal(2, documents.Count);
        foreach (var file in preview.Documents)
        {
            var document = Assert.Single(documents, document => document.Title == file.Title);
            var pages = await db.ContentDocumentPages.Where(page => page.DocumentId == document.Id).OrderBy(page => page.PageIndex).ToListAsync();
            Assert.Equal(Enum.Parse<ContentDocumentKind>(kind), document.Kind);
            Assert.Equal(3, document.RequestedPageCount);
            Assert.Equal("Saved brand", document.BrandStylePrompt);
            Assert.Equal("logo.png", document.BrandLogoObjectKey);
            Assert.Equal(file.Pages.Select(page => (page.Title, page.Body)), pages.Select(page => (page.Title, page.Body)));
            Assert.Equal(string.Join("\n\n", file.Pages.Skip(1).Select(page => page.Body)), document.SourceContent);
        }
        Assert.Equal(failFirstJob ? 1 : 2, jobs.Created.Count);
        if (failFirstJob)
        {
            var retryable = Assert.Single(documents, document => document.Status == ContentDocumentStatus.AwaitingDesign);
            Assert.NotEmpty(retryable.Error!);
            Assert.IsType<AcceptedResult>(await controller.RegenerateImages(retryable.Id, CancellationToken.None));
        }
        else Assert.All(documents, document => Assert.Equal(ContentDocumentStatus.Planning, document.Status));
    }

    private sealed class BatchJobs(bool failFirst) : IBackgroundJobClient
    {
        public List<Job> Created { get; } = [];
        private bool _failNext = failFirst;
        public string Create(Job job, IState state)
        {
            if (_failNext) { _failNext = false; throw new InvalidOperationException("Queue unavailable"); }
            Created.Add(job);
            return Guid.NewGuid().ToString();
        }
        public bool ChangeState(string jobId, IState state, string? expectedState) => throw new NotSupportedException();
    }

    private sealed class PlainSecrets : IProjectSecretVault
    {
        public bool IsProtected(string? storedValue) => false;
        public string Protect(Guid projectId, string secret) => secret;
        public string? Unprotect(Guid projectId, string? storedValue) => storedValue;
    }

    private sealed class OutlineResponses(params string[] responses) : IGeminiClient
    {
        public int Calls { get; private set; }
        public Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => Task.FromResult(responses[Calls++]);
        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string apiKeyOverride = null!) => throw new NotSupportedException();
    }
}
