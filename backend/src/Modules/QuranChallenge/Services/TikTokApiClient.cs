using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modules.QuranChallenge.Services;

public sealed class TikTokApiClient
{
    private const string ApiBase = "https://open.tiktokapis.com";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _clientKey;
    private readonly string? _clientSecret;
    private readonly string? _redirectUri;

    public TikTokApiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _clientKey = configuration["TIKTOK_CLIENT_KEY"];
        _clientSecret = configuration["TIKTOK_CLIENT_SECRET"];
        _redirectUri = configuration["TIKTOK_OAUTH_REDIRECT_URI"];
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientKey)
        && !string.IsNullOrWhiteSpace(_clientSecret)
        && Uri.TryCreate(_redirectUri, UriKind.Absolute, out _);

    public string AuthorizationUrl(string state)
    {
        EnsureConfigured();
        var query = new Dictionary<string, string?>
        {
            ["client_key"] = _clientKey,
            ["redirect_uri"] = _redirectUri,
            ["response_type"] = "code",
            ["scope"] = "user.info.basic,video.publish",
            ["state"] = state
        };
        return QueryString.Create(query).ToUriComponent()
            .Insert(0, "https://www.tiktok.com/v2/auth/authorize/");
    }

    public Task<TikTokTokens> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var fields = ClientFields();
        fields["code"] = code;
        fields["grant_type"] = "authorization_code";
        fields["redirect_uri"] = _redirectUri!;
        return RequestTokensAsync(fields, cancellationToken);
    }

    public Task<TikTokTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var fields = ClientFields();
        fields["grant_type"] = "refresh_token";
        fields["refresh_token"] = refreshToken;
        return RequestTokensAsync(fields, cancellationToken);
    }

    public async Task<TikTokUser> UserAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get,
            $"{ApiBase}/v2/user/info/?fields=open_id,display_name,avatar_url", accessToken);
        var payload = await SendJsonAsync<TikTokUserResponse>(request, cancellationToken);
        EnsureOk(payload.Error);
        return payload.Data?.User
            ?? throw new InvalidOperationException("لم يُرجع TikTok بيانات الحساب.");
    }

    public async Task<TikTokCreatorInfo> CreatorInfoAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post,
            $"{ApiBase}/v2/post/publish/creator_info/query/", accessToken);
        request.Content = JsonContent.Create(new { });
        var payload = await SendJsonAsync<TikTokCreatorInfoResponse>(request, cancellationToken);
        EnsureOk(payload.Error);
        return payload.Data
            ?? throw new InvalidOperationException("لم يُرجع TikTok إعدادات النشر للحساب.");
    }

    public async Task<TikTokPostInitialization> InitializePostAsync(
        string accessToken,
        TikTokPostRequest post,
        long videoSize,
        CancellationToken cancellationToken)
    {
        var (chunkSize, chunkCount) = ChunkPlan(videoSize);
        using var request = Authorized(HttpMethod.Post,
            $"{ApiBase}/v2/post/publish/video/init/", accessToken);
        request.Content = JsonContent.Create(new
        {
            post_info = new
            {
                title = post.Title,
                privacy_level = post.PrivacyLevel,
                disable_comment = !post.AllowComment,
                disable_duet = !post.AllowDuet,
                disable_stitch = !post.AllowStitch,
                video_cover_timestamp_ms = 1000
            },
            source_info = new
            {
                source = "FILE_UPLOAD",
                video_size = videoSize,
                chunk_size = chunkSize,
                total_chunk_count = chunkCount
            }
        });
        var payload = await SendJsonAsync<TikTokPostInitializationResponse>(request, cancellationToken);
        EnsureOk(payload.Error);
        return payload.Data is { PublishId.Length: > 0, UploadUrl.Length: > 0 }
            ? payload.Data
            : throw new InvalidOperationException("لم يُرجع TikTok رابط رفع صالحًا.");
    }

    public async Task UploadAsync(
        string uploadUrl,
        byte[] video,
        CancellationToken cancellationToken)
    {
        var (chunkSize, chunkCount) = ChunkPlan(video.LongLength);
        var offset = 0L;
        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var remaining = video.LongLength - offset;
            var count = chunkIndex == chunkCount - 1
                ? checked((int)remaining)
                : checked((int)Math.Min(chunkSize, remaining));
            using var content = new ByteArrayContent(video, checked((int)offset), count);
            content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            content.Headers.ContentLength = count;
            content.Headers.TryAddWithoutValidation(
                "Content-Range",
                $"bytes {offset}-{offset + count - 1}/{video.LongLength}");
            using var response = await _httpClientFactory.CreateClient()
                .PutAsync(uploadUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"رفض TikTok جزء الفيديو ({(int)response.StatusCode}): {detail[..Math.Min(detail.Length, 500)]}");
            }
            offset += count;
        }
    }

    public async Task<TikTokPublishStatus> PublishStatusAsync(
        string accessToken,
        string publishId,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post,
            $"{ApiBase}/v2/post/publish/status/fetch/", accessToken);
        request.Content = JsonContent.Create(new { publish_id = publishId });
        var payload = await SendJsonAsync<TikTokPublishStatusResponse>(request, cancellationToken);
        EnsureOk(payload.Error);
        return payload.Data
            ?? throw new InvalidOperationException("لم يُرجع TikTok حالة النشر.");
    }

    public async Task RevokeAsync(string accessToken, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var fields = ClientFields();
        fields["token"] = accessToken;
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClientFactory.CreateClient()
            .PostAsync($"{ApiBase}/v2/oauth/revoke/", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<TikTokTokens> RequestTokensAsync(
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClientFactory.CreateClient()
            .PostAsync($"{ApiBase}/v2/oauth/token/", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"فشل تسجيل TikTok ({(int)response.StatusCode}): {body[..Math.Min(body.Length, 500)]}");
        }
        return JsonSerializer.Deserialize<TikTokTokens>(body)
            ?? throw new InvalidOperationException("استجابة TikTok لم تحتوِ على رموز الدخول.");
    }

    private async Task<T> SendJsonAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"رفض TikTok الطلب ({(int)response.StatusCode}): {body[..Math.Min(body.Length, 500)]}");
        }
        return JsonSerializer.Deserialize<T>(body)
            ?? throw new InvalidOperationException("استجابة TikTok غير صالحة.");
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private Dictionary<string, string> ClientFields()
    {
        EnsureConfigured();
        return new Dictionary<string, string>
        {
            ["client_key"] = _clientKey!,
            ["client_secret"] = _clientSecret!
        };
    }

    private static (long ChunkSize, int ChunkCount) ChunkPlan(long videoSize)
    {
        if (videoSize <= 0) throw new ArgumentOutOfRangeException(nameof(videoSize));
        const long maxSingleChunk = 64L * 1024 * 1024;
        if (videoSize <= maxSingleChunk) return (videoSize, 1);
        const long regularChunk = 10L * 1024 * 1024;
        return (regularChunk, checked((int)(videoSize / regularChunk)));
    }

    private static void EnsureOk(TikTokError? error)
    {
        if (error is null || error.Code == "ok") return;
        throw new InvalidOperationException(
            $"TikTok: {error.Message ?? error.Code} ({error.Code})");
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("بيانات TikTok App غير مُعدّة على الخادم.");
        }
    }
}

public sealed record TikTokPostRequest(
    string Title,
    string PrivacyLevel,
    bool AllowComment,
    bool AllowDuet,
    bool AllowStitch);

public sealed record TikTokTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("open_id")] string OpenId,
    [property: JsonPropertyName("refresh_expires_in")] int RefreshExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("scope")] string Scope);

public sealed record TikTokUser(
    [property: JsonPropertyName("open_id")] string OpenId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

public sealed record TikTokCreatorInfo(
    [property: JsonPropertyName("creator_avatar_url")] string? CreatorAvatarUrl,
    [property: JsonPropertyName("creator_username")] string? CreatorUsername,
    [property: JsonPropertyName("creator_nickname")] string CreatorNickname,
    [property: JsonPropertyName("privacy_level_options")] string[] PrivacyLevelOptions,
    [property: JsonPropertyName("comment_disabled")] bool CommentDisabled,
    [property: JsonPropertyName("duet_disabled")] bool DuetDisabled,
    [property: JsonPropertyName("stitch_disabled")] bool StitchDisabled,
    [property: JsonPropertyName("max_video_post_duration_sec")] int MaxVideoPostDurationSeconds);

public sealed record TikTokPostInitialization(
    [property: JsonPropertyName("publish_id")] string PublishId,
    [property: JsonPropertyName("upload_url")] string UploadUrl);

public sealed record TikTokPublishStatus(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("fail_reason")] string? FailReason,
    [property: JsonPropertyName("publicaly_available_post_id")] long[]? PubliclyAvailablePostIds,
    [property: JsonPropertyName("uploaded_bytes")] long UploadedBytes);

public sealed record TikTokError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("log_id")] string? LogId);

file sealed record TikTokUserResponse(
    [property: JsonPropertyName("data")] TikTokUserData? Data,
    [property: JsonPropertyName("error")] TikTokError? Error);
file sealed record TikTokUserData([property: JsonPropertyName("user")] TikTokUser? User);
file sealed record TikTokCreatorInfoResponse(
    [property: JsonPropertyName("data")] TikTokCreatorInfo? Data,
    [property: JsonPropertyName("error")] TikTokError? Error);
file sealed record TikTokPostInitializationResponse(
    [property: JsonPropertyName("data")] TikTokPostInitialization? Data,
    [property: JsonPropertyName("error")] TikTokError? Error);
file sealed record TikTokPublishStatusResponse(
    [property: JsonPropertyName("data")] TikTokPublishStatus? Data,
    [property: JsonPropertyName("error")] TikTokError? Error);
