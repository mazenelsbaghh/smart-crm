using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Modules.Content.Domain;
using Modules.Content.Services;
using Shared.Storage;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ContentPaperTests
{
    [Fact]
    public void Frame_approval_is_bound_to_its_project_and_cannot_be_tampered_with()
    {
        var protection = new EphemeralDataProtectionProvider();
        var projectId = Guid.NewGuid();
        var design = new ContentPaperDesign(projectId, "frame.png", null, "مدرّس", "طوابع", ["#285582"]);
        var token = design.Protect(protection);
        var restored = ContentPaperDesign.Restore(protection, projectId, token);
        Assert.Equal(design.FrameKey, restored.FrameKey);
        Assert.Equal(design.Colors, restored.Colors);
        Assert.Throws<ArgumentException>(() => ContentPaperDesign.Restore(protection, Guid.NewGuid(), token));
        Assert.Throws<ArgumentException>(() => ContentPaperDesign.Restore(protection, projectId, "invalid"));
        Assert.Throws<ArgumentException>(() => ContentPaperDesign.Restore(protection, projectId, null));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Each_page_receives_the_frame_reference_and_only_the_selected_teacher(bool includeTeacher, bool includeReference)
    {
        var projectId = Guid.NewGuid();
        var design = new ContentPaperDesign(projectId, "frame.png", includeTeacher ? "teacher.jpg" : null, "", "طوابع", ["#285582"],
            includeReference ? ["reference.webp"] : []);
        var document = new ContentDocument { ProjectId = projectId, Kind = ContentDocumentKind.Paper,
            PaperDesignJson = JsonSerializer.Serialize(design), RequestedPageCount = 2 };
        var handler = new CaptureImageRequest();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://image.test/") };
        var storage = new ImageStorage();
        var imageClient = new ContentImageClient(new GeminiImageClient(http), new OpenAiImageClient(http),
            new ProjectSecretVault(new EphemeralDataProtectionProvider()));
        foreach (var pageIndex in new[] { 0, 1 })
        {
            var image = await ContentPaperRendering.GeneratePageAsync(new(document,
                new ContentDocumentPage { PageIndex = pageIndex, Title = "عنوان", Body = "النص الأصلي" }, "test-key", new ContentAutomationSettings()),
                storage, imageClient, CancellationToken.None);
            Assert.Equal(new byte[] { 9, 8, 7 }, image.Bytes);
            var attachments = handler.LastRequest!.RootElement.GetProperty("contents")[0].GetProperty("parts")
                .EnumerateArray().Where(part => part.TryGetProperty("inlineData", out _)).ToArray();
            Assert.Equal(1 + (includeTeacher ? 1 : 0) + (includeReference ? 1 : 0), attachments.Length);
            Assert.Equal(Convert.ToBase64String(ImageStorage.Frame), attachments[0].GetProperty("inlineData").GetProperty("data").GetString());
            if (includeTeacher)
                Assert.Equal(Convert.ToBase64String(ImageStorage.Teacher), attachments[1].GetProperty("inlineData").GetProperty("data").GetString());
            if (includeReference)
                Assert.Equal(Convert.ToBase64String(ImageStorage.Reference), attachments[^1].GetProperty("inlineData").GetProperty("data").GetString());
        }
    }

    [Fact]
    public async Task Approved_openai_frame_keeps_its_provider_and_portrait_shape_for_lesson_pages()
    {
        var projectId = Guid.NewGuid();
        var protection = new EphemeralDataProtectionProvider();
        var design = new ContentPaperDesign(projectId, "frame.png", "teacher.jpg", "مدرّس", "شرائط", ["#285582"], ["reference.webp"], "OpenAI", OpenAiImageClient.FastModel);
        var restored = ContentPaperDesign.Restore(protection, projectId, design.Protect(protection));
        var document = new ContentDocument { ProjectId = projectId, Kind = ContentDocumentKind.Paper,
            PaperDesignJson = JsonSerializer.Serialize(restored), RequestedPageCount = 2 };
        var vault = new ProjectSecretVault(protection);
        var imageSettings = new ContentAutomationSettings { ProjectId = projectId, ImageProvider = "Gemini",
            OpenAiImageApiKey = vault.Protect(projectId, "paper-image-test-key") };
        using var handler = new CaptureImageRequest();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://image.test/") };
        var client = new ContentImageClient(new GeminiImageClient(http), new OpenAiImageClient(http), vault);

        var image = await ContentPaperRendering.GeneratePageAsync(new(document,
            new ContentDocumentPage { PageIndex = 1, Title = "عنوان", Body = "النص الأصلي" }, "gemini-test-key", imageSettings),
            new ImageStorage(), client, CancellationToken.None);

        Assert.Equal(new byte[] { 9, 8, 7 }, image.Bytes);
        Assert.Equal("2448x3264", image.Size);
        Assert.Equal(OpenAiImageClient.FastModel, image.Model);
        var images = handler.LastRequest!.RootElement.GetProperty("images");
        Assert.Equal(3, images.GetArrayLength());
        Assert.Equal("data:image/png;base64,AQID", images[0].GetProperty("image_url").GetString());
        Assert.Equal("data:image/jpeg;base64,BAUG", images[1].GetProperty("image_url").GetString());
        Assert.Equal("data:image/webp;base64,BwgJ", images[2].GetProperty("image_url").GetString());
    }

    [Fact]
    public void Previously_approved_frames_without_a_provider_keep_gemini()
    {
        var protection = new EphemeralDataProtectionProvider();
        var projectId = Guid.NewGuid();
        var legacyJson = JsonSerializer.Serialize(new { ProjectId = projectId, FrameKey = "frame.png",
            TeacherName = "", Style = "شرائط", Colors = new[] { "#285582" } });
        var token = protection.CreateProtector("ContentPaperFrame.v1").Protect(legacyJson);

        Assert.Equal("Gemini", ContentPaperDesign.Restore(protection, projectId, token).ImageProvider);
    }

    private sealed class CaptureImageRequest : HttpMessageHandler
    {
        public JsonDocument? LastRequest { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest?.Dispose();
            LastRequest = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(HttpStatusCode.OK) { Content = new StringContent(request.Headers.Authorization is not null ? """{"data":[{"b64_json":"CQgH"}]}""" : """{"candidates":[{"content":{"parts":[{"inlineData":{"mimeType":"image/png","data":"CQgH"}}]}}]}""") };
        }
        protected override void Dispose(bool disposing) { if (disposing) LastRequest?.Dispose(); base.Dispose(disposing); }
    }

    private sealed class ImageStorage : IObjectStorage
    {
        public static readonly byte[] Frame = [1, 2, 3];
        public static readonly byte[] Teacher = [4, 5, 6];
        public static readonly byte[] Reference = [7, 8, 9];
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(key switch { "frame.png" => Frame, "teacher.jpg" => Teacher,
                "reference.webp" => Reference, _ => throw new InvalidOperationException(key) }));
        public Task<string> UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetSignedUrlAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
