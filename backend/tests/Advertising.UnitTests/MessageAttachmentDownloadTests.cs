using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Media.API;
using Modules.Media.Domain;
using Modules.Media.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MessageAttachmentDownloadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_streams_original_bytes_only_for_the_authorized_asset_project(bool crossProject)
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        using var services = new ServiceCollection().AddSingleton<IProjectAuthorizationService, ProjectAuthorizationService>().BuildServiceProvider();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant, services);
        var asset = new Asset { ProjectId = crossProject ? Guid.NewGuid() : projectId,
            FileName = "حجز.pdf", ContentType = "application/pdf", StoragePath = "synthetic/file.pdf" };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        var assetService = new AssetService(db, new SyntheticObjectStorage(), null!, tenant);
        var controller = new AssetsController(assetService, tenant)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("ProjectId", projectId.ToString())], "test")) } }
        };

        var response = await controller.DownloadContent(asset.Id);

        if (crossProject) Assert.IsType<ForbidResult>(response);
        else
        {
            var file = Assert.IsType<FileStreamResult>(response);
            using var reader = new StreamReader(file.FileStream);
            Assert.Equal("synthetic original bytes", await reader.ReadToEndAsync());
            Assert.Equal("حجز.pdf", file.FileDownloadName);
            Assert.Equal("application/octet-stream", file.ContentType);
        }
    }

    private sealed class SyntheticObjectStorage : IMinIoStorageService
    {
        public Task<Stream> DownloadFileAsync(string key) => Task.FromResult<Stream>(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes("synthetic original bytes")));
        public Task<string> UploadFileAsync(string key, Stream stream, string contentType) => throw new NotSupportedException();
        public Task<string> GetSignedUrlAsync(string key, TimeSpan expiry) => throw new NotSupportedException();
        public Task DeleteFileAsync(string key) => throw new NotSupportedException();
        public Task EnsureBucketExistsAsync() => throw new NotSupportedException();
    }
}
