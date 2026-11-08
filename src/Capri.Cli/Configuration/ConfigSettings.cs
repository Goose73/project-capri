using System.Globalization;
using Capri.Core.Models;
using Capri.Core.Providers;

namespace Capri.Cli.Configuration;

/// <summary>
/// Allowlisted, non-secret settings that the CLI may read or change. Values
/// returned by this type are safe to display; URLs are redacted.
/// </summary>
public static class ConfigSettings
{
    private const int MaxModelLength = 200;
    private const int MaxBaseUrlLength = 2048;
    private const int MinCharacterHeight = 150;
    private const int MaxCharacterHeight = 400;

    private static readonly string[] SafeKeys =
    [
        "provider.id",
        "provider.model",
        "provider.baseUrl",
        "provider.streaming",
        "provider.freeOnly",
        "provider.allowCloud",
        "provider.temperature",
        "provider.maxOutputTokens",
        "provider.timeoutSeconds",
        "automation.enabled",
        "automation.allowAppLaunch",
        "automation.allowWebLaunch",
        "automation.allowMediaControl",
        "automation.requireConfirmation",
        "ui.startMinimized",
        "ui.alwaysOnTop",
        "ui.theme",
        "ui.reduceMotion",
        "ui.avatarScale",
        "ui.characterHeight",
        "voice.enabled",
        "voice.wakeWord",
        "voice.language",
        "voice.speechRate",
        "voice.volume",
    ];

    private static readonly string[] Themes = ["System", "Light", "Dark"];

    /// <summary>Keys accepted by <c>config get</c> and <c>config set</c>.</summary>
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(SafeKeys);

    /// <summary>True when the key is on the non-secret allowlist.</summary>
    public static bool IsKnownKey(string? key)
    {
        return !string.IsNullOrWhiteSpace(key)
            && SafeKeys.Contains(key.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the canonical allowlist spelling for a key, or null when the
    /// key is not allowlisted.
    /// </summary>
    public static string? CanonicalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();
        return SafeKeys.FirstOrDefault(
            candidate => string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Values are non-secret and safe to display. The base URL is redacted so
    /// credentials or query parameters from a hand-edited settings file are
    /// never printed.
    /// </summary>
    public static object GetValue(AppSettings settings, string key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var canonical = CanonicalKey(key);
        var provider = settings.Provider;
        var automation = settings.Automation;
        var ui = settings.Ui;
        var voice = settings.Voice;

        return canonical switch
        {
            "provider.id" => provider.ProviderId,
            "provider.model" => provider.ChatModel,
            "provider.baseUrl" => SafeUrl.ForDisplay(provider.BaseUrl),
            "provider.streaming" => provider.EnableStreaming,
            "provider.freeOnly" => provider.FreeModelsOnly,
            "provider.allowCloud" => provider.AllowCloudInference,
            "provider.temperature" => provider.Temperature,
            "provider.maxOutputTokens" => provider.MaxOutputTokens,
            "provider.timeoutSeconds" => provider.RequestTimeoutSeconds,
            "automation.enabled" => automation.Enabled,
            "automation.allowAppLaunch" => automation.AllowAppLaunch,
            "automation.allowWebLaunch" => automation.AllowWebLaunch,
            "automation.allowMediaControl" => automation.AllowMediaControl,
            "automation.requireConfirmation" => automation.RequireConfirmation,
            "ui.startMinimized" => ui.StartMinimized,
            "ui.alwaysOnTop" => ui.AlwaysOnTop,
            "ui.theme" => ui.Theme,
            "ui.reduceMotion" => ui.ReduceMotion,
            "ui.avatarScale" => ui.AvatarScale,
            "ui.characterHeight" => ui.CharacterHeight,
            "voice.enabled" => voice.Enabled,
            "voice.wakeWord" => voice.WakeWord,
            "voice.language" => voice.Language,
            "voice.speechRate" => voice.SpeechRate,
            "voice.volume" => voice.Volume,
            _ => throw new ArgumentException("The setting is not allowlisted.", nameof(key)),
        };
    }

    /// <summary>Formats a value for plain-text output using invariant culture.</summary>
    public static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolean => boolean ? "true" : "false",
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Validates and applies a value. Error messages never echo the supplied
    /// value, so a value accidentally containing a secret cannot leak.
    /// </summary>
    public static bool TrySet(
        AppSettings current,
        string key,
        string? rawValue,
        out AppSettings updated,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(current);
        updated = current;
        error = string.Empty;

        var canonical = CanonicalKey(key);
        var provider = current.Provider;
        var automation = current.Automation;
        var ui = current.Ui;
        var voice = current.Voice;
        var value = rawValue?.Trim() ?? string.Empty;

        switch (canonical)
        {
            case "provider.id":
            {
                if (!ProviderCatalog.TryGetById(value, out var profile))
                {
                    error = "Unknown provider id. Choose ollama, lmstudio, openrouter, or openai.";
                    return false;
                }

                var baseUrl = provider.BaseUrl;
                if (string.IsNullOrWhiteSpace(baseUrl)
                    || ProviderCatalog.TryGetById(provider.ProviderId, out var previous)
                    && SameUrl(baseUrl, previous.DefaultBaseUrl))
                {
                    baseUrl = profile.DefaultBaseUrl;
                }

                updated = current with
                {
                    Provider = provider with
                    {
                        ProviderId = profile.Id,
                        BaseUrl = baseUrl,
                    },
                };
                return true;
            }

            case "provider.model":
            {
                if (value.Length == 0 || value.Length > MaxModelLength)
                {
                    error = "A model id is required (maximum 200 characters).";
                    return false;
                }

                if (value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
                {
                    error = "A model id must not contain whitespace or control characters.";
                    return false;
                }

                updated = current with
                {
                    Provider = provider with { ChatModel = value },
                };
                return true;
            }

            case "provider.baseUrl":
            {
                if (value.Length > MaxBaseUrlLength)
                {
                    error = "The base URL is too long.";
                    return false;
                }

                if (!SafeUrl.TryValidateBaseUrl(value, out var normalized, out var urlError))
                {
                    error = urlError;
                    return false;
                }

                updated = current with
                {
                    Provider = provider with { BaseUrl = normalized },
                };
                return true;
            }

            case "provider.streaming":
                if (!TryParseBoolean(value, out var streaming))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Provider = provider with { EnableStreaming = streaming } };
                return true;

            case "provider.freeOnly":
                if (!TryParseBoolean(value, out var freeOnly))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Provider = provider with { FreeModelsOnly = freeOnly } };
                return true;

            case "provider.allowCloud":
                if (!TryParseBoolean(value, out var allowCloud))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with
                {
                    Provider = provider with
                    {
                        AllowCloudInference = allowCloud,
                        CloudConsentRecordedUtc = allowCloud
                            ? provider.CloudConsentRecordedUtc ?? DateTimeOffset.UtcNow
                            : null,
                    },
                };
                return true;

            case "provider.temperature":
                if (!TryParseDouble(value, out var temperature) || temperature is < 0 or > 2)
                {
                    error = "Temperature must be a number between 0 and 2.";
                    return false;
                }

                updated = current with { Provider = provider with { Temperature = temperature } };
                return true;

            case "provider.maxOutputTokens":
                if (!TryParseInt(value, out var maxTokens) || maxTokens is < 1 or > 200_000)
                {
                    error = "Maximum output tokens must be an integer between 1 and 200000.";
                    return false;
                }

                updated = current with { Provider = provider with { MaxOutputTokens = maxTokens } };
                return true;

            case "provider.timeoutSeconds":
                if (!TryParseInt(value, out var timeout) || timeout is < 1 or > 3600)
                {
                    error = "Request timeout must be an integer between 1 and 3600 seconds.";
                    return false;
                }

                updated = current with { Provider = provider with { RequestTimeoutSeconds = timeout } };
                return true;

            case "automation.enabled":
                if (!TryParseBoolean(value, out var automationEnabled))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Automation = automation with { Enabled = automationEnabled } };
                return true;

            case "automation.allowAppLaunch":
                if (!TryParseBoolean(value, out var allowAppLaunch))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Automation = automation with { AllowAppLaunch = allowAppLaunch } };
                return true;

            case "automation.allowWebLaunch":
                if (!TryParseBoolean(value, out var allowWebLaunch))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Automation = automation with { AllowWebLaunch = allowWebLaunch } };
                return true;

            case "automation.allowMediaControl":
                if (!TryParseBoolean(value, out var allowMediaControl))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Automation = automation with { AllowMediaControl = allowMediaControl } };
                return true;

            case "automation.requireConfirmation":
                if (!TryParseBoolean(value, out var requireConfirmation))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Automation = automation with { RequireConfirmation = requireConfirmation } };
                return true;

            case "ui.startMinimized":
                if (!TryParseBoolean(value, out var startMinimized))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Ui = ui with { StartMinimized = startMinimized } };
                return true;

            case "ui.alwaysOnTop":
                if (!TryParseBoolean(value, out var alwaysOnTop))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Ui = ui with { AlwaysOnTop = alwaysOnTop } };
                return true;

            case "ui.theme":
            {
                var theme = Themes.FirstOrDefault(
                    candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
                if (theme is null)
                {
                    error = "Theme must be System, Light, or Dark.";
                    return false;
                }

                updated = current with { Ui = ui with { Theme = theme } };
                return true;
            }

            case "ui.reduceMotion":
                if (!TryParseBoolean(value, out var reduceMotion))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Ui = ui with { ReduceMotion = reduceMotion } };
                return true;

            case "ui.avatarScale":
                if (!TryParseDouble(value, out var avatarScale) || avatarScale is < 0.5 or > 3)
                {
                    error = "Avatar scale must be a number between 0.5 and 3.";
                    return false;
                }

                updated = current with { Ui = ui with { AvatarScale = avatarScale } };
                return true;

            case "ui.characterHeight":
                if (!TryParseInt(value, out var characterHeight)
                    || characterHeight is < MinCharacterHeight or > MaxCharacterHeight)
                {
                    error = "Character height must be an integer between 150 and 400.";
                    return false;
                }

                updated = current with { Ui = ui with { CharacterHeight = characterHeight } };
                return true;

            case "voice.enabled":
                if (!TryParseBoolean(value, out var voiceEnabled))
                {
                    error = "Use true or false.";
                    return false;
                }

                updated = current with { Voice = voice with { Enabled = voiceEnabled } };
                return true;

            case "voice.wakeWord":
                if (value.Length == 0 || value.Length > 32 || value.Any(char.IsControl))
                {
                    error = "Wake word must be 1-32 characters without control characters.";
                    return false;
                }

                updated = current with { Voice = voice with { WakeWord = value } };
                return true;

            case "voice.language":
                if (!IsLanguageTag(value))
                {
                    error = "Language must be a tag such as en-US.";
                    return false;
                }

                updated = current with { Voice = voice with { Language = value } };
                return true;

            case "voice.speechRate":
                if (!TryParseDouble(value, out var speechRate) || speechRate is < 0.5 or > 2)
                {
                    error = "Speech rate must be a number between 0.5 and 2.";
                    return false;
                }

                updated = current with { Voice = voice with { SpeechRate = speechRate } };
                return true;

            case "voice.volume":
                if (!TryParseDouble(value, out var volume) || volume is < 0 or > 1)
                {
                    error = "Volume must be a number between 0 and 1.";
                    return false;
                }

                updated = current with { Voice = voice with { Volume = volume } };
                return true;

            default:
                error = "Unknown or unsupported setting.";
                return false;
        }
    }

    private static string CanonicalKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A setting key is required.", nameof(key));
        }

        return CanonicalizeKey(key)
            ?? throw new ArgumentException("The setting is not allowlisted.", nameof(key));
    }

    private static bool TryParseBoolean(string value, out bool parsed)
    {
        switch (value.ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "on":
                parsed = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "off":
                parsed = false;
                return true;
            default:
                parsed = false;
                return false;
        }
    }

    private static bool TryParseInt(string value, out int parsed)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool TryParseDouble(string value, out double parsed)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
            && double.IsFinite(parsed);
    }

    private static bool IsLanguageTag(string value)
    {
        if (value.Length is < 2 or > 35)
        {
            return false;
        }

        var segments = value.Split('-');
        if (segments[0].Length is < 2 or > 3 || !segments[0].All(char.IsAsciiLetter))
        {
            return false;
        }

        for (var index = 1; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length is < 2 or > 8 || !segment.All(char.IsAsciiLetterOrDigit))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameUrl(string left, string right)
    {
        return string.Equals(
            left.Trim().TrimEnd('/'),
            right.Trim().TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }
}
