namespace Capri.Core.Models;

/// <summary>
/// A request to generate one or more assistant messages.
/// </summary>
/// <param name="Messages">The conversation history to send to the provider.</param>
/// <param name="ModelId">Optional model override; otherwise the router's choice is used.</param>
/// <param name="Temperature">Optional sampling temperature.</param>
/// <param name="MaxOutputTokens">Optional cap on generated tokens.</param>
public sealed record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    string? ModelId = null,
    double? Temperature = null,
    int? MaxOutputTokens = null);
