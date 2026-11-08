namespace Capri.Core.Models;

/// <summary>
/// An incremental chunk produced while streaming a chat completion.
/// </summary>
/// <param name="Content">The text fragment carried by this chunk.</param>
/// <param name="Role">The author role when the provider reports it with a chunk.</param>
/// <param name="FinishReason">Provider-specific reason the stream ended.</param>
/// <param name="Error">Provider error details when the chunk represents a failure.</param>
public sealed record ChatDelta(
    string Content,
    ChatRole? Role = null,
    string? FinishReason = null,
    ProviderError? Error = null);
