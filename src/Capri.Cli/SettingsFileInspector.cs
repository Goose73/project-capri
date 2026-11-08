using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Cli;

/// <summary>State of the settings file when inspected without applying it.</summary>
public enum SettingsFileState
{
    /// <summary>The file does not exist; defaults are in use.</summary>
    Missing,

    /// <summary>The file exists but is empty; defaults are in use.</summary>
    Empty,

    /// <summary>The file exists and contains a valid settings document.</summary>
    Readable,

    /// <summary>The file exists but does not contain valid settings JSON.</summary>
    Malformed,

    /// <summary>The file exists but cannot be read.</summary>
    Unreadable,
}

/// <summary>Result of a read-only settings file inspection.</summary>
public sealed record SettingsFileInspection(SettingsFileState State, string Detail);

/// <summary>
/// Validates the settings file without exposing its content. Used by
/// <c>doctor</c> and by <c>config set</c> to avoid overwriting a file that the
/// settings store could not read.
/// </summary>
public static class SettingsFileInspector
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Inspects <see cref="ICapriPaths.SettingsFile"/>.</summary>
    public static SettingsFileInspection Inspect(ICapriPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var settingsFile = paths.SettingsFile;

        if (!File.Exists(settingsFile))
        {
            return new SettingsFileInspection(
                SettingsFileState.Missing,
                "Settings file has not been created; built-in defaults are in use.");
        }

        try
        {
            var bytes = File.ReadAllBytes(settingsFile);
            if (bytes.Length == 0)
            {
                return new SettingsFileInspection(
                    SettingsFileState.Empty,
                    "Settings file is empty; built-in defaults are in use.");
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
            return settings is null
                ? new SettingsFileInspection(
                    SettingsFileState.Malformed,
                    "Settings file does not contain a settings object.")
                : new SettingsFileInspection(
                    SettingsFileState.Readable,
                    "Settings file is readable.");
        }
        catch (JsonException)
        {
            return new SettingsFileInspection(
                SettingsFileState.Malformed,
                "Settings file is not valid JSON.");
        }
        catch (UnauthorizedAccessException)
        {
            return new SettingsFileInspection(
                SettingsFileState.Unreadable,
                "Settings file cannot be read because access was denied.");
        }
        catch (IOException)
        {
            return new SettingsFileInspection(
                SettingsFileState.Unreadable,
                "Settings file cannot be read because of an I/O error.");
        }
    }
}
