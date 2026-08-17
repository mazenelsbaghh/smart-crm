using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Modules.QuranChallenge.Services;

public sealed class FacebookReelsUploadClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiVersion;

    public FacebookReelsUploadClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiVersion = configuration["FACEBOOK_GRAPH_API_VERSION"] ?? "v20.0";
    }

    public async Task<string> UploadAsync(
        FacebookReelUpload upload,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var session = await CreateSessionAsync(pageAccessToken, cancellationToken);
        await UploadBytesAsync(session.UploadUrl, upload.VideoBytes, pageAccessToken, cancellationToken);
        await PublishAsync(session.VideoId, upload, pageAccessToken, cancellationToken);
        return session.VideoId;
    }

    private async Task<ReelSession> CreateSessionAsync(
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = $"https://graph.facebook.com/{_apiVersion}/me/video_reels";
        using var content = FormContent(("access_token", pageAccessToken), ("upload_phase", "start"));
        using var response = await _httpClientFactory.CreateClient().PostAsync(endpoint, content, cancellationToken);
        await EnsureSuccessAsync(response, "إنشاء جلسة Facebook Reel", cancellationToken);
        var session = await response.Content.ReadFromJsonAsync<ReelSession>(cancellationToken);
        return session is { VideoId.Length: > 0, UploadUrl.Length: > 0 }
            ? session
            : throw new InvalidOperationException("لم يُرجع Facebook بيانات جلسة رفع صالحة.");
    }

    private async Task UploadBytesAsync(
        string uploadUrl,
        byte[] videoBytes,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(videoBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", pageAccessToken);
        request.Headers.TryAddWithoutValidation("offset", "0");
        request.Headers.TryAddWithoutValidation("file_size", videoBytes.Length.ToString());
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "رفع ملف Facebook Reel", cancellationToken);
    }

    private async Task PublishAsync(
        string videoId,
        FacebookReelUpload upload,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = $"https://graph.facebook.com/{_apiVersion}/me/video_reels";
        using var content = FormContent(
            ("access_token", pageAccessToken),
            ("video_id", videoId),
            ("upload_phase", "finish"),
            ("video_state", "PUBLISHED"),
            ("description", upload.Description),
            ("title", upload.Title));
        using var response = await _httpClientFactory.CreateClient().PostAsync(endpoint, content, cancellationToken);
        await EnsureSuccessAsync(response, "نشر Facebook Reel", cancellationToken);
    }

    private static FormUrlEncodedContent FormContent(params (string Key, string Value)[] values)
    {
        return new FormUrlEncodedContent(values.Select(value =>
            new KeyValuePair<string, string>(value.Key, value.Value)));
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"{operation} فشل: {body}", null, response.StatusCode);
    }

    private sealed record ReelSession(
        [property: JsonPropertyName("video_id")] string VideoId,
        [property: JsonPropertyName("upload_url")] string UploadUrl);
}

public sealed record FacebookReelUpload(string Title, string Description, byte[] VideoBytes);
