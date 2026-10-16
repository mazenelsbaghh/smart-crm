namespace Modules.AI.Services;

public sealed class DeferredAiReplyException() : Exception("AI generation is temporarily unavailable or returned unusable output.");
