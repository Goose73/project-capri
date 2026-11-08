using Capri.Core.Models;
using Capri.Core.Providers;

namespace Capri.Providers;

/// <summary>
/// Pure, fail-closed policy that decides whether a provider/model pair may be
/// used. Local profiles are always allowed. Cloud profiles require explicit
/// cloud-inference consent and, when free-models-only mode is enabled, a model
/// that is known to be free. The guard performs no I/O and never invents or
/// silently substitutes a paid model.
/// </summary>
public static class FreeModelGuard
{
    /// <summary>
    /// Message returned when a cloud request is attempted without explicit
    /// consent. Callers can surface this verbatim to the user.
    /// </summary>
    public const string CloudInferenceDisabledMessage =
        "Cloud inference is disabled. Enable cloud inference and confirm consent before using this provider.";

    /// <summary>
    /// Message returned when free-models-only mode rejects the selected model.
    /// The model identifier is intentionally not echoed to avoid reflecting
    /// provider or user content back through error surfaces.
    /// </summary>
    public const string FreeModelBlockedMessage =
        "Free models only is enabled and the selected model is not allowed.";

    /// <summary>
    /// Message returned when free-models-only mode is active but no model has
    /// been selected. An empty model must never fall through to a paid default.
    /// </summary>
    public const string NoFreeModelSelectedMessage =
        "Free models only is enabled but no model is selected.";

    // Conservative allowlist for cloud providers that do not expose a naming
    // convention for free models. It is intentionally empty today: unknown
    // cloud models fail closed rather than being treated as free.
    private static readonly HashSet<string> FreeModelAllowlist = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Model ids that are explicitly allowed in free-models-only mode even when
    /// the provider does not use the <c>:free</c> suffix convention.
    /// </summary>
    public static IReadOnlyCollection<string> AllowedFreeModels => FreeModelAllowlist.ToArray();

    /// <summary>
    /// Resolves the active provider profile from settings. Known catalog ids
    /// resolve to their preset. Unknown ids that point at a loopback HTTP
    /// endpoint are treated as local (matching credential-isolation behavior);
    /// all other unknown ids are treated as cloud and must fail closed.
    /// </summary>
    public static ProviderProfile? ResolveProfile(AppSettings.ProviderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var providerId = string.IsNullOrWhiteSpace(settings.ProviderId)
            ? null
            : settings.ProviderId.Trim();

        if (ProviderCatalog.TryGetById(providerId, out var profile))
        {
            return profile;
        }

        if (ProviderCatalog.IsLoopbackAllowedHttp(settings.BaseUrl))
        {
            return new ProviderProfile
            {
                Id = providerId ?? string.Empty,
                DisplayName = providerId ?? "Unknown local provider",
                DefaultBaseUrl = settings.BaseUrl?.Trim() ?? string.Empty,
                IsLocal = true,
                RequiresApiKey = false,
                SupportsFreeModelsOnly = true,
            };
        }

        return null;
    }

    /// <summary>
    /// Decides whether a chat completion/stream may be sent for the supplied
    /// profile and model.
    /// </summary>
    /// <param name="settings">Active provider settings (consent and free-mode flags).</param>
    /// <param name="profile">Resolved provider profile, or null when unknown.</param>
    /// <param name="modelId">Requested or configured model id, if any.</param>
    public static FreeModelGuardDecision Evaluate(
        AppSettings.ProviderSettings settings,
        ProviderProfile? profile,
        string? modelId)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Local profiles are always allowed and never require cloud consent.
        if (profile is { IsLocal: true })
        {
            return FreeModelGuardDecision.Allowed;
        }

        // Unknown profiles are treated as cloud: no consent means no request.
        if (!settings.AllowCloudInference)
        {
            return FreeModelGuardDecision.Deny(
                ProviderErrorKind.Configuration,
                CloudInferenceDisabledMessage);
        }

        if (!settings.FreeModelsOnly)
        {
            return FreeModelGuardDecision.Allowed;
        }

        var trimmedModel = modelId?.Trim();
        if (string.IsNullOrEmpty(trimmedModel))
        {
            return FreeModelGuardDecision.Deny(
                ProviderErrorKind.Configuration,
                NoFreeModelSelectedMessage);
        }

        return IsAllowedFreeModel(profile, trimmedModel)
            ? FreeModelGuardDecision.Allowed
            : FreeModelGuardDecision.Deny(
                ProviderErrorKind.InvalidRequest,
                FreeModelBlockedMessage);
    }

    /// <summary>
    /// Decides whether a health probe may contact the remote provider. Health
    /// probes use the same cloud-consent gate as chat because they still reach
    /// the remote host. Free-models-only mode does not apply because a probe
    /// does not select a chat model.
    /// </summary>
    public static FreeModelGuardDecision EvaluateHealth(
        AppSettings.ProviderSettings settings,
        ProviderProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (profile is { IsLocal: true })
        {
            return FreeModelGuardDecision.Allowed;
        }

        return settings.AllowCloudInference
            ? FreeModelGuardDecision.Allowed
            : FreeModelGuardDecision.Deny(
                ProviderErrorKind.Configuration,
                CloudInferenceDisabledMessage);
    }

    private static bool IsAllowedFreeModel(ProviderProfile? profile, string modelId)
    {
        if (FreeModelAllowlist.Contains(modelId))
        {
            return true;
        }

        if (profile is null || !profile.SupportsFreeModelsOnly)
        {
            return false;
        }

        // OpenRouter marks free variants with a ":free" suffix.
        return string.Equals(profile.Id, ProviderCatalog.OpenRouterId, StringComparison.OrdinalIgnoreCase)
            && modelId.EndsWith(":free", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Result of a <see cref="FreeModelGuard"/> evaluation.
/// </summary>
public readonly record struct FreeModelGuardDecision(
    bool IsAllowed,
    ProviderErrorKind ErrorKind,
    string? Message)
{
    /// <summary>A decision that permits the operation.</summary>
    public static FreeModelGuardDecision Allowed { get; } =
        new(true, ProviderErrorKind.Unknown, null);

    /// <summary>Creates a fail-closed decision with a user-safe message.</summary>
    public static FreeModelGuardDecision Deny(ProviderErrorKind errorKind, string message)
    {
        return new FreeModelGuardDecision(false, errorKind, message);
    }

    /// <summary>
    /// Converts a denied decision into a provider error. Only call when
    /// <see cref="IsAllowed"/> is false.
    /// </summary>
    public ProviderError ToError()
    {
        return new ProviderError(ErrorKind, Message ?? string.Empty);
    }
}
