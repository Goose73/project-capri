using Capri.Core.Models;

namespace Capri.Security;

/// <summary>
/// Builds the default <see cref="AppSettings"/> used before a user settings
/// file exists and when individual provider fields are missing.
/// </summary>
public static class CapriDefaultSettings
{
    /// <summary>Default provider identifier.</summary>
    public const string DefaultProviderId = "openai";

    /// <summary>Default OpenAI-compatible base URL.</summary>
    public const string DefaultBaseUrl = "https://api.openai.com/v1";

    /// <summary>Default chat model.</summary>
    public const string DefaultModelId = "gpt-4o-mini";

    /// <summary>Default request timeout, in seconds.</summary>
    public const int DefaultRequestTimeoutSeconds = 60;

    /// <summary>Default maximum output tokens.</summary>
    public const int DefaultMaxOutputTokens = 1024;

    /// <summary>Default sampling temperature.</summary>
    public const double DefaultTemperature = 0.7;

    /// <summary>
    /// Creates a complete default settings snapshot. No secrets are included.
    /// </summary>
    public static AppSettings Create()
    {
        return new AppSettings
        {
            SchemaVersion = 1,
            Provider = new AppSettings.ProviderSettings
            {
                ProviderId = DefaultProviderId,
                BaseUrl = DefaultBaseUrl,
                ChatModel = DefaultModelId,
                EnableStreaming = true,
                Temperature = DefaultTemperature,
                MaxOutputTokens = DefaultMaxOutputTokens,
                RequestTimeoutSeconds = DefaultRequestTimeoutSeconds,
            },
        };
    }
}
