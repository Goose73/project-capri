using Capri.Core.Models;
using Capri.Core.Providers;

namespace Capri.Security;

/// <summary>
/// Builds the default <see cref="AppSettings"/> used before a user settings
/// file exists and when individual provider fields are missing.
/// </summary>
public static class CapriDefaultSettings
{
    /// <summary>Current settings schema version.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Default provider identifier.</summary>
    public const string DefaultProviderId = ProviderCatalog.OpenAiId;

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

    /// <summary>Stable identifier for the local Ollama provider.</summary>
    public const string OllamaProviderId = ProviderCatalog.OllamaId;

    /// <summary>Default Ollama OpenAI-compatible base URL.</summary>
    public const string OllamaBaseUrl = "http://localhost:11434/v1";

    /// <summary>Stable identifier for the local LM Studio provider.</summary>
    public const string LmStudioProviderId = ProviderCatalog.LmStudioId;

    /// <summary>Default LM Studio OpenAI-compatible base URL.</summary>
    public const string LmStudioBaseUrl = "http://127.0.0.1:1234/v1";

    /// <summary>
    /// Creates a complete default settings snapshot. No secrets are included.
    /// The legacy default remains OpenAI with the gpt-4o-mini model so that
    /// existing no-file installations keep working unchanged.
    /// </summary>
    public static AppSettings Create()
    {
        return new AppSettings
        {
            SchemaVersion = CurrentSchemaVersion,
            Provider = new AppSettings.ProviderSettings
            {
                ProviderId = DefaultProviderId,
                BaseUrl = DefaultBaseUrl,
                ChatModel = DefaultModelId,
                EnableStreaming = true,
                Temperature = DefaultTemperature,
                MaxOutputTokens = DefaultMaxOutputTokens,
                RequestTimeoutSeconds = DefaultRequestTimeoutSeconds,
                FreeModelsOnly = true,
                AllowCloudInference = false,
            },
        };
    }

    /// <summary>
    /// Returns the catalog default base URL for a known provider, falling back
    /// to the legacy OpenAI default for unknown or blank identifiers.
    /// </summary>
    public static string GetDefaultBaseUrl(string? providerId)
    {
        var catalogDefault = ProviderCatalog.GetDefaultBaseUrl(providerId);
        return string.IsNullOrWhiteSpace(catalogDefault) ? DefaultBaseUrl : catalogDefault;
    }
}
