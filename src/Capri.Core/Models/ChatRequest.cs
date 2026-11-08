namespace Capri.Core.Models;

/// <summary>
/// A request to generate one or more assistant messages.
/// </summary>
/// <param name="Messages">The conversation history to send to the provider.</param>
/// <param name="ModelId">Optional model override; otherwise the router's choice is used.</param>
/// <param name="Temperature">Optional sampling temperature.</param>
/// <param name="MaxOutputTokens">Optional cap on generated tokens.</param>
/// <param name="RequireJsonObject">
/// Optional hint that the response must be a single JSON object. Providers that
/// support OpenAI-compatible response formats map this to
/// <c>response_format: {"type":"json_object"}</c>. Defaults to false so existing
/// requests are unchanged.
/// </param>
public sealed record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    string? ModelId = null,
    double? Temperature = null,
    int? MaxOutputTokens = null,
    bool RequireJsonObject = false);
