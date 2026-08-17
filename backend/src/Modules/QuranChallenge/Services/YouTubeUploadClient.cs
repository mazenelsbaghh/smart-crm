using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Modules.QuranChallenge.Services;

public sealed class YouTubeUploadClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public YouTubeUploadClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> UploadAsync(YouTubeVideoUpload upload, string accessToken, CancellationToken cancellationToken)
    {
        var uploadUrl = await ResumableUploadUrlAsync(upload, accessToken, cancellationToken);
        using var content = new ByteArrayContent(upload.VideoBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var video = await response.Content.ReadFromJsonAsync<UploadedVideo>(cancellationToken);
        return video?.Id ?? throw new InvalidOperationException("تم الرفع لكن YouTube لم يُرجع معرّف الفيديو.");
    }

    private async Task<Uri> ResumableUploadUrlAsync(YouTubeVideoUpload upload, string accessToken, CancellationToken cancellationToken)
    {
        const string endpoint = "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status&notifySubscribers=false";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Upload-Content-Length", upload.VideoBytes.Length.ToString());
        request.Headers.Add("X-Upload-Content-Type", "video/mp4");
        request.Content = JsonContent.Create(new
        {
            snippet = new { upload.Title, upload.Description, categoryId = "27", tags = upload.Tags },
            status = new { privacyStatus = upload.PrivacyStatus, selfDeclaredMadeForKids = false }
        });
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response.Headers.Location ?? throw new InvalidOperationException("YouTube لم يُرجع رابط جلسة الرفع.");
    }

    private sealed record UploadedVideo([property: JsonPropertyName("id")] string Id);
}

public sealed record YouTubeVideoUpload(
    string Title,
    string Description,
    string PrivacyStatus,
    string[] Tags,
    byte[] VideoBytes);
