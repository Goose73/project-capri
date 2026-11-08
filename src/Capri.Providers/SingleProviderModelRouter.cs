using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Providers;

/// <summary>
/// Phase 1 router that always selects the single configured chat provider and
/// its configured model. A preferred model on the request wins over settings.
/// </summary>
public sealed class SingleProviderModelRouter : IModelRouter
{
    private const string FallbackModelId = "gpt-4o-mini";

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
            : !string.IsNullOrWhiteSpace(providerSettings.ChatModel)
                ? providerSettings.ChatModel.Trim()
                : FallbackModelId;

        var route = new ModelRoute(
            _provider.ProviderId,
            modelId,
            providerSettings.EnableStreaming);

        return ValueTask.FromResult(route);
    }
}
