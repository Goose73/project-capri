namespace Capri.Core.Models;

/// <summary>
/// Inputs used to select a provider and model.
/// </summary>
/// <param name="PreferredModelId">Optional caller preference for a specific model.</param>
/// <param name="RequiresStreaming">True when the response must be streamed.</param>
/// <param name="RequiresTools">True when tool calling is required.</param>
/// <param name="EstimatedPromptTokens">Optional prompt-size estimate used for capacity routing.</param>
public sealed record ModelRouteRequest(
    string? PreferredModelId = null,
    bool RequiresStreaming = true,
    bool RequiresTools = false,
    int? EstimatedPromptTokens = null);
