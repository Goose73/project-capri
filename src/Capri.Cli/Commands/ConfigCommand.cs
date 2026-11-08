using Capri.Cli.Configuration;
using Capri.Cli.Services;
using Capri.Core.Models;

namespace Capri.Cli.Commands;

/// <summary>
/// Reads and changes allowlisted, non-secret settings. Secret-like names are
/// rejected without echoing user input.
/// </summary>
public sealed class ConfigCommand
{
    private readonly ICliConsole _console;
    private readonly ICliServices _services;

    /// <summary>Creates the config command.</summary>
    public ConfigCommand(ICliConsole console, ICliServices services)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Executes a config subcommand. <paramref name="arguments"/> excludes the
    /// <c>config</c> token and option flags; when empty, the interactive menu is
    /// started.
    /// </summary>
    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        bool json,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments.Count == 0)
        {
            return await RunInteractiveAsync(cancellationToken).ConfigureAwait(false);
        }

        var subcommand = arguments[0].Trim().ToLowerInvariant();
        var positionals = arguments.Skip(1).ToArray();

        switch (subcommand)
        {
            case "keys":
                if (positionals.Length > 0)
                {
                    return UsageError("Usage: capri config keys [--json]");
                }

                WriteKeys(json);
                return CliExitCodes.Success;

            case "list":
                if (positionals.Length > 0)
                {
                    return UsageError("Usage: capri config list [--json]");
                }

                WriteList(json);
                return CliExitCodes.Success;

            case "get":
                if (positionals.Length != 1)
                {
                    return UsageError("Usage: capri config get <setting> [--json]");
                }

                return GetValue(positionals[0], json);

            case "set":
                if (positionals.Length != 2)
                {
                    return UsageError("Usage: capri config set <setting> <value> [--json]");
                }

                return await SetValueAsync(positionals[0], positionals[1], json, cancellationToken)
                    .ConfigureAwait(false);

            default:
                return UsageError(
                    "Unknown config command. Use list, keys, get, or set. Run 'capri config --help' for usage.");
        }
    }

    private void WriteKeys(bool json)
    {
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { keys = ConfigSettings.Keys }));
            return;
        }

        foreach (var key in ConfigSettings.Keys)
        {
            _console.WriteLine(key);
        }
    }

    private void WriteList(bool json)
    {
        var settings = _services.Settings.Current;
        if (json)
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var key in ConfigSettings.Keys)
            {
                values[key] = ConfigSettings.GetValue(settings, key);
            }

            _console.WriteLine(JsonOutput.Serialize(values));
            return;
        }

        _console.WriteLine("Project Capri settings (non-secret)");
        _console.WriteLine("-----------------------------------");
        foreach (var key in ConfigSettings.Keys)
        {
            var value = ConfigSettings.FormatValue(ConfigSettings.GetValue(settings, key));
            _console.WriteLine($"{key} = {value}");
        }
    }

    private int GetValue(string key, bool json)
    {
        if (TryResolveKey(key, requireSafeKey: true, out var canonical, out var error))
        {
            var value = ConfigSettings.GetValue(_services.Settings.Current, canonical);
            if (json)
            {
                _console.WriteLine(JsonOutput.Serialize(new { key = canonical, value }));
            }
            else
            {
                _console.WriteLine(ConfigSettings.FormatValue(value));
            }

            return CliExitCodes.Success;
        }

        _console.WriteErrorLine($"capri: {error}");
        return CliExitCodes.Usage;
    }

    private async Task<int> SetValueAsync(
        string key,
        string value,
        bool json,
        CancellationToken cancellationToken)
    {
        if (!TryResolveKey(key, requireSafeKey: true, out var canonical, out var keyError))
        {
            _console.WriteErrorLine($"capri: {keyError}");
            return CliExitCodes.Usage;
        }

        if (!CanPersist(out var persistError))
        {
            _console.WriteErrorLine($"capri: {persistError}");
            return CliExitCodes.Error;
        }

        if (!ConfigSettings.TrySet(_services.Settings.Current, canonical, value, out var updated, out var valueError))
        {
            _console.WriteErrorLine($"capri: invalid value for {canonical}. {valueError}");
            return CliExitCodes.Usage;
        }

        try
        {
            await _services.Settings.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _console.WriteErrorLine("capri: the settings file could not be written.");
            return CliExitCodes.Error;
        }

        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new
            {
                key = canonical,
                persisted = true,
                settingsFile = _services.Paths.SettingsFile,
            }));
        }
        else
        {
            _console.WriteLine($"Updated {canonical} (persisted).");
        }

        return CliExitCodes.Success;
    }

    /// <summary>
    /// Runs the minimal interactive settings menu. Returns usage error when the
    /// console is not interactive.
    /// </summary>
    public async Task<int> RunInteractiveAsync(CancellationToken cancellationToken = default)
    {
        if (!_console.IsInteractive)
        {
            _console.WriteErrorLine(
                "capri: the interactive config menu needs a terminal. Use 'capri config list', 'capri config get <setting>', or 'capri config set <setting> <value>'.");
            return CliExitCodes.Usage;
        }

        _console.WriteLine("Project Capri settings (non-secret)");
        _console.WriteLine("-----------------------------------");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var settings = _services.Settings.Current;
            for (var index = 0; index < ConfigSettings.Keys.Count; index++)
            {
                var key = ConfigSettings.Keys[index];
                var value = ConfigSettings.FormatValue(ConfigSettings.GetValue(settings, key));
                _console.WriteLine($"  {index + 1,2}. {key} = {value}");
            }

            _console.Write("Setting number or key (q to quit): ");
            var selection = _console.ReadLine();
            if (selection is null
                || string.Equals(selection.Trim(), "q", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selection.Trim(), "quit", StringComparison.OrdinalIgnoreCase))
            {
                return CliExitCodes.Success;
            }

            if (selection.Trim().Length == 0)
            {
                continue;
            }

            string? selectedKey = null;
            if (int.TryParse(selection.Trim(), out var number)
                && number >= 1
                && number <= ConfigSettings.Keys.Count)
            {
                selectedKey = ConfigSettings.Keys[number - 1];
            }
            else if (ConfigSettings.CanonicalizeKey(selection) is { } canonical)
            {
                selectedKey = canonical;
            }

            if (selectedKey is null)
            {
                _console.WriteLine(
                    "Unknown selection. Enter a listed number or key. Secret settings are not available here.");
                continue;
            }

            _console.Write($"Value for {selectedKey} (blank to cancel): ");
            var input = _console.ReadLine();
            if (input is null || input.Length == 0)
            {
                continue;
            }

            var result = await SetValueAsync(selectedKey, input, json: false, cancellationToken)
                .ConfigureAwait(false);
            if (result != CliExitCodes.Success)
            {
                _console.WriteLine("No change was saved.");
            }
        }
    }

    private static bool TryResolveKey(
        string key,
        bool requireSafeKey,
        out string canonical,
        out string error)
    {
        canonical = string.Empty;
        error = string.Empty;

        if (requireSafeKey && SecretGuard.IsSecretLike(key))
        {
            error = "That setting name looks like a credential. Secret and credential settings are not accessible from the CLI.";
            return false;
        }

        var resolved = ConfigSettings.CanonicalizeKey(key);
        if (resolved is null)
        {
            error = "Unknown or unsupported setting. Run 'capri config keys' to list safe settings.";
            return false;
        }

        canonical = resolved;
        return true;
    }

    private bool CanPersist(out string error)
    {
        var inspection = SettingsFileInspector.Inspect(_services.Paths);
        switch (inspection.State)
        {
            case SettingsFileState.Readable:
            case SettingsFileState.Missing:
                error = string.Empty;
                return true;
            case SettingsFileState.Empty:
                error = "the settings file is empty. Refusing to overwrite it; run 'capri doctor' for guidance.";
                return false;
            case SettingsFileState.Malformed:
                error = "the settings file is not valid JSON. Refusing to overwrite it; run 'capri doctor' for guidance.";
                return false;
            case SettingsFileState.Unreadable:
                error = "the settings file cannot be read. Refusing to overwrite it; run 'capri doctor' for guidance.";
                return false;
            default:
                error = "the settings file state is unknown. Refusing to overwrite it.";
                return false;
        }
    }

    private int UsageError(string message)
    {
        _console.WriteErrorLine($"capri: {message}");
        return CliExitCodes.Usage;
    }
}
