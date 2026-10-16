using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text.Json;
using Modules.Content.API;
using Modules.Content.Services;
using Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Modules.Content.Domain;
using Modules.Projects.Domain;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ContentImageProviderPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Image_provider_and_encrypted_key_round_trip_without_changing_other_projects()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var otherProjectId = Guid.NewGuid();
        var vault = new ProjectSecretVault(new EphemeralDataProtectionProvider());
        var settings = new ContentAutomationSettings
        {
            ProjectId = tenant.ProjectId,
            ImageProvider = "OpenAI",
            OpenAiImageModel = OpenAiImageClient.FastModel,
            OpenAiImageApiKey = vault.Protect(tenant.ProjectId, "image-api-test-key")
        };
        db.AddRange(new Project { Id = tenant.ProjectId, Name = "Image selection" },
            new Project { Id = otherProjectId, Name = "Other image selection" },
            settings, new ContentAutomationSettings { ProjectId = otherProjectId });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.ContentAutomationSettings.SingleAsync();

        Assert.Equal("OpenAI", stored.ImageProvider);
        Assert.Equal(OpenAiImageClient.FastModel, stored.OpenAiImageModel);
        Assert.StartsWith("v1:", stored.OpenAiImageApiKey);
        Assert.Equal("image-api-test-key", vault.Unprotect(tenant.ProjectId, stored.OpenAiImageApiKey));
        var otherSettings = await db.ContentAutomationSettings.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == otherProjectId);
        Assert.Equal("Gemini", otherSettings.ImageProvider);
        Assert.Equal(OpenAiImageClient.HighestQualityModel, otherSettings.OpenAiImageModel);
        Assert.Empty(otherSettings.OpenAiImageApiKey);
    }
    [Fact]
    public async Task Paper_frame_saves_a_project_key_and_pins_openai_without_changing_post_selection()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var protection = new EphemeralDataProtectionProvider();
        var vault = new ProjectSecretVault(protection);
        db.AddRange(new Project { Id = tenant.ProjectId, Name = "Paper image selection" },
            new ContentAutomationSettings { ProjectId = tenant.ProjectId, ImageProvider = "Gemini", IsEnabled = true, HasApprovedStyle = true });
        await db.SaveChangesAsync();
        using var handler = new PaperImageResponse();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://image.test/") };
        var images = new ContentImageClient(new GeminiImageClient(http), new OpenAiImageClient(http), vault);
        var controller = new ContentPapersController(db, tenant, new ProjectAuthorizationService(), vault, images, new PaperStorage(), protection)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.Role, "Owner"), new Claim("ProjectId", tenant.ProjectId.ToString())], "test")) } }
        };

        var response = Assert.IsType<OkObjectResult>(await controller.CreateFrame(new PaperFrameRequest
            { StyleId = "ribbons", PaletteId = "mint", ImageProvider = "OpenAI", OpenAiImageApiKey = "paper-image-key", OpenAiImageModel = OpenAiImageClient.FastModel }, CancellationToken.None));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        var design = ContentPaperDesign.Restore(protection, tenant.ProjectId, json.RootElement.GetProperty("token").GetString());
        db.ChangeTracker.Clear();
        var stored = await db.ContentAutomationSettings.SingleAsync();
        Assert.Equal("OpenAI", design.ImageProvider);
        Assert.Equal(OpenAiImageClient.FastModel, design.OpenAiImageModel);
        Assert.Equal("Gemini", stored.ImageProvider);
        Assert.True(stored.IsEnabled);
        Assert.True(stored.HasApprovedStyle);
        Assert.StartsWith("v1:", stored.OpenAiImageApiKey);
        Assert.Equal("paper-image-key", vault.Unprotect(tenant.ProjectId, stored.OpenAiImageApiKey));
        Assert.DoesNotContain("paper-image-key", JsonSerializer.Serialize(response.Value));
        var preview = Assert.IsType<OkObjectResult>(await controller.PreviewPage(new PaperPagePreviewRequest(
            json.RootElement.GetProperty("token").GetString()!, "شرح كامل في ورقة واحدة"), CancellationToken.None));
        var imagePreview = Assert.IsType<ContentImageTrialResponse>(preview.Value);
        Assert.Equal(OpenAiImageClient.FastModel, imagePreview.Model);
        Assert.Equal("768x1024", imagePreview.Size);
        Assert.Empty(await db.ContentDocuments.ToListAsync());
        Assert.Empty(await db.ContentPosts.ToListAsync());
        Assert.IsType<BadRequestObjectResult>(await controller.PreviewPage(new PaperPagePreviewRequest(
            new ContentPaperDesign(Guid.NewGuid(), "frame.png", null, "", "", []).Protect(protection), "شرح"), CancellationToken.None));
        var encryptedKey = stored.OpenAiImageApiKey;

        Assert.IsType<OkObjectResult>(await controller.CreateFrame(new PaperFrameRequest
            { StyleId = "ribbons", PaletteId = "mint", ImageProvider = "OpenAI" }, CancellationToken.None));
        Assert.Equal(encryptedKey, stored.OpenAiImageApiKey);
        Assert.Equal("Bearer paper-image-key", handler.Authorization);
    }

    private sealed class PaperImageResponse : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new StringContent("""{"data":[{"b64_json":"CQgH"}]}""") });
        }
    }

    private sealed class PaperStorage : IObjectStorage
    {
        public Task<string> UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) => Task.FromResult(key);
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        public Task<string> GetSignedUrlAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
