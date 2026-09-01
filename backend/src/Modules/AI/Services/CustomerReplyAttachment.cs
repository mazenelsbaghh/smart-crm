namespace Modules.AI.Services;

public sealed record CustomerReplyAttachment(byte[] Bytes, string MimeType);

internal static class CustomerReplyAttachmentFileName
{
    public static string ForVoiceNote(string mimeType) => $"voice-note.{mimeType.ToLowerInvariant() switch
    {
        "audio/mpeg" => "mp3",
        "audio/mp4" => "m4a",
        "audio/wav" or "audio/x-wav" => "wav",
        "audio/webm" => "webm",
        _ => "ogg"
    }}";
}
