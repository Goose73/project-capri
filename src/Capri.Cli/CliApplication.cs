using Capri.Cli.Commands;
using Capri.Cli.Services;

namespace Capri.Cli;

/// <summary>
/// Top-level command dispatcher. Constructing it does not touch the file
/// system, network, or console until a command runs.
/// </summary>
public sealed class CliApplication
{
    private readonly ICliConsole _console;
    private readonly ICliServices _services;
    private readonly StatusCommand _status;
    private readonly DoctorCommand _doctor;
    private readonly ConfigCommand _config;
    private readonly ModelsCommand? _models;
    private readonly AskCommand _ask;
    private readonly ChatCommand _chat;
    private readonly ProvidersCommand _providers;
    private readonly DashboardCommand _dashboard;
    private readonly IDesktopLauncher _launcher;

    /// <summary>Creates the dispatcher with explicit collaborators.</summary>
    public CliApplication(
        ICliConsole console,
        ICliServices services,
        IEndpointProbe probe,
        IDesktopLauncher launcher,
        IDpapiAvailabilityProbe dpapiProbe,
        IOllamaClient? ollamaClient = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        ArgumentNullException.ThrowIfNull(probe);
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        ArgumentNullException.ThrowIfNull(dpapiProbe);

        _status = new StatusCommand(console, services, probe);
        _doctor = new DoctorCommand(console, services, probe, dpapiProbe);
        _config = new ConfigCommand(console, services);
        var ollama = ollamaClient ?? services.OllamaClient;
        _models = ollama is null ? null : new ModelsCommand(console, services, ollama);
        _ask = new AskCommand(console, services);
        _chat = new ChatCommand(console, services);
        _providers = new ProvidersCommand(console, services);
        _dashboard = new DashboardCommand(console, _status, _doctor, _config, launcher, ct => _chat.ExecuteAsync(ct));
    }

    /// <summary>Runs the parsed invocation and returns a process exit code.</summary>
    public async Task<int> RunAsync(
        CliInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        if (!invocation.IsValid)
        {
            _console.WriteErrorLine($"capri: {invocation.Error}");
            _console.WriteErrorLine("Run 'capri help' for usage.");
            return CliExitCodes.Usage;
        }

        switch (invocation.Kind)
        {
            case CliCommandKind.None:
                if (!_console.IsInteractive)
                {
                    _console.WriteLine(
                        "capri: no command specified and the terminal is not interactive. Showing help.");
                    _console.WriteLine(HelpText.Full);
                    return CliExitCodes.Success;
                }

                return await _dashboard.RunAsync(cancellationToken).ConfigureAwait(false);

            case CliCommandKind.Help:
                _console.WriteLine(HelpText.For(invocation.Topic));
                return CliExitCodes.Success;

            case CliCommandKind.Version:
                _console.WriteLine($"Project Capri CLI {GetVersion()}");
                return CliExitCodes.Success;

            case CliCommandKind.Status:
                return await _status.ExecuteAsync(invocation.Json, cancellationToken).ConfigureAwait(false);

            case CliCommandKind.Doctor:
                return await _doctor.ExecuteAsync(invocation.Json, cancellationToken).ConfigureAwait(false);

            case CliCommandKind.Config:
                return await _config
                    .ExecuteAsync(invocation.Arguments, invocation.Json, cancellationToken)
                    .ConfigureAwait(false);

            case CliCommandKind.Models:
                if (_models is null)
                {
                    _console.WriteErrorLine("capri: model management services are unavailable in this CLI build.");
                    return CliExitCodes.Error;
                }

                return await _models.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);

            case CliCommandKind.Ask:
                return await _ask.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);

            case CliCommandKind.Chat:
                return await _chat.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            case CliCommandKind.Providers:
                return await _providers.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);

            case CliCommandKind.Setup:
                return SetupCommand.Execute(invocation.Repair, _console);

            case CliCommandKind.Start:
            case CliCommandKind.Gui:
                cancellationToken.ThrowIfCancellationRequested();
                var result = _launcher.Launch();
                if (result.Started)
                {
                    _console.WriteLine(result.Message);
                    return CliExitCodes.Success;
                }

                _console.WriteErrorLine($"capri: {result.Message}");
                return CliExitCodes.Error;

            default:
                _console.WriteErrorLine("capri: unknown command. Run 'capri help' for usage.");
                return CliExitCodes.Usage;
        }
    }

    private static string GetVersion()
    {
        var version = typeof(CliApplication).Assembly.GetName().Version;
        return version is null ? "0.1.0" : version.ToString(3);
    }
}
