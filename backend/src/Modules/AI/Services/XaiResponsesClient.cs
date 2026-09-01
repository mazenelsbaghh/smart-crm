using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Modules.AI.Services;

public sealed record XaiCustomerReplyRequest(
    string Prompt,
    string? ApiKey,
    string Model,
    CustomerReplyAttachment? Attachment = null);

public sealed class XaiResponsesClient(HttpClient httpClient, ILogger<XaiResponsesClient> logger)
{
    private const string ApiError = "[AI_ERROR] Unable to reach xAI.";

    public async Task<string> GenerateReplyAsync(
        XaiCustomerReplyRequest replyRequest,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(replyRequest.ApiKey))
        {
            logger.LogWarning("xAI customer-reply generation was skipped because the API key is missing");
            return ApiError;
        }

        try
        {
            var responseInput = await BuildResponseInputAsync(replyRequest, cancellationToken);
            return responseInput is null
                ? ApiError
                : await SendResponseRequestAsync(replyRequest, responseInput, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("xAI customer-reply request timed out for model {Model}", replyRequest.Model);
            return ApiError;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or FormatException)
        {
            logger.LogWarning(exception, "xAI customer-reply generation failed for model {Model}", replyRequest.Model);
            return ApiError;
        }
    }

    private async Task<object?> BuildResponseInputAsync(
        XaiCustomerReplyRequest replyRequest,
        CancellationToken cancellationToken)
    {
        var attachment = replyRequest.Attachment;
        if (attachment is null || attachment.Bytes.Length == 0) return replyRequest.Prompt;

        if (attachment.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            var transcription = await TranscribeAudioAsync(attachment, replyRequest.ApiKey!, cancellationToken);
            return string.IsNullOrWhiteSpace(transcription)
                ? null
                : $"{replyRequest.Prompt}\n\n[Customer voice-note transcription]\n{transcription}";
        }

        if (attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return CreateImageInput(replyRequest.Prompt, attachment);
        }

        logger.LogWarning(
            "xAI customer-reply generation does not support attachment type {MimeType}",
            attachment.MimeType);
        return null;
    }

    private async Task<string> SendResponseRequestAsync(
        XaiCustomerReplyRequest replyRequest,
        object responseInput,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "v1/responses", replyRequest.ApiKey!);
        request.Content = new StringContent(
            JsonSerializer.Serialize(CreateRequestBody(replyRequest.Model, responseInput)),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "xAI customer-reply request failed with HTTP {StatusCode} for model {Model}",
                (int)response.StatusCode,
                replyRequest.Model);
            return ApiError;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ResponsesApiOutputText.Extract(responseJson) ?? ApiError;
    }

    private async Task<string?> TranscribeAudioAsync(
        CustomerReplyAttachment attachment,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "v1/stt", apiKey);
        using var form = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(attachment.Bytes);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(attachment.MimeType);
        form.Add(audioContent, "file", CustomerReplyAttachmentFileName.ForVoiceNote(attachment.MimeType));
        request.Content = form;

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "xAI voice-note transcription failed with HTTP {StatusCode}",
                (int)response.StatusCode);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseJson);
        return document.RootElement.TryGetProperty("text", out var transcription)
            ? transcription.GetString()?.Trim()
            : null;
    }

    private static object CreateRequestBody(string model, object responseInput) => new
    {
        model,
        input = responseInput,
        reasoning = new { effort = "low" },
        text = new { format = new { type = "json_object" } },
        max_output_tokens = 4_000,
        store = false
    };

    private static object CreateImageInput(string prompt, CustomerReplyAttachment attachment) => new[]
    {
        new
        {
            role = "user",
            content = new object[]
            {
                new { type = "input_text", text = prompt },
                new
                {
                    type = "input_image",
                    image_url = $"data:{attachment.MimeType};base64,{Convert.ToBase64String(attachment.Bytes)}"
                }
            }
        }
    };

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path, string apiKey)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        return request;
    }
}
