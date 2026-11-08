using System.Text;
using Capri.Core.Providers;

namespace Capri.Providers;

/// <summary>
/// Computes credential-store keys that isolate provider API keys by profile.
/// Keys are derived from the canonical provider identifiers in
/// <see cref="ProviderCatalog"/> and contain no secret material.
/// </summary>
public static class ProviderCredentials
{
    /// <summary>
    /// Legacy shared credential key. It is retained for compatibility and may
    /// only be read during the one-time OpenAI profile migration.
    /// </summary>
    public const string LegacyApiKeyCredentialKey = "provider.apiKey";

    private const string KeyPrefix = "provider.";
    private const string KeySuffix = ".apiKey";
    private const string UnknownProfileId = "unknown";

    /// <summary>
    /// Returns the isolated API-key credential key for a catalog profile, for
    /// example <c>provider.openai.apiKey</c>.
    /// </summary>
    public static string GetApiKeyCredentialKey(ProviderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return GetApiKeyCredentialKey(profile.Id);
    }

    /// <summary>
    /// Returns the isolated API-key credential key for a provider identifier.
    /// Known identifiers resolve to their canonical catalog casing; unknown
    /// identifiers are normalized into an isolated key and never map to the
    /// legacy shared key.
    /// </summary>
    public static string GetApiKeyCredentialKey(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return string.Concat(KeyPrefix, CanonicalizeProviderId(providerId), KeySuffix);
    }

    private static string CanonicalizeProviderId(string providerId)
    {
        if (ProviderCatalog.TryGetById(providerId, out var profile))
        {
            return profile.Id;
        }

        var trimmed = providerId.Trim();
        var builder = new StringBuilder(trimmed.Length);
        foreach (var character in trimmed)
        {
            builder.Append(
                char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                    ? char.ToLowerInvariant(character)
                    : '-');
        }

        return builder.Length == 0 ? UnknownProfileId : builder.ToString();
    }
}
