using System.Diagnostics.CodeAnalysis;

namespace Capri.Core.Providers;

/// <summary>
/// Static catalog of known OpenAI-compatible provider presets. This type is
/// pure data: it performs no network or file I/O and contains no secrets.
/// </summary>
public static class ProviderCatalog
{
    /// <summary>Stable identifier for the Ollama provider.</summary>
    public const string OllamaId = "ollama";

    /// <summary>Stable identifier for the LM Studio provider.</summary>
    public const string LmStudioId = "lmstudio";

    /// <summary>Stable identifier for the OpenRouter provider.</summary>
    public const string OpenRouterId = "openrouter";

    /// <summary>Stable identifier for the OpenAI provider.</summary>
    public const string OpenAiId = "openai";

    /// <summary>Ollama runs locally and needs no API key.</summary>
    public static ProviderProfile Ollama { get; } = new()
    {
        Id = OllamaId,
        DisplayName = "Ollama",
        DefaultBaseUrl = "http://localhost:11434/v1",
        RequiresApiKey = false,
        IsLocal = true,
        SupportsFreeModelsOnly = true,
    };

    /// <summary>LM Studio runs locally and needs no API key.</summary>
    public static ProviderProfile LmStudio { get; } = new()
    {
        Id = LmStudioId,
        DisplayName = "LM Studio",
        DefaultBaseUrl = "http://127.0.0.1:1234/v1",
        RequiresApiKey = false,
        IsLocal = true,
        SupportsFreeModelsOnly = true,
    };

    /// <summary>OpenRouter is a cloud service that can serve free model ids.</summary>
    public static ProviderProfile OpenRouter { get; } = new()
    {
        Id = OpenRouterId,
        DisplayName = "OpenRouter",
        DefaultBaseUrl = "https://openrouter.ai/api/v1",
        RequiresApiKey = true,
        IsLocal = false,
        SupportsFreeModelsOnly = true,
    };

    /// <summary>OpenAI is a cloud service that requires an API key.</summary>
    public static ProviderProfile OpenAi { get; } = new()
    {
        Id = OpenAiId,
        DisplayName = "OpenAI",
        DefaultBaseUrl = "https://api.openai.com/v1",
        RequiresApiKey = true,
        IsLocal = false,
        SupportsFreeModelsOnly = false,
    };

    /// <summary>All known provider profiles in stable display order.</summary>
    public static IReadOnlyList<ProviderProfile> All { get; } =
        Array.AsReadOnly(new[] { Ollama, LmStudio, OpenRouter, OpenAi });

    private static readonly Dictionary<string, ProviderProfile> ProfilesById =
        All.ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, ProviderProfile> ProfilesByBaseUrl =
        All.ToDictionary(
            profile => NormalizeBaseUrl(profile.DefaultBaseUrl),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Looks up a provider profile by its stable identifier. Lookup is
    /// case-insensitive and ignores surrounding whitespace.
    /// </summary>
    public static bool TryGetById(
        string? providerId,
        [NotNullWhen(true)] out ProviderProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(providerId)
            || !ProfilesById.TryGetValue(providerId.Trim(), out profile))
        {
            profile = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Looks up a provider profile by its default base URL. Lookup ignores
    /// surrounding whitespace and trailing slashes.
    /// </summary>
    public static bool TryGetByBaseUrl(
        string? baseUrl,
        [NotNullWhen(true)] out ProviderProfile? profile)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl)
            && ProfilesByBaseUrl.TryGetValue(NormalizeBaseUrl(baseUrl), out profile))
        {
            return true;
        }

        profile = null;
        return false;
    }

    /// <summary>
    /// Returns the default base URL for a known provider, or an empty string
    /// for an unknown provider.
    /// </summary>
    public static string GetDefaultBaseUrl(string? providerId)
    {
        return TryGetById(providerId, out var profile)
            ? profile.DefaultBaseUrl
            : string.Empty;
    }

    /// <summary>
    /// True when the provider identifier is known and requires an API key.
    /// Unknown providers return false.
    /// </summary>
    public static bool RequiresApiKey(string? providerId)
    {
        return TryGetById(providerId, out var profile) && profile.RequiresApiKey;
    }

    /// <summary>
    /// True when the provider identifier is known and runs locally.
    /// Unknown providers return false.
    /// </summary>
    public static bool IsLocal(string? providerId)
    {
        return TryGetById(providerId, out var profile) && profile.IsLocal;
    }

    /// <summary>
    /// True when the supplied base URL uses plain HTTP against a loopback
    /// host. Non-HTTP, malformed, and remote-host URLs return false.
    /// </summary>
    public static bool IsLoopbackAllowedHttp(string? baseUrl)
    {
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && uri.IsLoopback;
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return baseUrl.Trim().TrimEnd('/');
    }
}
