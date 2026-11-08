namespace Capri.Core.Models;

/// <summary>
/// Provider and model selected for a chat request.
/// </summary>
/// <param name="ProviderId">Identifier of the provider implementation.</param>
/// <param name="ModelId">Identifier of the model to invoke.</param>
/// <param name="SupportsStreaming">True when the selected model supports streaming.</param>
public sealed record ModelRoute(
    string ProviderId,
    string ModelId,
    bool SupportsStreaming = true);
