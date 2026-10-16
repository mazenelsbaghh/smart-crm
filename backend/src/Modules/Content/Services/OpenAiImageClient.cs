using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Modules.Content.Services;

public sealed record OpenAiImageRequest(string Prompt, string? ApiKey, IReadOnlyList<GeminiReferenceImage> References,
    string AspectRatio = GeminiImageClient.SquareAspectRatio, string Model = OpenAiImageClient.HighestQualityModel,
    string? ImageSize = null);

public sealed class OpenAiImageClient(HttpClient httpClient)
{
    public const string HighestQualityModel = "gpt-image-2.5-sunburst";
    public const string FastModel = "gpt-image-2.5-flare";
    public const string OutputDimensions = "2880x2880";
    public const string PortraitDimensions = "2448x3264";
    public static bool SupportsModel(string model) => model is HighestQualityModel or FastModel;

    public async Task<GeneratedImage> GenerateAsync(OpenAiImageRequest imageRequest, CancellationToken cancellationToken)
    {
        if (!SupportsModel(imageRequest.Model)) throw new ArgumentException("اختر Sunburst أو Flare لتوليد صور ChatGPT.");
        if (string.IsNullOrWhiteSpace(imageRequest.ApiKey))
            throw new InvalidOperationException("أضف مفتاح OpenAI في إعدادات صور البوستات أولاً.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            imageRequest.References.Count > 0 ? "v1/images/edits" : "v1/images/generations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", imageRequest.ApiKey);
        request.Content = ImageRequestContent(imageRequest);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ProviderError(response.StatusCode));
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return GeneratedImageFromResponse(document.RootElement, imageRequest.Model, Dimensions(imageRequest));
    }

    private static JsonContent ImageRequestContent(OpenAiImageRequest imageRequest)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = imageRequest.Model,
            ["prompt"] = imageRequest.Prompt,
            ["quality"] = imageRequest.ImageSize == GeminiImageClient.PreviewImageSize ? "high" : "max",
            ["size"] = Dimensions(imageRequest),
            ["output_format"] = "png",
            ["n"] = 1
        };
        if (imageRequest.References.Count > 0)
            payload["images"] = imageRequest.References.Select(reference => new
            {
                image_url = $"data:{reference.MimeType};base64,{Convert.ToBase64String(reference.Bytes)}"
            }).ToArray();
        return JsonContent.Create(payload);
    }

    private static string Dimensions(OpenAiImageRequest request)
    {
        var portrait = request.AspectRatio == GeminiImageClient.PortraitAspectRatio;
        if (request.ImageSize == GeminiImageClient.PreviewImageSize) return portrait ? "768x1024" : "1024x1024";
        return portrait ? PortraitDimensions : OutputDimensions;
    }

    private static GeneratedImage GeneratedImageFromResponse(JsonElement response, string model, string dimensions)
    {
        if (!response.TryGetProperty("data", out var images) || images.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("OpenAI لم يرجع صورة صالحة.");
        foreach (var generatedImage in images.EnumerateArray())
        {
            if (!generatedImage.TryGetProperty("b64_json", out var encodedImage)
                || string.IsNullOrWhiteSpace(encodedImage.GetString())) continue;
            return new GeneratedImage(Convert.FromBase64String(encodedImage.GetString()!), "image/png",
                model, dimensions);
        }
        throw new InvalidOperationException("OpenAI لم يرجع صورة صالحة.");
    }

    private static string ProviderError(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "مفتاح OpenAI غير صالح. حدّث المفتاح في إعدادات صور البوستات.",
        System.Net.HttpStatusCode.Forbidden => "حساب OpenAI لا يملك صلاحية موديل الصور. راجع صلاحيات المفتاح والتحقق من الحساب.",
        System.Net.HttpStatusCode.TooManyRequests => "وصل حساب OpenAI لحد الاستخدام أو الرصيد. راجع الحساب ثم أعد المحاولة.",
        _ => $"تعذر توليد الصورة من OpenAI ({(int)status}). راجع إعدادات الحساب أو أعد المحاولة لاحقًا."
    };
}
