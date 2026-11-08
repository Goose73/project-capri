using Capri.Cli.Commands;
using Capri.Cli.Services;

namespace Capri.Cli;

/// <summary>
/// Basic functional dashboard for an interactive terminal.
/// </summary>
public sealed class DashboardCommand
{
    private readonly ICliConsole _console;
    private readonly StatusCommand _status;
    private readonly DoctorCommand _doctor;
    private readonly ConfigCommand _config;
    private readonly TaskCommand? _task;
    private readonly IDesktopLauncher _launcher;
    private readonly Func<CancellationToken, Task<int>>? _chatRunner;

    /// <summary>Creates the dashboard.</summary>
    public DashboardCommand(
        ICliConsole console,
        StatusCommand status,
        DoctorCommand doctor,
        ConfigCommand config,
        IDesktopLauncher launcher,
        Func<CancellationToken, Task<int>>? chatRunner = null)
        : this(console, status, doctor, config, task: null, launcher, chatRunner)
    {
    }

    /// <summary>Creates the dashboard with task support.</summary>
    public DashboardCommand(
        ICliConsole console,
        StatusCommand status,
        DoctorCommand doctor,
        ConfigCommand config,
        TaskCommand? task,
        IDesktopLauncher launcher,
        Func<CancellationToken, Task<int>>? chatRunner = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _doctor = doctor ?? throw new ArgumentNullException(nameof(doctor));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _task = task;
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _chatRunner = chatRunner;
    }

    /// <summary>Runs the menu until the user exits.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_console.IsInteractive)
        {
            _console.WriteErrorLine(
                "capri: the interactive dashboard needs a terminal. Run 'capri help' for commands.");
            return CliExitCodes.Usage;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _console.Clear();
            _console.WriteLine("Project Capri - CLI");
            _console.WriteLine("===================");
            _console.WriteLine("  1. Status");
            _console.WriteLine("  2. Config");
            _console.WriteLine("  3. Doctor");
            _console.WriteLine("  4. Chat");
            _console.WriteLine("  5. Task");
            _console.WriteLine("  6. Launch desktop app");
            _console.WriteLine("  7. Help");
            _console.WriteLine("  8. Exit");
            _console.Write("Select: ");

            var input = _console.ReadLine();
            if (input is null)
            {
                return CliExitCodes.Success;
            }

            switch (input.Trim().ToLowerInvariant())
            {
                case "1":
                case "status":
                    await _status.ExecuteAsync(json: false, cancellationToken).ConfigureAwait(false);
                    Pause();
                    break;

                case "2":
                case "config":
                    await _config.RunInteractiveAsync(cancellationToken).ConfigureAwait(false);
                    Pause();
                    break;

                case "3":
                case "doctor":
                    await _doctor.ExecuteAsync(json: false, cancellationToken).ConfigureAwait(false);
                    Pause();
                    break;

                case "4":
                case "chat":
                    if (_chatRunner is null)
                    {
                        _console.WriteLine(
                            "Chat: configure a provider/model with `capri models use <model>` or `capri config`, then run `capri chat`.");
                    }
                    else
                    {
                        _ = await _chatRunner(cancellationToken).ConfigureAwait(false);
                    }

                    Pause();
                    break;

                case "5":
                case "task":
                    if (_task is null)
                    {
                        _console.WriteLine("Task: run `capri task \"<request>\"` for AI-planned local tasks, or `capri task status|stop|reset-stop|history`.");
                    }
                    else
                    {
                        _console.WriteLine("Task actions: 1) Run task  2) Recent tasks  3) Stop  4) Reset stop  5) Back");
                        _console.Write("Select: ");
                        var taskChoice = _console.ReadLine()?.Trim().ToLowerInvariant();
                        switch (taskChoice)
                        {
                            case "1":
                            case "run":
                                _console.Write("Describe the task: ");
                                var request = _console.ReadLine();
                                if (!string.IsNullOrWhiteSpace(request))
                                    _ = await _task.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = [request], Json = false }, cancellationToken).ConfigureAwait(false);
                                else
                                    _console.WriteLine("Cancelled (empty request).");
                                break;
                            case "2":
                            case "tasks":
                                _ = await _task.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Tasks, Json = false }, cancellationToken).ConfigureAwait(false);
                                break;
                            case "3":
                            case "stop":
                                _ = await _task.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["stop"] }, cancellationToken).ConfigureAwait(false);
                                break;
                            case "4":
                            case "reset":
                            case "reset-stop":
                                _ = await _task.ExecuteAsync(new CliInvocation { Kind = CliCommandKind.Task, Arguments = ["reset-stop"] }, cancellationToken).ConfigureAwait(false);
                                break;
                            default:
                                break;
                        }
                    }
                    Pause();
                    break;

                case "6":
                case "gui":
                case "start":
                case "launch":
                    WriteLaunchResult(_launcher.Launch());
                    Pause();
                    break;

                case "7":
                case "help":
                    _console.WriteLine(HelpText.Full);
                    Pause();
                    break;

                case "8":
                case "exit":
                case "quit":
                case "q":
                    return CliExitCodes.Success;

                case "":
                    break;

                default:
                    _console.WriteLine("Unknown selection. Enter 1-8.");
                    Pause();
                    break;
            }
        }
    }

    private void WriteLaunchResult(DesktopLaunchResult result)
    {
        if (result.Started)
        {
            _console.WriteLine(result.Message);
            return;
        }

        _console.WriteLine($"Could not launch the desktop app: {result.Message}");
    }

    private void Pause()
    {
        _console.Write("Press Enter to return to the dashboard...");
        _ = _console.ReadLine();
    }
}
