using Capri.Core.Companion;

namespace Capri.Core.Models;

/// <summary>
/// User-configurable application settings. Secrets are intentionally excluded;
/// API keys and similar values belong in the secure credential store.
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// Schema version used for forward-compatible migrations. Version 2 adds
    /// provider inference-safety settings.
    /// </summary>
    public int SchemaVersion { get; init; } = 2;

    /// <summary>
    /// Model provider configuration.
    /// </summary>
    public ProviderSettings Provider { get; init; } = new();

    /// <summary>
    /// Desktop automation configuration.
    /// </summary>
    public AutomationSettings Automation { get; init; } = new();

    /// <summary>
    /// Desktop UI configuration.
    /// </summary>
    public UiSettings Ui { get; init; } = new();

    /// <summary>
    /// Voice input and output configuration.
    /// </summary>
    public VoiceSettings Voice { get; init; } = new();

    /// <summary>
    /// Future optional local webcam companion preferences. Disabled by default.
    /// A saved Enabled preference is never sufficient to begin camera capture:
    /// each application session requires fresh, explicit user consent.
    /// </summary>
    public AmbientVisionSettings AmbientVision { get; init; } = new();

    /// <summary>
    /// Chat provider configuration. No API keys or other secrets are stored here.
    /// </summary>
    public sealed record ProviderSettings
    {
        /// <summary>
        /// Identifier of the selected provider implementation.
        /// </summary>
        public string ProviderId { get; init; } = string.Empty;

        /// <summary>
        /// Base URL of the provider endpoint.
        /// </summary>
        public string BaseUrl { get; init; } = string.Empty;

        /// <summary>
        /// Default chat model identifier.
        /// </summary>
        public string ChatModel { get; init; } = string.Empty;

        /// <summary>
        /// True when responses should be streamed.
        /// </summary>
        public bool EnableStreaming { get; init; } = true;

        /// <summary>
        /// Sampling temperature used when a request does not override it.
        /// </summary>
        public double Temperature { get; init; } = 0.7;

        /// <summary>
        /// Maximum number of output tokens per request.
        /// </summary>
        public int MaxOutputTokens { get; init; } = 1024;

        /// <summary>
        /// HTTP request timeout in seconds.
        /// </summary>
        public int RequestTimeoutSeconds { get; init; } = 60;

        /// <summary>
        /// True when only free (or local) model ids should be selected. Added
        /// in schema version 2; safe default is on.
        /// </summary>
        public bool FreeModelsOnly { get; init; } = true;

        /// <summary>
        /// True when cloud inference has been explicitly allowed. Added in
        /// schema version 2; safe default is off.
        /// </summary>
        public bool AllowCloudInference { get; init; }

        /// <summary>
        /// UTC timestamp of the recorded cloud-inference consent, or null when
        /// no consent has been recorded. Added in schema version 2.
        /// </summary>
        public DateTimeOffset? CloudConsentRecordedUtc { get; init; }
    }

    /// <summary>
    /// Desktop automation configuration.
    /// </summary>
    public sealed record AutomationSettings
    {
        /// <summary>
        /// True when automation actions are enabled.
        /// </summary>
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// True when the assistant may launch applications.
        /// </summary>
        public bool AllowAppLaunch { get; init; } = true;

        /// <summary>
        /// True when the assistant may open web pages.
        /// </summary>
        public bool AllowWebLaunch { get; init; } = true;

        /// <summary>
        /// True when the assistant may send media keys.
        /// </summary>
        public bool AllowMediaControl { get; init; } = true;

        /// <summary>
        /// True when user-visible actions must be confirmed before execution.
        /// </summary>
        public bool RequireConfirmation { get; init; }
    }

    /// <summary>
    /// Desktop UI configuration.
    /// </summary>
    public sealed record UiSettings
    {
        /// <summary>
        /// True when the application should start minimized.
        /// </summary>
        public bool StartMinimized { get; init; }

        /// <summary>
        /// True when the assistant window should stay above other windows.
        /// </summary>
        public bool AlwaysOnTop { get; init; } = true;

        /// <summary>
        /// Theme identifier, for example "System", "Light", or "Dark".
        /// </summary>
        public string Theme { get; init; } = "System";

        /// <summary>True when the avatar should use reduced-motion presentation.</summary>
        public bool ReduceMotion { get; init; }

        /// <summary>
        /// Scale factor applied to the avatar.
        /// </summary>
        public double AvatarScale { get; init; } = 1.0;

        /// <summary>
        /// Visible character/avatar height in device-independent pixels (150–400). Default 250.
        /// </summary>
        public int CharacterHeight { get; init; } = 250;

        /// <summary>
        /// Saved companion window left position in device-independent pixels, or null when no position is saved.
        /// </summary>
        public double? CharacterLeft { get; init; }

        /// <summary>
        /// Saved companion window top position in device-independent pixels, or null when no position is saved.
        /// </summary>
        public double? CharacterTop { get; init; }
    }

    /// <summary>
    /// Voice input and output configuration. Device identifiers are not secrets.
    /// </summary>
    public sealed record VoiceSettings
    {
        /// <summary>
        /// True when voice input and output are enabled.
        /// </summary>
        public bool Enabled { get; init; }

        /// <summary>
        /// True when wake-word activation is enabled.
        /// </summary>
        public bool EnableWakeWord { get; init; }

        /// <summary>
        /// Wake word used to activate listening.
        /// </summary>
        public string WakeWord { get; init; } = "Capri";

        /// <summary>
        /// Speech recognition and synthesis language tag.
        /// </summary>
        public string Language { get; init; } = "en-US";

        /// <summary>
        /// Optional input device identifier.
        /// </summary>
        public string? InputDeviceId { get; init; }

        /// <summary>
        /// Optional output device identifier.
        /// </summary>
        public string? OutputDeviceId { get; init; }

        /// <summary>
        /// Speech rate multiplier.
        /// </summary>
        public double SpeechRate { get; init; } = 1.0;

        /// <summary>
        /// Output volume between 0 and 1.
        /// </summary>
        public double Volume { get; init; } = 1.0;
    }
}
