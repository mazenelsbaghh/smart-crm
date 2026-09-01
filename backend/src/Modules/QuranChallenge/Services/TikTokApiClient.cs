using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Modules.QuranChallenge.Services;

public sealed class TikTokApiClient
{
    private const string ApiBase = "https://zernio.com/api/v1";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _apiKey;

    public TikTokApiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["ZERNIO_API_KEY"];
    }

    public bool IsConfigured => _apiKey?.StartsWith("sk_", StringComparison.Ordinal) == true;

    public async Task<TikTokCreatorInfo> CreatorInfoAsync(string accountId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"accounts/{Uri.EscapeDataString(accountId)}/tiktok/creator-info?mediaType=video", null, cancellationToken);
        var root = document.RootElement;
        var creator = FindObject(root, "creator");
        var limits = FindObject(root, "postingLimits");
        var privacyLevels = FindArray(root, "privacyLevels")
            .Select(privacyOption => privacyOption.ValueKind == JsonValueKind.String
                ? privacyOption.GetString()
                : ReadString(privacyOption, "value"))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();

        return new TikTokCreatorInfo(
            ReadString(creator, "avatarUrl", "avatar_url"),
            ReadString(creator, "username", "creatorUsername"),
            ReadString(creator, "nickname", "displayName", "creatorNickname"),
            privacyLevels,
            ReadBoolean(limits, "commentDisabled", "comment_disabled"),
            ReadBoolean(limits, "duetDisabled", "duet_disabled"),
            ReadBoolean(limits, "stitchDisabled", "stitch_disabled"),
            ReadInt(limits, 600, "maxVideoPostDurationSeconds", "max_video_post_duration_sec"));
    }

    public async Task<string> PublishVideoAsync(string accountId, TikTokPostRequest post, byte[] videoBytes,
        CancellationToken cancellationToken)
    {
        using var upload = await SendAsync(HttpMethod.Post, "media/presign",
            new { filename = $"quran-challenge-{Guid.NewGuid():N}.mp4", contentType = "video/mp4" }, cancellationToken);
        var uploadUrl = ReadRequiredString(upload.RootElement, "uploadUrl");
        var publicUrl = ReadRequiredString(upload.RootElement, "publicUrl");

        using (var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl))
        {
            uploadRequest.Content = new ByteArrayContent(videoBytes);
            uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            using var uploadResponse = await _httpClientFactory.CreateClient().SendAsync(
                uploadRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(uploadResponse, cancellationToken);
        }

        using var created = await SendAsync(HttpMethod.Post, "posts", new
        {
            content = post.Title,
            mediaItems = new[] { new { type = "video", url = publicUrl } },
            platforms = new[] { new { platform = "tiktok", accountId } },
            tiktokSettings = new
            {
                privacy_level = post.PrivacyLevel,
                allow_comment = post.AllowComment,
                allow_duet = post.AllowDuet,
                allow_stitch = post.AllowStitch,
                content_preview_confirmed = true,
                express_consent_given = true,
                draft = false
            },
            publishNow = true
        }, cancellationToken);
        var createdPost = FindObject(created.RootElement, "post");
        ThrowIfPublishingFailed(createdPost);
        return ReadRequiredString(createdPost, "_id", "id");
    }

    public async Task<TikTokPublishStatus> PublishStatusAsync(string postId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, $"posts/{Uri.EscapeDataString(postId)}", null,
            cancellationToken);
        var post = FindObject(document.RootElement, "post");
        var rawStatus = ReadString(post, "status") ?? "unknown";
        var status = rawStatus.ToLowerInvariant() switch
        {
            "published" => "PUBLISH_COMPLETE",
            "failed" or "partial" => "FAILED",
            "draft" or "scheduled" or "publishing" => "PROCESSING",
            _ => rawStatus.ToUpperInvariant()
        };
        var failure = PublishingFailure(post);
        var urls = FindArray(post, "platforms")
            .Select(platformTarget => ReadString(platformTarget, "platformPostUrl"))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
        return new TikTokPublishStatus(status, failure, urls);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(method, $"{ApiBase}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"رفض Zernio الطلب ({(int)response.StatusCode}): {detail[..Math.Min(detail.Length, 500)]}");
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured) throw new InvalidOperationException("مفتاح Zernio API غير مُعدّ على الخادم.");
    }

    private static JsonElement FindObject(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Object) return value;
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object) return FindObject(data, property);
        return element;
    }

    private static IEnumerable<JsonElement> FindArray(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().ToArray();
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("data", out var data))
            return FindArray(data, property);
        return [];
    }

    private static string ReadRequiredString(JsonElement element, params string[] properties) =>
        ReadString(element, properties)
        ?? throw new InvalidOperationException($"لم تُرجع استجابة Zernio الحقل {properties[0]}.");

    private static string? ReadString(JsonElement element, params string[] properties)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in properties)
            if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        if (element.TryGetProperty("data", out var data)) return ReadString(data, properties);
        return null;
    }

    private static bool ReadBoolean(JsonElement element, params string[] properties)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in properties)
            if (element.TryGetProperty(property, out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        return false;
    }

    private static int ReadInt(JsonElement element, int fallback, params string[] properties)
    {
        if (element.ValueKind != JsonValueKind.Object) return fallback;
        foreach (var property in properties)
            if (element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result)) return result;
        return fallback;
    }

    private static void ThrowIfPublishingFailed(JsonElement post)
    {
        if (!string.Equals(ReadString(post, "status"), "failed", StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidOperationException(PublishingFailure(post) ?? "فشل النشر المباشر على TikTok.");
    }

    private static string? PublishingFailure(JsonElement post)
    {
        var postFailure = ReadString(post, "error", "errorMessage", "failReason");
        if (!string.IsNullOrWhiteSpace(postFailure)) return postFailure;
        return FindArray(post, "platforms")
            .Select(platform => ReadString(platform, "errorMessage", "error", "failReason"))
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
    }
}

public sealed record TikTokPostRequest(string Title, string PrivacyLevel, bool AllowComment, bool AllowDuet,
    bool AllowStitch);

public sealed record TikTokCreatorInfo(string? CreatorAvatarUrl, string? CreatorUsername, string? CreatorNickname,
    IReadOnlyList<string> PrivacyLevelOptions, bool CommentDisabled, bool DuetDisabled, bool StitchDisabled,
    int MaxVideoPostDurationSeconds);

public sealed record TikTokPublishStatus(string Status, string? FailReason,
    IReadOnlyList<string> PubliclyAvailablePostIds);
