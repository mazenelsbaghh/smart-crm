using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Modules.AI.Services;

public sealed record OpenAiCustomerReplyRequest(
    string Prompt,
    string? ApiKey,
    string Model,
    CustomerReplyAttachment? Attachment = null);

public sealed class OpenAiResponsesClient(HttpClient httpClient, ILogger<OpenAiResponsesClient> logger)
{
    private const string ApiError = "[AI_ERROR] Unable to reach OpenAI.";
    private const string TranscriptionModel = "gpt-4o-mini-transcribe";

    public async Task<string> GenerateReplyAsync(
        OpenAiCustomerReplyRequest replyRequest,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(replyRequest.ApiKey))
        {
            logger.LogWarning("OpenAI customer-reply generation was skipped because the project API key is missing");
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
            logger.LogWarning("OpenAI customer-reply request timed out for model {Model}", replyRequest.Model);
            return ApiError;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or FormatException)
        {
            logger.LogWarning(exception, "OpenAI customer-reply generation failed for model {Model}", replyRequest.Model);
            return ApiError;
        }
    }

    private async Task<object?> BuildResponseInputAsync(
        OpenAiCustomerReplyRequest replyRequest,
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

        return attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? CreateImageInput(replyRequest.Prompt, attachment)
            : replyRequest.Prompt;
    }

    private async Task<string> SendResponseRequestAsync(
        OpenAiCustomerReplyRequest replyRequest,
        object responseInput,
        CancellationToken cancellationToken)
    {
        using var httpRequest = CreateAuthorizedRequest(HttpMethod.Post, "v1/responses", replyRequest.ApiKey!);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(CreateResponseBody(replyRequest.Model, responseInput)),
            Encoding.UTF8,
            "application/json");

        using var httpResponse = await httpClient.SendAsync(httpRequest, cancellationToken);
        if (!httpResponse.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OpenAI customer-reply request failed with HTTP {StatusCode} for model {Model}",
                (int)httpResponse.StatusCode,
                replyRequest.Model);
            return ApiError;
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        return ResponsesApiOutputText.Extract(responseJson) ?? ApiError;
    }

    private async Task<string?> TranscribeAudioAsync(
        CustomerReplyAttachment attachment,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var httpRequest = CreateAuthorizedRequest(HttpMethod.Post, "v1/audio/transcriptions", apiKey);
        using var form = CreateTranscriptionForm(attachment);
        httpRequest.Content = form;
        using var httpResponse = await httpClient.SendAsync(httpRequest, cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OpenAI voice-note transcription failed with HTTP {StatusCode}",
                (int)httpResponse.StatusCode);
            return null;
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseJson);
        return document.RootElement.TryGetProperty("text", out var transcription)
            ? transcription.GetString()?.Trim()
            : null;
    }

    private static object CreateResponseBody(string model, object responseInput) => new
    {
        model,
        input = responseInput,
        reasoning = new { effort = "low" },
        text = new
        {
            format = new { type = "json_object" },
            verbosity = "low"
        },
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

    private static MultipartFormDataContent CreateTranscriptionForm(CustomerReplyAttachment attachment)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(TranscriptionModel), "model");
        var audioContent = new ByteArrayContent(attachment.Bytes);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(attachment.MimeType);
        form.Add(audioContent, "file", CustomerReplyAttachmentFileName.ForVoiceNote(attachment.MimeType));
        return form;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path, string apiKey)
    {
        var httpRequest = new HttpRequestMessage(method, path);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        return httpRequest;
    }
}
