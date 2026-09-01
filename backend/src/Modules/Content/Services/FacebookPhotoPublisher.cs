using System.Net;
using System.Text.Json;

namespace Modules.Content.Services;

public sealed record FacebookPhotoResult(string PostId);
public sealed record FacebookPhotoPublication(
    string PageId,
    string PageAccessToken,
    string Caption,
    Stream Image,
    string MimeType);

public sealed class FacebookPhotoPublisher
{
    private readonly HttpClient _httpClient;

    public FacebookPhotoPublisher(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<FacebookPhotoResult> PublishAsync(
        FacebookPhotoPublication publication,
        CancellationToken cancellationToken)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(publication.PageAccessToken), "access_token");
        multipart.Add(new StringContent(publication.Caption), "message");
        var imageContent = new StreamContent(publication.Image);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(publication.MimeType);
        multipart.Add(imageContent, "source", "content-post.png");

        using var response = await _httpClient.PostAsync(
            $"{Uri.EscapeDataString(publication.PageId)}/photos",
            multipart,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FacebookPublishException(
                $"رفض Facebook نشر الصورة ({(int)response.StatusCode}): {SafeMetaError(body)}",
                (int)response.StatusCode >= 500);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var id = root.TryGetProperty("post_id", out var postId)
            ? postId.GetString()
            : root.TryGetProperty("id", out var photoId)
                ? photoId.GetString()
                : null;
        return new FacebookPhotoResult(id ?? throw new InvalidOperationException("Facebook لم يُرجع رقم المنشور."));
    }

    private static string SafeMetaError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                var providerMessage = message.GetString() ?? "خطأ غير معروف";
                return providerMessage[..Math.Min(providerMessage.Length, 500)];
            }
        }
        catch (JsonException)
        {
            // Do not expose arbitrary upstream content.
        }

        return "خطأ غير معروف من Meta.";
    }
}

public sealed class FacebookPublishException : Exception
{
    public FacebookPublishException(string message, bool outcomeUnknown) : base(message)
    {
        OutcomeUnknown = outcomeUnknown;
    }

    public bool OutcomeUnknown { get; }
}
