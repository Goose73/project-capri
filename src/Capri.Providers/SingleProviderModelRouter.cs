using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Providers;

/// <summary>
/// Phase 1 router that always selects the single configured chat provider and
/// its configured model. A preferred model on the request wins over settings.
/// No model is invented when neither source supplies one: the provider reports
/// a configuration error instead, so the router never falls back to a paid
/// cloud default.
/// </summary>
public sealed class SingleProviderModelRouter : IModelRouter
{
    private readonly IChatProvider _provider;
    private readonly IAppSettingsStore _settings;

    /// <summary>
    /// Creates a router over the configured provider.
    /// </summary>
    public SingleProviderModelRouter(IChatProvider provider, IAppSettingsStore settings)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public ValueTask<ModelRoute> ResolveAsync(
        ModelRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var providerSettings = _settings.Current.Provider;
        var modelId = !string.IsNullOrWhiteSpace(request.PreferredModelId)
            ? request.PreferredModelId!.Trim()
            : providerSettings.ChatModel?.Trim() ?? string.Empty;

        var route = new ModelRoute(
            ResolveProviderId(providerSettings),
            modelId,
            providerSettings.EnableStreaming);

        return ValueTask.FromResult(route);
    }

    private string ResolveProviderId(AppSettings.ProviderSettings providerSettings)
    {
        // Resolve the active profile through the catalog so known providers use
        // their canonical id. Unknown ids are preserved rather than replaced by
        // an arbitrary default; a blank id falls back to the injected provider.
        var profile = FreeModelGuard.ResolveProfile(providerSettings);
        if (profile is not null && !string.IsNullOrWhiteSpace(profile.Id))
        {
            return profile.Id;
        }

        return string.IsNullOrWhiteSpace(providerSettings.ProviderId)
            ? _provider.ProviderId
            : providerSettings.ProviderId.Trim();
    }
}
