using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Modules.QuranChallenge.Services;

public sealed class YouTubeOAuthClient
{
    private const string UploadScope = "https://www.googleapis.com/auth/youtube.upload";
    private const string ReadScope = "https://www.googleapis.com/auth/youtube.readonly";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly string? _redirectUri;

    public YouTubeOAuthClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _clientId = configuration["YOUTUBE_CLIENT_ID"];
        _clientSecret = configuration["YOUTUBE_CLIENT_SECRET"];
        _redirectUri = configuration["YOUTUBE_OAUTH_REDIRECT_URI"];
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId)
        && !string.IsNullOrWhiteSpace(_clientSecret)
        && Uri.TryCreate(_redirectUri, UriKind.Absolute, out _);

    public string AuthorizationUrl(string state)
    {
        EnsureConfigured();
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _clientId,
            ["redirect_uri"] = _redirectUri,
            ["response_type"] = "code",
            ["scope"] = $"{UploadScope} {ReadScope}",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state
        };
        return QueryString.Create(query).ToUriComponent().Insert(0, "https://accounts.google.com/o/oauth2/v2/auth");
    }

    public async Task<YouTubeOAuthTokens> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var fields = ClientFields();
        fields["code"] = code;
        fields["redirect_uri"] = _redirectUri!;
        fields["grant_type"] = "authorization_code";
        return await RequestTokensAsync(fields, cancellationToken);
    }

    public async Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var fields = ClientFields();
        fields["refresh_token"] = refreshToken;
        fields["grant_type"] = "refresh_token";
        return (await RequestTokensAsync(fields, cancellationToken)).AccessToken;
    }

    public async Task<YouTubeChannel> ChannelAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ChannelListResponse>(cancellationToken);
        var channel = payload?.Items?.FirstOrDefault();
        return channel is null
            ? throw new InvalidOperationException("لم يتم العثور على قناة YouTube مرتبطة بهذا الحساب.")
            : new YouTubeChannel(channel.Id, channel.Snippet.Title);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken });
        using var response = await _httpClientFactory.CreateClient().PostAsync("https://oauth2.googleapis.com/revoke", content, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest) return;
        response.EnsureSuccessStatusCode();
    }

    private Dictionary<string, string> ClientFields()
    {
        return new Dictionary<string, string>
        {
            ["client_id"] = _clientId!,
            ["client_secret"] = _clientSecret!
        };
    }

    private async Task<YouTubeOAuthTokens> RequestTokensAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClientFactory.CreateClient().PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var failure = await response.Content.ReadFromJsonAsync<GoogleOAuthError>(cancellationToken);
            if (failure?.Error == "invalid_grant") throw new YouTubeReauthenticationRequiredException();
            throw new InvalidOperationException(failure?.Description is { Length: > 0 }
                ? $"تعذّر تجديد صلاحية YouTube: {failure.Description}"
                : $"تعذّر تجديد صلاحية YouTube (رمز الحالة {(int)response.StatusCode}).");
        }
        return await response.Content.ReadFromJsonAsync<YouTubeOAuthTokens>(cancellationToken)
            ?? throw new InvalidOperationException("استجابة Google لم تحتوِ على بيانات الدخول.");
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("بيانات Google OAuth غير مُعدّة على الخادم.");
        }
    }

    private sealed record ChannelListResponse([property: JsonPropertyName("items")] List<ChannelResource>? Items);
    private sealed record ChannelResource(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("snippet")] ChannelSnippet Snippet);
    private sealed record ChannelSnippet([property: JsonPropertyName("title")] string Title);
    private sealed record GoogleOAuthError(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? Description);
}

public sealed class YouTubeReauthenticationRequiredException : InvalidOperationException
{
    public YouTubeReauthenticationRequiredException()
        : base("انتهت صلاحية ربط YouTube أو أُلغي. أعد ربط القناة ثم شغّل الجدولة.") { }
}

public sealed record YouTubeChannel(string Id, string Title);

public sealed record YouTubeOAuthTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);
