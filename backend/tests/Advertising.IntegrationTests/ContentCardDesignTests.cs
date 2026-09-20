using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Content.API;
using Modules.Content.Domain;
using Modules.Content.Services;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Shared.Storage;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ContentCardDesignTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("Queued", "Cancelled")]
    [InlineData("Generating", "Stopping")]
    [InlineData("Ready", "Ready")]
    public async Task Stop_preserves_saved_artwork_and_only_transitions_active_work(string initial, string expected)
    {
        var tenant = Tenant();
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var game = new ContentCardGame { ProjectId = tenant.ProjectId, DesignStatus = initial, BackImageObjectKey = "saved.png" };
        db.Add(game);
        await db.SaveChangesAsync();

        Assert.IsType<OkObjectResult>(await Controller(db, tenant).StopDesign(game.Id, CancellationToken.None));

        await db.Entry(game).ReloadAsync();
        Assert.Equal(expected, game.DesignStatus);
        Assert.Equal("saved.png", game.BackImageObjectKey);
    }

    [Fact]
    public async Task Stop_during_back_generation_does_not_start_faces_or_mark_the_game_ready()
    {
        var tenant = Tenant();
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var game = new ContentCardGame { ProjectId = tenant.ProjectId, DesignStatus = "Queued", CardCount = 1, BrandLogoObjectKey = "logo.png" };
        var card = new ContentGameCard { ProjectId = tenant.ProjectId, GameId = game.Id, Title = "Swap" };
        db.AddRange(game, card, new ProjectSettings { ProjectId = tenant.ProjectId, GeminiApiKey = "test-only" });
        await db.SaveChangesAsync();
        using var http = new HttpClient(new StopDuringImage(async () =>
        {
            await using var stopDb = postgres.CreateContext(tenant);
            await Controller(stopDb, tenant).StopDesign(game.Id, CancellationToken.None);
        })) { BaseAddress = new Uri("https://image.test/") };
        var storage = new MemoryStorage();
        var service = new ContentCardGameDesignService(db, new GeminiImageClient(http), new TestVault(), storage,
            NullLogger<ContentCardGameDesignService>.Instance);

        await service.GenerateAsync(tenant.ProjectId, game.Id, CancellationToken.None);

        await db.Entry(game).ReloadAsync();
        await db.Entry(card).ReloadAsync();
        Assert.Equal("Cancelled", game.DesignStatus);
        Assert.True(ContentCardArtwork.IsCompleteImage(game.BackImageObjectKey));
        Assert.Null(card.ImageObjectKey);
        Assert.Single(storage.Uploads);
        await service.GenerateAsync(tenant.ProjectId, game.Id, CancellationToken.None);
        Assert.Single(storage.Uploads);
    }

    [Fact]
    public async Task Failed_replacement_keeps_legacy_asset_but_does_not_mark_it_as_ready()
    {
        var tenant = Tenant();
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var game = new ContentCardGame { ProjectId = tenant.ProjectId, DesignStatus = "Queued", CardCount = 1,
            BrandLogoObjectKey = "logo.png", BackImageObjectKey = "game/full-card-v1/back.png" };
        var card = new ContentGameCard { ProjectId = tenant.ProjectId, GameId = game.Id, ImageObjectKey = "legacy.png" };
        db.AddRange(game, card, new ProjectSettings { ProjectId = tenant.ProjectId, GeminiApiKey = "test-only" });
        await db.SaveChangesAsync();
        using var http = new HttpClient(new FailedImage()) { BaseAddress = new Uri("https://image.test/") };
        var service = new ContentCardGameDesignService(db, new GeminiImageClient(http), new TestVault(), new MemoryStorage(),
            NullLogger<ContentCardGameDesignService>.Instance);

        await service.GenerateAsync(tenant.ProjectId, game.Id, CancellationToken.None);

        await db.Entry(game).ReloadAsync();
        Assert.Equal("Failed", game.DesignStatus);
        Assert.Equal("legacy.png", card.ImageObjectKey);
        Assert.NotNull(card.ImageError);
    }

    [Fact]
    public async Task Another_project_cannot_stop_a_game_and_legacy_backgrounds_are_not_full_images()
    {
        var tenant = Tenant();
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var game = new ContentCardGame { ProjectId = tenant.ProjectId, DesignStatus = "Ready", BackImageObjectKey = "legacy.png" };
        db.AddRange(game, new ContentGameCard { ProjectId = tenant.ProjectId, GameId = game.Id, ImageObjectKey = "legacy-face.png" });
        await db.SaveChangesAsync();
        var response = Assert.IsType<OkObjectResult>(await Controller(db, tenant).Get(game.Id, CancellationToken.None));
        var json = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("game").GetProperty("backImageUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("cards")[0].GetProperty("imageUrl").ValueKind);

        tenant.SetProjectId(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(await Controller(db, tenant).StopDesign(game.Id, CancellationToken.None));
        await db.Entry(game).ReloadAsync();
        Assert.Equal("Ready", game.DesignStatus);
    }

    private static TenantContext Tenant()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        return tenant;
    }

    private static ContentCardGamesController Controller(AppDbContext db, TenantContext tenant) =>
        new(db, tenant, new ProjectAuthorizationService(), null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("ProjectId", tenant.ProjectId.ToString()), new Claim(ClaimTypes.Role, "Owner")], "test")) } }
        };

    private sealed class StopDuringImage(Func<Task> stop) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await stop();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"candidates":[{"content":{"parts":[{"inlineData":{"mimeType":"image/png","data":"AQID"}}]}}]}""") };
        }
    }

    private sealed class TestVault : IProjectSecretVault
    {
        public bool IsProtected(string? storedValue) => false;
        public string Protect(Guid projectId, string secret) => secret;
        public string? Unprotect(Guid projectId, string? storedValue) => storedValue;
    }

    private sealed class FailedImage : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
    }

    private sealed class MemoryStorage : IObjectStorage
    {
        public List<string> Uploads { get; } = [];
        public Task<string> UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken = default)
        { Uploads.Add(objectKey); return Task.FromResult(objectKey); }
        public Task<Stream> DownloadAsync(string objectKey, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        public Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
