namespace Capri.Core.Models;

/// <summary>
/// A completed non-streaming chat response.
/// </summary>
/// <param name="Message">The assistant message produced by the provider.</param>
/// <param name="ModelId">The model that produced the response, when reported.</param>
/// <param name="FinishReason">Provider-specific reason the response ended.</param>
/// <param name="Usage">Token accounting, when reported.</param>
/// <param name="Error">Provider error details when the response represents a failure.</param>
public sealed record ChatCompletion(
    ChatMessage Message,
    string? ModelId = null,
    string? FinishReason = null,
    TokenUsage? Usage = null,
    ProviderError? Error = null);
