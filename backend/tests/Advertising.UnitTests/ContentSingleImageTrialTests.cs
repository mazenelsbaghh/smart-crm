using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Content.API;
using Modules.Content.Domain;
using Modules.Content.Services;
using Shared.Infrastructure;
using Shared.Security;
using Shared.Storage;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ContentSingleImageTrialTests
{
    [Fact]
    public async Task One_flare_trial_uses_only_its_project_key_and_logo_without_creating_posts_or_enabling_publishing()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant, new ServiceCollection().BuildServiceProvider());
        var vault = new ProjectSecretVault(new EphemeralDataProtectionProvider());
        var projectId = tenant.ProjectId;
        var otherId = Guid.NewGuid();
        var current = new ContentAutomationSettings { ProjectId = projectId, ImageProvider = "Gemini", LogoObjectKey = "current-logo.png",
            OpenAiImageApiKey = vault.Protect(projectId, "current-image-key") };
        db.AddRange(current, new ContentAutomationSettings { ProjectId = otherId, LogoObjectKey = "other-logo.png",
            OpenAiImageApiKey = vault.Protect(otherId, "other-image-key") });
        await db.SaveChangesAsync();
        using var handler = new TrialImageResponse();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://image.test/") };
        var service = new ContentImageTrialService(db, new ContentImageClient(new GeminiImageClient(http), new OpenAiImageClient(http), vault),
            vault, new TrialLogoStorage());

        var preview = await service.GenerateAsync(projectId, new("صورة واحدة للتجربة", "OpenAI", OpenAiImageClient.FastModel), CancellationToken.None);

        Assert.Equal("data:image/png;base64,CQgH", preview.Image);
        Assert.Equal(OpenAiImageClient.FastModel, preview.Model);
        Assert.Equal("1024x1024", preview.Size);
        Assert.Equal("Bearer current-image-key", handler.Authorization);
        Assert.Equal("/v1/images/edits", handler.Path);
        Assert.False(current.IsEnabled);
        Assert.Equal("Gemini", current.ImageProvider);
        Assert.Empty(await db.ContentPosts.ToListAsync());
        Assert.Empty(await db.ContentWeekPlans.ToListAsync());
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.Equal(1, request.RootElement.GetProperty("n").GetInt32());
        Assert.Equal("high", request.RootElement.GetProperty("quality").GetString());
        Assert.Equal("data:image/png;base64,AQID", request.RootElement.GetProperty("images")[0].GetProperty("image_url").GetString());
    }

    [Theory]
    [InlineData("Agent", false)]
    [InlineData("Owner", true)]
    public async Task Trials_are_forbidden_for_agents_and_foreign_project_tokens(string role, bool foreignProject)
    {
        var tenant = new TenantContext(); tenant.SetProjectId(Guid.NewGuid());
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role),
            new Claim("ProjectId", (foreignProject ? Guid.NewGuid() : tenant.ProjectId).ToString())], "test"));
        var controller = new ContentImageTrialsController(tenant, new ProjectAuthorizationService(), null!)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } } };

        Assert.IsType<ForbidResult>(await controller.Preview(new("معاينة"), CancellationToken.None));
    }

    private sealed class TrialImageResponse : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString(); Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"data":[{"b64_json":"CQgH"}]}""") };
        }
    }

    private sealed class TrialLogoStorage : IObjectStorage
    {
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(key == "current-logo.png" ? new MemoryStream([1, 2, 3]) : throw new InvalidOperationException(key));
        public Task<string> UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetSignedUrlAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
