using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Modules.Content.Services;

public sealed record GeminiOmniVideoRequest(
    string EnterpriseProjectId,
    string Prompt,
    string AspectRatio,
    string Resolution,
    int DurationSeconds,
    string? ApiKey,
    byte[]? FirstFramePng = null);

public enum GeminiOmniInteractionStatus
{
    InProgress,
    Completed,
    Failed,
    Cancelled,
    Incomplete,
    RequiresAction
}

public sealed record GeminiOmniInteraction(
    string InteractionId,
    GeminiOmniInteractionStatus Status,
    byte[]? VideoBytes,
    string? VideoMimeType)
{
    public bool IsPending => Status == GeminiOmniInteractionStatus.InProgress;
    public bool IsCompleted => Status == GeminiOmniInteractionStatus.Completed;
    public bool IsTerminalFailure => Status is GeminiOmniInteractionStatus.Failed
        or GeminiOmniInteractionStatus.Cancelled
        or GeminiOmniInteractionStatus.Incomplete;
    public bool RequiresAction => Status == GeminiOmniInteractionStatus.RequiresAction;
}

public sealed class GeminiOmniRetryableException(
    string code,
    string safeMessage,
    TimeSpan? retryAfter = null) : ContentVideoException(code, safeMessage)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class GeminiOmniSubmissionUncertainException(
    string code,
    string safeMessage,
    string? interactionId = null) : ContentVideoException(code, safeMessage)
{
    public string? InteractionId { get; } = interactionId;
}

public sealed class GeminiOmniVideoClient(HttpClient httpClient)
{
    private const int MaximumInteractionIdLength = 500;
    private const int MaximumInlineResponseBytes = 64 * 1024 * 1024;

    public async Task<GeminiOmniInteraction> SubmitAsync(
        GeminiOmniVideoRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var input = new List<object>
        {
            new { type = "text", text = request.Prompt }
        };
        if (request.FirstFramePng is { Length: > 0 } firstFrame)
        {
            input.Add(new
            {
                type = "image",
                data = Convert.ToBase64String(firstFrame),
                mime_type = "image/png"
            });
        }

        var body = new
        {
            model = ContentVideoCapabilities.Model,
            background = true,
            input,
            response_format = new[]
            {
                new
                {
                    type = "video",
                    aspect_ratio = request.AspectRatio,
                    resolution = request.Resolution,
                    duration = $"{request.DurationSeconds}s"
                }
            },
            generation_config = new
            {
                video_config = new
                {
                    task = request.FirstFramePng is { Length: > 0 }
                        ? "image_to_video"
                        : "text_to_video"
                }
            }
        };

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            InteractionCollectionPath(request.EnterpriseProjectId))
        {
            Content = JsonContent.Create(body)
        };
        Authenticate(message, request.ApiKey);
        return await SendAsync(message, OmniRequestKind.Submission, cancellationToken);
    }

    public async Task<GeminiOmniInteraction> GetAsync(
        string enterpriseProjectId,
        string interactionId,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interactionId))
            throw new ContentVideoException("OMNI_INTERACTION_ID_MISSING", "معرّف توليد المشهد غير موجود.");
        ValidateEnterpriseProjectId(enterpriseProjectId);

        var path = $"{InteractionCollectionPath(enterpriseProjectId)}/{Uri.EscapeDataString(interactionId)}";
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        Authenticate(message, apiKey);
        return await SendAsync(message, OmniRequestKind.Poll, cancellationToken);
    }

    private async Task<GeminiOmniInteraction> SendAsync(
        HttpRequestMessage message,
        OmniRequestKind requestKind,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            EnsureSuccessfulResponse(response, requestKind);
            return await ParseResponseAsync(response.Content, requestKind, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TransportFailure(requestKind, "OMNI_TIMEOUT", "انتهت مهلة الاتصال بخدمة توليد الفيديو.");
        }
        catch (HttpRequestException)
        {
            throw TransportFailure(requestKind, "OMNI_UNAVAILABLE", "تعذر الاتصال بخدمة توليد الفيديو.");
        }
        catch (IOException)
        {
            throw TransportFailure(requestKind, "OMNI_UNAVAILABLE", "تعذر الاتصال بخدمة توليد الفيديو.");
        }
    }

    private static void EnsureSuccessfulResponse(
        HttpResponseMessage response,
        OmniRequestKind requestKind)
    {
        if (response.IsSuccessStatusCode) return;

        var statusCode = (int)response.StatusCode;
        var retryAfter = RetryAfter(response.Headers);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new GeminiOmniRetryableException(
                "OMNI_HTTP_429",
                "حصة Gemini مشغولة مؤقتًا. ستتم إعادة المحاولة تلقائيًا.",
                retryAfter);
        }

        if (response.StatusCode == HttpStatusCode.RequestTimeout || statusCode >= 500)
        {
            if (requestKind == OmniRequestKind.Poll)
            {
                throw new GeminiOmniRetryableException(
                    $"OMNI_HTTP_{statusCode}",
                    "تعذر فحص حالة المشهد مؤقتًا. ستتم إعادة المحاولة تلقائيًا.",
                    retryAfter);
            }

            throw new GeminiOmniSubmissionUncertainException(
                $"OMNI_HTTP_{statusCode}_SUBMISSION_UNCERTAIN",
                "تعذر التأكد هل استلم Gemini طلب المشهد. يلزم تأكيد يدوي قبل إعادة المحاولة.");
        }

        throw new ContentVideoException(
            $"OMNI_HTTP_{statusCode}",
            "رفض Google Cloud طلب توليد الفيديو. راجع تفعيل Agent Platform والصلاحيات والحصة.");
    }

    private static async Task<GeminiOmniInteraction> ParseResponseAsync(
        HttpContent content,
        OmniRequestKind requestKind,
        CancellationToken cancellationToken)
    {
        JsonDocument? document = null;
        try
        {
            var responseBytes = await ReadBoundedResponseAsync(content, cancellationToken);
            document = JsonDocument.Parse(responseBytes);
            return ParseInteraction(document.RootElement);
        }
        catch (ContentVideoException) when (requestKind == OmniRequestKind.Submission)
        {
            throw MalformedSubmission(SubmissionInteractionId(document));
        }
        catch (JsonException) when (requestKind == OmniRequestKind.Submission)
        {
            throw MalformedSubmission(SubmissionInteractionId(document));
        }
        catch (FormatException) when (requestKind == OmniRequestKind.Submission)
        {
            throw MalformedSubmission(SubmissionInteractionId(document));
        }
        catch (InvalidOperationException) when (requestKind == OmniRequestKind.Submission)
        {
            throw MalformedSubmission(SubmissionInteractionId(document));
        }
        catch (JsonException)
        {
            throw new ContentVideoException("OMNI_INVALID_RESPONSE", "أعادت خدمة الفيديو استجابة غير صالحة.");
        }
        catch (FormatException)
        {
            throw new ContentVideoException("OMNI_INVALID_VIDEO_DATA", "بيانات الفيديو المولدة غير صالحة.");
        }
        catch (InvalidOperationException)
        {
            throw new ContentVideoException("OMNI_INVALID_RESPONSE", "أعادت خدمة الفيديو استجابة غير صالحة.");
        }
        finally
        {
            document?.Dispose();
        }
    }

    private static async Task<byte[]> ReadBoundedResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumInlineResponseBytes)
            throw ResponseTooLarge();

        var initialCapacity = content.Headers.ContentLength is > 0
            ? (int)content.Headers.ContentLength.Value
            : 0;
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream(initialCapacity);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0) break;
            if (destination.Length + bytesRead > MaximumInlineResponseBytes)
                throw ResponseTooLarge();
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
        return destination.ToArray();
    }

    private static void Authenticate(HttpRequestMessage message, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ContentVideoException(
                "OMNI_AUTH_MISSING",
                "أضف مفتاح Gemini في إعدادات المشروع.");
        message.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
    }

    private static GeminiOmniInteraction ParseInteraction(JsonElement root)
    {
        var id = InteractionId(root);
        var rawStatus = root.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(rawStatus))
            throw new ContentVideoException("OMNI_INVALID_RESPONSE", "أعادت خدمة الفيديو استجابة غير صالحة.");

        var status = ParseStatus(rawStatus);
        var videoOutput = VideoOutput(root);
        if (status == GeminiOmniInteractionStatus.Completed && videoOutput.VideoBytes is null)
        {
            throw new ContentVideoException(
                "OMNI_VIDEO_DATA_MISSING",
                "اكتمل توليد المشهد لكن ملف الفيديو غير موجود في الاستجابة.");
        }

        return new GeminiOmniInteraction(
            id,
            status,
            videoOutput.VideoBytes,
            videoOutput.MimeType);
    }

    private static GeminiOmniVideoOutput VideoOutput(JsonElement root)
    {
        if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            return new GeminiOmniVideoOutput(null, null);

        foreach (var step in steps.EnumerateArray())
        {
            if (!IsModelOutput(step, out var content)) continue;
            foreach (var contentBlock in content.EnumerateArray())
            {
                if (!IsInlineVideo(contentBlock, out var encodedVideo)) continue;
                var comma = encodedVideo.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    ? encodedVideo.IndexOf(',')
                    : -1;
                var videoBytes = Convert.FromBase64String(
                    comma >= 0 ? encodedVideo[(comma + 1)..] : encodedVideo);
                var mimeType = contentBlock.TryGetProperty("mime_type", out var mimeElement)
                    ? mimeElement.GetString()
                    : "video/mp4";
                return new GeminiOmniVideoOutput(videoBytes, mimeType);
            }
        }

        return new GeminiOmniVideoOutput(null, null);
    }

    private static bool IsModelOutput(JsonElement step, out JsonElement content)
    {
        content = default;
        return step.TryGetProperty("type", out var stepType)
            && string.Equals(stepType.GetString(), "model_output", StringComparison.Ordinal)
            && step.TryGetProperty("content", out content)
            && content.ValueKind == JsonValueKind.Array;
    }

    private static bool IsInlineVideo(JsonElement contentBlock, out string encodedVideo)
    {
        encodedVideo = string.Empty;
        if (!contentBlock.TryGetProperty("type", out var contentType)
            || !string.Equals(contentType.GetString(), "video", StringComparison.Ordinal)
            || !contentBlock.TryGetProperty("data", out var dataElement))
        {
            return false;
        }

        encodedVideo = dataElement.GetString() ?? string.Empty;
        return encodedVideo.Length > 0;
    }

    private static GeminiOmniInteractionStatus ParseStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "in_progress" => GeminiOmniInteractionStatus.InProgress,
            "completed" => GeminiOmniInteractionStatus.Completed,
            "failed" => GeminiOmniInteractionStatus.Failed,
            "cancelled" => GeminiOmniInteractionStatus.Cancelled,
            "incomplete" => GeminiOmniInteractionStatus.Incomplete,
            "requires_action" => GeminiOmniInteractionStatus.RequiresAction,
            _ => throw new ContentVideoException(
                "OMNI_STATUS_UNKNOWN",
                "أعاد Gemini حالة توليد غير معروفة؛ تم إيقاف المشهد بأمان.")
        };

    private static string? SubmissionInteractionId(JsonDocument? document)
        => document is null ? null : InteractionId(document.RootElement);

    private static string? InteractionId(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var interactionId = idElement.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(interactionId)
            || interactionId.Length > MaximumInteractionIdLength
            || interactionId.Any(char.IsControl)
                ? null
                : interactionId;
    }

    private static void Validate(GeminiOmniVideoRequest request)
    {
        ValidateEnterpriseProjectId(request.EnterpriseProjectId);
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ContentVideoException("OMNI_PROMPT_MISSING", "وصف المشهد غير موجود.");
        if (!ContentVideoCapabilities.AspectRatios.Contains(request.AspectRatio))
            throw new ContentVideoException("OMNI_ASPECT_RATIO_INVALID", "مقاس الفيديو غير مدعوم.");
        if (!ContentVideoCapabilities.Resolutions.Contains(request.Resolution))
            throw new ContentVideoException("OMNI_RESOLUTION_INVALID", "دقة الفيديو غير مدعومة.");
        if (request.DurationSeconds is < ContentVideoCapabilities.MinimumDurationSeconds
            or > ContentVideoCapabilities.MaximumDurationSeconds)
        {
            throw new ContentVideoException("OMNI_DURATION_INVALID", "مدة المشهد غير مدعومة.");
        }
        if (request.FirstFramePng is { Length: > 20 * 1024 * 1024 })
            throw new ContentVideoException("OMNI_IMAGE_TOO_LARGE", "إطار الربط أكبر من الحد المسموح.");
    }

    private static void ValidateEnterpriseProjectId(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)
            || projectId.Length is < 6 or > 30
            || !char.IsAsciiLetterLower(projectId[0])
            || !char.IsAsciiLetterOrDigit(projectId[^1])
            || projectId.Any(character =>
                !(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-')))
        {
            throw new ContentVideoException("OMNI_PROJECT_ID_INVALID", "Google Cloud Project ID غير صالح.");
        }
    }

    private static ContentVideoException TransportFailure(
        OmniRequestKind requestKind,
        string code,
        string safeMessage) =>
        requestKind == OmniRequestKind.Poll
            ? new GeminiOmniRetryableException(code, safeMessage)
            : new GeminiOmniSubmissionUncertainException(
                $"{code}_SUBMISSION_UNCERTAIN",
                "تعذر التأكد هل استلم Gemini طلب المشهد. يلزم تأكيد يدوي قبل إعادة المحاولة.");

    private static TimeSpan? RetryAfter(HttpResponseHeaders headers)
    {
        if (headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            return delta;
        if (headers.RetryAfter?.Date is DateTimeOffset date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : null;
        }
        return null;
    }

    private static GeminiOmniSubmissionUncertainException MalformedSubmission(
        string? interactionId) => new(
        "OMNI_SUBMISSION_RESPONSE_UNCERTAIN",
        interactionId is null
            ? "أرسل Gemini استجابة غير مكتملة بعد طلب المشهد؛ يلزم تأكيد يدوي قبل إعادة المحاولة."
            : "استلم Gemini طلب المشهد لكن تعذر تفسير حالته؛ يمكن استكمال فحصه بأمان.",
        interactionId);

    private static ContentVideoException ResponseTooLarge() => new(
        "OMNI_RESPONSE_TOO_LARGE",
        "استجابة الفيديو أكبر من الحد الآمن للمعالجة.");

    private static string InteractionCollectionPath(string projectId) =>
        $"v1beta1/projects/{Uri.EscapeDataString(projectId)}/locations/global/interactions";

    private enum OmniRequestKind
    {
        Submission,
        Poll
    }

    private sealed record GeminiOmniVideoOutput(byte[]? VideoBytes, string? MimeType);
}
