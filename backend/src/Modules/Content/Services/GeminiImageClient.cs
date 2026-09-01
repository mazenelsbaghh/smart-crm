using System.Net.Http.Json;
using System.Text.Json;

namespace Modules.Content.Services;

public sealed record GeneratedImage(byte[] Bytes, string MimeType, string Model, string Size);
public sealed record GeminiImageRequest(string Prompt, string ApiKey, byte[] LogoBytes, string LogoMimeType);

public sealed class GeminiImageClient
{
    public const string HighestQualityModel = "gemini-3-pro-image";
    public const string OutputSize = "4K";
    public const string AspectRatio = "1:1";
    private const string GenerateContentApiVersion = "v1beta";
    private const string ApiOutputSize = "IMAGE_SIZE_FOUR_K";
    private const string ApiAspectRatio = "ASPECT_RATIO_ONE_BY_ONE";

    private readonly HttpClient _httpClient;

    public GeminiImageClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GeneratedImage> GenerateAsync(
        GeminiImageRequest imageRequest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageRequest.ApiKey))
            throw new InvalidOperationException("مفتاح Gemini غير موجود في إعدادات المشروع.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{GenerateContentApiVersion}/models/{HighestQualityModel}:generateContent");
        request.Headers.Add("x-goog-api-key", imageRequest.ApiKey);
        request.Content = JsonContent.Create(new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = imageRequest.Prompt },
                        new
                        {
                            inlineData = new
                            {
                                mimeType = imageRequest.LogoMimeType,
                                data = Convert.ToBase64String(imageRequest.LogoBytes)
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "IMAGE" },
                responseFormat = new
                {
                    image = new
                    {
                        aspectRatio = ApiAspectRatio,
                        imageSize = ApiOutputSize
                    }
                }
            }
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"فشل توليد الصورة من Gemini ({(int)response.StatusCode}): {SafeApiError(responseJson)}");
        }

        using var document = JsonDocument.Parse(responseJson);
        var parts = document.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts");
        foreach (var part in parts.EnumerateArray())
        {
            if (!part.TryGetProperty("inlineData", out var inlineData)) continue;
            var base64Image = inlineData.GetProperty("data").GetString();
            if (string.IsNullOrWhiteSpace(base64Image)) continue;
            var mimeType = inlineData.TryGetProperty("mimeType", out var mime)
                ? mime.GetString() ?? "image/png"
                : "image/png";
            return new GeneratedImage(Convert.FromBase64String(base64Image), mimeType, HighestQualityModel, OutputSize);
        }

        throw new InvalidOperationException("Gemini لم يُرجع صورة صالحة.");
    }

    private static string SafeApiError(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                var providerMessage = message.GetString() ?? "خطأ غير معروف";
                return providerMessage[..Math.Min(providerMessage.Length, 500)];
            }
        }
        catch (JsonException)
        {
            // Return a generic message without echoing an untrusted provider response.
        }

        return "خطأ غير معروف من مزود الصور.";
    }
}
