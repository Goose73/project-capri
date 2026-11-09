namespace Capri.Cli;

/// <summary>
/// Commands understood by the Capri CLI.
/// </summary>
public enum CliCommandKind
{
    /// <summary>No arguments were supplied.</summary>
    None,

    /// <summary>General or command-specific help.</summary>
    Help,

    /// <summary>Print the CLI version.</summary>
    Version,

    /// <summary>Show effective settings and reachability.</summary>
    Status,

    /// <summary>Run diagnostics.</summary>
    Doctor,

    /// <summary>Read or change safe settings.</summary>
    Config,

    /// <summary>Guided setup placeholder (Milestone 2).</summary>
    Setup,

    /// <summary>Launch the desktop application.</summary>
    Start,

    /// <summary>Alias for <see cref="Start"/>.</summary>
    Gui,

    /// <summary>Manage and inspect local/provider models.</summary>
    Models,

    /// <summary>Send a single question to the configured provider.</summary>
    Ask,

    /// <summary>Start an interactive chat session.</summary>
    Chat,

    /// <summary>Provider operations such as inference testing.</summary>
    Providers,

    /// <summary>Run a local AI task through the existing coordinator (Milestone 4).</summary>
    Task,

    /// <summary>List recent task metadata.</summary>
    Tasks,

    /// <summary>Internal per-user task authority host (M5).</summary>
    Authority,

    /// <summary>The command was not recognized.</summary>
    Unknown,
}

/// <summary>
/// Immutable result of parsing the raw command line.
/// </summary>
public sealed record CliInvocation
{
    /// <summary>The selected command.</summary>
    public CliCommandKind Kind { get; init; }

    /// <summary>
    /// Positional arguments for the command (options and their values are
    /// removed). For <see cref="CliCommandKind.Config"/> this is the
    /// subcommand followed by its positional arguments.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>True when JSON output was requested.</summary>
    public bool Json { get; init; }

    /// <summary>True when <c>setup --repair</c> was requested.</summary>
    public bool Repair { get; init; }

    /// <summary>True when explicit confirmation was provided (for example <c>models pull --yes</c>).</summary>
    public bool Yes { get; init; }

    /// <summary>Optional help topic (for example <c>config</c>).</summary>
    public string? Topic { get; init; }

    /// <summary>A safe, user-facing usage error, or null when parsing succeeded.</summary>
    public string? Error { get; init; }

    /// <summary>True when the invocation can be executed.</summary>
    public bool IsValid => Error is null;
}

/// <summary>
/// Deterministic, side-effect-free command line parser.
/// </summary>
public static class CliParser
{
    private static readonly string[] KnownCommands =
    [
        "help",
        "status",
        "doctor",
        "config",
        "setup",
        "start",
        "gui",
        "models",
        "ask",
        "chat",
        "providers",
        "task",
        "tasks",
        "version",
    ];

    /// <summary>
    /// Parses command line arguments. The parser never touches the file
    /// system, network, or console.
    /// </summary>
    public static CliInvocation Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return new CliInvocation { Kind = CliCommandKind.None };
        }

        var head = args[0].Trim();
        if (head.Length == 0)
        {
            return UsageError("The command is empty.");
        }

        switch (head.ToLowerInvariant())
        {
            case "help":
            case "-h":
            case "--help":
            case "/?":
                return new CliInvocation
                {
                    Kind = CliCommandKind.Help,
                    Topic = args.Count > 1 ? args[1].Trim().ToLowerInvariant() : null,
                };

            case "version":
            case "--version":
            case "-v":
                return new CliInvocation { Kind = CliCommandKind.Version };

            case "status":
                return ParseFlagOnly(args, CliCommandKind.Status, allowJson: true, allowRepair: false);

            case "doctor":
                return ParseFlagOnly(args, CliCommandKind.Doctor, allowJson: true, allowRepair: false);

            case "setup":
                return ParseFlagOnly(args, CliCommandKind.Setup, allowJson: false, allowRepair: true);

            case "start":
                return ParseFlagOnly(args, CliCommandKind.Start, allowJson: false, allowRepair: false);

            case "gui":
                return ParseFlagOnly(args, CliCommandKind.Gui, allowJson: false, allowRepair: false);

            case "config":
                return ParseConfig(args);

            case "models":
                return ParseModels(args);

            case "ask":
                return ParseAsk(args);

            case "chat":
                return ParseFlagOnly(args, CliCommandKind.Chat, allowJson: false, allowRepair: false);

            case "providers":
                return ParseProviders(args);

            case "task":
                return ParseTask(args);

            case "tasks":
                return ParseTasks(args);

            case "authority":
                return ParseAuthority(args);

            default:
                return UsageError(
                    "Unknown command. Run 'capri help' for the list of supported commands.");
        }
    }

    /// <summary>
    /// Returns true when the input is one of the recognized command names.
    /// </summary>
    public static bool IsKnownCommand(string? command)
    {
        return !string.IsNullOrWhiteSpace(command)
            && KnownCommands.Contains(command.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static CliInvocation ParseFlagOnly(
        IReadOnlyList<string> args,
        CliCommandKind kind,
        bool allowJson,
        bool allowRepair)
    {
        var json = false;
        var repair = false;

        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index].Trim();
            if (argument.Length == 0)
            {
                continue;
            }

            if (IsHelpOption(argument))
            {
                return new CliInvocation
                {
                    Kind = CliCommandKind.Help,
                    Topic = args[0].Trim().ToLowerInvariant(),
                };
            }

            if (string.Equals(argument, "--json", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowJson)
                {
                    return UsageError("The '--json' option is not supported by this command.");
                }

                json = true;
                continue;
            }

            if (string.Equals(argument, "--repair", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowRepair)
                {
                    return UsageError("The '--repair' option is only supported by 'capri setup'.");
                }

                repair = true;
                continue;
            }

            return UsageError("Unknown option. Run 'capri help' for usage.");
        }

        return new CliInvocation
        {
            Kind = kind,
            Json = json,
            Repair = repair,
        };
    }

    private static CliInvocation ParseConfig(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            return new CliInvocation { Kind = CliCommandKind.Config };
        }

        var subcommand = args[1].Trim().ToLowerInvariant();
        if (IsHelpOption(subcommand) || string.Equals(subcommand, "help", StringComparison.OrdinalIgnoreCase))
        {
            return new CliInvocation { Kind = CliCommandKind.Help, Topic = "config" };
        }

        var json = false;
        var positionals = new List<string>(capacity: 3);
        for (var index = 2; index < args.Count; index++)
        {
            var argument = args[index].Trim();
            if (argument.Length == 0)
            {
                continue;
            }

            if (IsHelpOption(argument))
            {
                return new CliInvocation { Kind = CliCommandKind.Help, Topic = "config" };
            }

            if (string.Equals(argument, "--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                return UsageError("Unknown option. Run 'capri config --help' for usage.");
            }

            positionals.Add(argument);
        }

        switch (subcommand)
        {
            case "list":
            case "keys":
                if (positionals.Count > 0)
                {
                    return UsageError("This config command does not accept positional arguments.");
                }

                break;

            case "get":
                if (positionals.Count != 1)
                {
                    return UsageError("Usage: capri config get <setting> [--json]");
                }

                break;

            case "set":
                if (positionals.Count != 2)
                {
                    return UsageError("Usage: capri config set <setting> <value> [--json]");
                }

                break;

            default:
                return UsageError(
                    "Unknown config command. Use list, keys, get, or set. Run 'capri config --help' for usage.");
        }

        var arguments = new List<string>(positionals.Count + 1) { subcommand };
        arguments.AddRange(positionals);

        return new CliInvocation
        {
            Kind = CliCommandKind.Config,
            Arguments = arguments,
            Json = json,
        };
    }

    private static CliInvocation ParseModels(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            return UsageError("Usage: capri models <list|recommend|pull|use> [args]. Run 'capri help models' for usage.");
        }

        var subcommand = args[1].Trim().ToLowerInvariant();
        if (IsHelpOption(subcommand) || string.Equals(subcommand, "help", StringComparison.OrdinalIgnoreCase))
        {
            return new CliInvocation { Kind = CliCommandKind.Help, Topic = "models" };
        }

        var json = false;
        var yes = false;
        var positionals = new List<string>(capacity: 2);
        for (var index = 2; index < args.Count; index++)
        {
            var argument = args[index].Trim();
            if (argument.Length == 0)
            {
                continue;
            }

            if (IsHelpOption(argument))
            {
                return new CliInvocation { Kind = CliCommandKind.Help, Topic = "models" };
            }

            if (string.Equals(argument, "--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (string.Equals(argument, "--yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "-y", StringComparison.OrdinalIgnoreCase))
            {
                yes = true;
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                return UsageError("Unknown option. Run 'capri help models' for usage.");
            }

            positionals.Add(argument);
        }

        switch (subcommand)
        {
            case "list":
            case "recommend":
                if (positionals.Count > 0)
                {
                    return UsageError($"Usage: capri models {subcommand} [--json]");
                }

                break;

            case "pull":
            case "use":
                if (positionals.Count != 1)
                {
                    return UsageError(subcommand == "pull"
                        ? "Usage: capri models pull <model> [--yes]"
                        : "Usage: capri models use <model>");
                }

                break;

            default:
                return UsageError("Unknown models command. Use list, recommend, pull, or use. Run 'capri help models' for usage.");
        }

        var arguments = new List<string>(positionals.Count + 1) { subcommand };
        arguments.AddRange(positionals);

        return new CliInvocation
        {
            Kind = CliCommandKind.Models,
            Arguments = arguments,
            Json = json,
            Yes = yes,
        };
    }

    private static CliInvocation ParseAsk(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            return UsageError("Usage: capri ask \"<question>\" [--json]");
        }

        var json = false;
        var parts = new List<string>(capacity: Math.Max(1, args.Count - 1));
        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];
            if (IsHelpOption(argument))
            {
                return new CliInvocation { Kind = CliCommandKind.Help, Topic = "ask" };
            }

            if (string.Equals(argument.Trim(), "--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (argument.TrimStart().StartsWith("--", StringComparison.Ordinal))
            {
                return UsageError("Unknown option. Run 'capri help ask' for usage.");
            }

            parts.Add(argument);
        }

        if (parts.Count == 0)
        {
            return UsageError("Usage: capri ask \"<question>\" [--json]");
        }

        return new CliInvocation
        {
            Kind = CliCommandKind.Ask,
            Arguments = [string.Join(' ', parts)],
            Json = json,
        };
    }

    private static CliInvocation ParseTask(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            return UsageError("Usage: capri task \"<request>\" | capri task status|watch <id> | capri task desktop-status|stop|reset-stop|history [--json]");
        }

        var first = args[1].Trim();
        if (IsHelpOption(first) || string.Equals(first, "help", StringComparison.OrdinalIgnoreCase))
        {
            return new CliInvocation { Kind = CliCommandKind.Help, Topic = "task" };
        }

        var firstLower = first.ToLowerInvariant();
        if (firstLower is "status" or "watch" or "desktop-status" or "stop" or "reset-stop" or "history")
        {
            var json = false;
            var positionals = new List<string> { firstLower };
            for (var index = 2; index < args.Count; index++)
            {
                var argument = args[index].Trim();
                if (argument.Length == 0) continue;
                if (IsHelpOption(argument))
                    return new CliInvocation { Kind = CliCommandKind.Help, Topic = "task" };
                if (string.Equals(argument, "--json", StringComparison.OrdinalIgnoreCase))
                {
                    json = true;
                    continue;
                }
                if (argument.StartsWith("--", StringComparison.Ordinal))
                    return UsageError("Unknown option. Run 'capri help task' for usage.");
                positionals.Add(argument);
            }

            if (firstLower is "status" or "watch")
            {
                if (positionals.Count != 2)
                    return UsageError($"Usage: capri task {firstLower} <id> [--json]");
            }
            else if (positionals.Count != 1)
            {
                return UsageError($"Usage: capri task {firstLower} [--json]");
            }

            return new CliInvocation
            {
                Kind = CliCommandKind.Task,
                Arguments = positionals,
                Json = json,
            };
        }

        // Run request: accept unquoted remainder reasonably.
        var parts = new List<string>(capacity: Math.Max(1, args.Count - 1));
        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];
            var trimmed = argument.Trim();
            if (IsHelpOption(trimmed))
                return new CliInvocation { Kind = CliCommandKind.Help, Topic = "task" };
            if (string.Equals(trimmed, "--json", StringComparison.OrdinalIgnoreCase))
                return UsageError("`capri task` execution is interactive and does not support --json. Use `capri task status|history --json` for metadata.");
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
                return UsageError("Unknown option. Run 'capri help task' for usage.");
            parts.Add(argument);
        }

        var request = string.Join(' ', parts).Trim();
        if (string.IsNullOrWhiteSpace(request))
            return UsageError("A task description is required. Usage: capri task \"<request>\"");

        return new CliInvocation
        {
            Kind = CliCommandKind.Task,
            Arguments = [request],
            Json = false,
        };
    }

    private static CliInvocation ParseTasks(IReadOnlyList<string> args)
    {
        return ParseFlagOnly(args, CliCommandKind.Tasks, allowJson: true, allowRepair: false);
    }

    private static CliInvocation ParseAuthority(IReadOnlyList<string> args)
    {
        var sub = args.Count > 1 ? args[1].Trim().ToLowerInvariant() : string.Empty;
        if (sub == "serve") return new CliInvocation { Kind = CliCommandKind.Authority, Arguments = ["serve"] };
        return new CliInvocation { Kind = CliCommandKind.Unknown, Arguments = [.. args.Skip(1)], Error = "Usage: capri authority serve" };
    }

    private static CliInvocation ParseProviders(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            return UsageError("Usage: capri providers test [--json]. Run 'capri help providers' for usage.");
        }

        var subcommand = args[1].Trim().ToLowerInvariant();
        if (IsHelpOption(subcommand) || string.Equals(subcommand, "help", StringComparison.OrdinalIgnoreCase))
        {
            return new CliInvocation { Kind = CliCommandKind.Help, Topic = "providers" };
        }

        var json = false;
        for (var index = 2; index < args.Count; index++)
        {
            var argument = args[index].Trim();
            if (argument.Length == 0)
            {
                continue;
            }

            if (IsHelpOption(argument))
            {
                return new CliInvocation { Kind = CliCommandKind.Help, Topic = "providers" };
            }

            if (string.Equals(argument, "--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                return UsageError("Unknown option. Run 'capri help providers' for usage.");
            }

            return UsageError("Usage: capri providers test [--json]");
        }

        if (!string.Equals(subcommand, "test", StringComparison.OrdinalIgnoreCase))
        {
            return UsageError("Unknown providers command. Use test. Run 'capri help providers' for usage.");
        }

        return new CliInvocation
        {
            Kind = CliCommandKind.Providers,
            Arguments = ["test"],
            Json = json,
        };
    }

    private static bool IsHelpOption(string argument)
    {
        return string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "/?", StringComparison.OrdinalIgnoreCase);
    }

    private static CliInvocation UsageError(string message)
    {
        return new CliInvocation
        {
            Kind = CliCommandKind.Unknown,
            Error = message,
        };
    }
}
