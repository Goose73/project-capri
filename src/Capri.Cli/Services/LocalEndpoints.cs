using Capri.Core.Models;
using Capri.Core.Providers;

namespace Capri.Cli.Services;

/// <summary>A named local endpoint that can be probed without credentials.</summary>
public sealed record LocalEndpointTarget(string Name, string BaseUrl, string FixHint);

/// <summary>
/// Builds the set of local endpoints shown by <c>status</c> and <c>doctor</c>.
/// Cloud endpoints are never contacted.
/// </summary>
public static class LocalEndpoints
{
    /// <summary>
    /// Returns Ollama and LM Studio defaults plus the active provider's base
    /// URL when it is a local provider at a different address.
    /// </summary>
    public static IReadOnlyList<LocalEndpointTarget> GetTargets(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var targets = new List<LocalEndpointTarget>
        {
            new(
                ProviderCatalog.Ollama.DisplayName,
                ProviderCatalog.Ollama.DefaultBaseUrl,
                "Start Ollama (for example 'ollama serve') and confirm the base URL. Capri never downloads models automatically."),
            new(
                ProviderCatalog.LmStudio.DisplayName,
                ProviderCatalog.LmStudio.DefaultBaseUrl,
                "Start the LM Studio local server and confirm the base URL. Capri never downloads models automatically."),
        };

        var provider = settings.Provider;
        if (ProviderCatalog.TryGetById(provider.ProviderId, out var profile)
            && profile.IsLocal
            && !string.IsNullOrWhiteSpace(provider.BaseUrl)
            && !targets.Any(target => SameUrl(target.BaseUrl, provider.BaseUrl)))
        {
            targets.Add(new LocalEndpointTarget(
                $"{profile.DisplayName} (configured)",
                provider.BaseUrl.Trim(),
                $"Start {profile.DisplayName} and confirm the configured base URL."));
        }

        return targets;
    }

    private static bool SameUrl(string left, string right)
    {
        return string.Equals(
            left.Trim().TrimEnd('/'),
            right.Trim().TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }
}
