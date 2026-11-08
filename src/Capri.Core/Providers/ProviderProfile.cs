namespace Capri.Core.Providers;

/// <summary>
/// Immutable description of a known chat provider preset. Profiles contain
/// no secrets and perform no I/O.
/// </summary>
public sealed record ProviderProfile
{
    /// <summary>
    /// Stable provider identifier used by settings and the catalog.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable provider name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Default OpenAI-compatible base URL for the provider.
    /// </summary>
    public required string DefaultBaseUrl { get; init; }

    /// <summary>
    /// True when requests to the provider require a stored API key.
    /// </summary>
    public bool RequiresApiKey { get; init; }

    /// <summary>
    /// True when the provider runs on the local machine.
    /// </summary>
    public bool IsLocal { get; init; }

    /// <summary>
    /// True when the provider can serve free (or free-tier/local) models.
    /// </summary>
    public bool SupportsFreeModelsOnly { get; init; }

    /// <summary>
    /// True when the provider is a cloud service.
    /// </summary>
    public bool IsCloud => !IsLocal;

    /// <summary>
    /// True when the default endpoint is plain HTTP against a loopback host
    /// (local providers only).
    /// </summary>
    public bool IsLoopbackAllowedHttp =>
        IsLocal && ProviderCatalog.IsLoopbackAllowedHttp(DefaultBaseUrl);
}
