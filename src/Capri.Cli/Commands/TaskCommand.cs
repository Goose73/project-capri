using System.Text.Json;
using Capri.Cli.Services;
using Capri.Core.Autonomy;
using Capri.Security.Autonomy;

namespace Capri.Cli.Commands;

/// <summary>
/// M4 task execution and control commands using the existing coordinator/parser/executor/journal.
/// </summary>
public sealed class TaskCommand
{
    private readonly ICliConsole _console;
    private readonly ICliServices _services;

    public TaskCommand(ICliConsole console, ICliServices services)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public async Task<int> ExecuteAsync(CliInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        if (invocation.Kind == CliCommandKind.Tasks)
            return await ListTasksAsync(invocation.Json, cancellationToken).ConfigureAwait(false);

        if (invocation.Arguments.Count == 0)
            return Usage("Usage: capri task \"<request>\" | capri task status|watch <id> [--json] | stop|reset-stop|history [--json]");

        var sub = invocation.Arguments[0].ToLowerInvariant();
        return sub switch
        {
            "status" => await StatusAsync(invocation, cancellationToken).ConfigureAwait(false),
            "watch" => await WatchAsync(invocation, cancellationToken).ConfigureAwait(false),
            "desktop-status" => await DesktopStatusAsync(invocation.Json, cancellationToken).ConfigureAwait(false),
            "stop" => Stop(invocation.Json),
            "reset-stop" => ResetStop(invocation.Json),
            "history" => await HistoryAsync(invocation.Json, cancellationToken).ConfigureAwait(false),
            _ => await RunTaskAsync(invocation.Arguments[0], cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<int> RunTaskAsync(string request, CancellationToken cancellationToken)
    {
        if (!_console.IsInteractive)
        {
            _console.WriteErrorLine("capri: `capri task` requires an interactive terminal for safe approval. No actions were dispatched.");
            _console.WriteErrorLine("Run from a terminal, review the exact plan, and type `approve PLAN` to proceed.");
            return CliExitCodes.Usage;
        }

        if (!ChatCommandSupport.TryGetConfiguredChat(_services, out var provider, out var router, out var configError))
        {
            _console.WriteErrorLine($"capri: {configError}");
            return CliExitCodes.Error;
        }

        if (_services.TaskExecutor is null || _services.TaskJournal is null)
        {
            _console.WriteErrorLine("capri: task execution services are unavailable in this CLI build.");
            return CliExitCodes.Error;
        }

        var taskId = Guid.NewGuid();
        var control = _services.TaskControl;
        var approval = new CliTaskApproval(_console);
        var coordinator = new TaskCoordinator(provider!, router!, approval, _services.TaskExecutor, _services.TaskJournal, control);

        _console.WriteLine($"Task: {taskId}");
        _console.WriteLine("Planning uses the configured local AI provider (genuine inference). No actions occur before explicit approval.");
        _console.WriteLine("Emergency stop: run `capri task stop` from another terminal. Reset for future tasks with `capri task reset-stop`.");
        _console.WriteLine("");

        TaskPhase final = TaskPhase.Created;
        try
        {
            await foreach (var progress in coordinator.RunAsync(request, taskId, cancellationToken).ConfigureAwait(false))
            {
                final = progress.Phase;
                RenderProgress(progress);
            }
        }
        catch (OperationCanceledException)
        {
            _console.WriteLine("[Cancelled] Task cancelled. If an external action was already dispatched, it may have occurred. No automatic replay.");
            return CliExitCodes.Cancelled;
        }

        if (cancellationToken.IsCancellationRequested)
            return CliExitCodes.Cancelled;

        return final switch
        {
            TaskPhase.Completed => CliExitCodes.Success,
            TaskPhase.Cancelled => CliExitCodes.Error,
            TaskPhase.Failed => CliExitCodes.Error,
            _ => CliExitCodes.Error,
        };
    }

    private void RenderProgress(TaskProgress progress)
    {
        var prefix = progress.Phase switch
        {
            TaskPhase.Planning => "[Planning]",
            TaskPhase.AwaitingPermission => "[Approval]",
            TaskPhase.Executing => "[Executing]",
            TaskPhase.Observing => "[Observing]",
            TaskPhase.Verifying => "[Verifying]",
            TaskPhase.Completed => "[Completed]",
            TaskPhase.Failed => "[Failed]",
            TaskPhase.Cancelled => "[Cancelled]",
            _ => "[Task]",
        };
        var model = !string.IsNullOrWhiteSpace(progress.ModelId) ? $" ({progress.ProviderId}/{progress.ModelId})" : string.Empty;
        _console.WriteLine($"{prefix}{model} {progress.Message}");
    }

    private async Task<int> ListTasksAsync(bool json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var history = _services.TaskHistory?.GetRecent(20) ?? [];
        var active = _services.TaskControl?.GetActive();
        var stop = _services.TaskControl?.GetStopState();

        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new
            {
                ok = true,
                stop = stop is null ? null : new { stop.Stopped, stop.Generation, updatedUtc = stop.UpdatedUtc },
                active = active is null ? null : new { taskId = active.TaskId, processId = active.ProcessId, startedUtc = active.StartedUtc },
                tasks = history.Select(SafeTaskDto),
            }));
            return CliExitCodes.Success;
        }

        _console.WriteLine($"Emergency stop: {(stop?.Stopped == true ? $"LATCHED (generation {stop.Generation})" : "not latched")}. Use `capri task reset-stop` for future tasks.");
        if (active is not null)
            _console.WriteLine($"Active task: {active.TaskId} (PID {active.ProcessId}, started {active.StartedUtc:u})");
        else
            _console.WriteLine("Active task: none");
        _console.WriteLine("");
        if (history.Count == 0)
        {
            _console.WriteLine("No task history metadata.");
            return CliExitCodes.Success;
        }

        foreach (var entry in history)
        {
            _console.WriteLine($"- {entry.TaskId}  {entry.Phase,-18} step {entry.StepIndex}{FormatTotal(entry.TotalSteps)}  updated {entry.UpdatedUtc:u}  {FormatVerification(entry)}");
        }
        return CliExitCodes.Success;
    }

    private async Task<int> StatusAsync(CliInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.Arguments.Count < 2 || !Guid.TryParse(invocation.Arguments[1], out var id))
        {
            return WriteJsonOrError(invocation.Json, false, "Usage: capri task status <id> [--json]", CliExitCodes.Usage);
        }

        var entry = _services.TaskHistory?.Find(id);
        var active = _services.TaskControl?.GetActive();
        var isActive = active?.TaskId == id;
        if (entry is null && !isActive)
            return WriteJsonOrError(invocation.Json, false, "Task id not found in local metadata history.", CliExitCodes.Error, new { taskId = id });

        if (invocation.Json)
        {
            _console.WriteLine(JsonOutput.Serialize(new
            {
                ok = true,
                taskId = id,
                active = isActive,
                metadata = entry is null ? null : SafeTaskDto(entry),
                note = "Metadata only: no prompts, plans, targets, note text, or credentials.",
            }));
            return CliExitCodes.Success;
        }

        _console.WriteLine($"Task: {id}");
        _console.WriteLine($"Active process ownership: {(isActive ? $"yes (PID {active!.ProcessId})" : "no")}");
        if (entry is not null)
        {
            _console.WriteLine($"Phase: {entry.Phase}");
            _console.WriteLine($"Step: {entry.StepIndex}{FormatTotal(entry.TotalSteps)}");
            _console.WriteLine($"Provider/model: {entry.ProviderId ?? "(unknown)"}/{entry.ModelId ?? "(unknown)"}");
            _console.WriteLine($"Last step verified: {FormatBool(entry.LastStepVerified)}");
            _console.WriteLine($"Last step dispatched: {FormatBool(entry.LastStepDispatched)}");
            _console.WriteLine($"Updated (UTC): {entry.UpdatedUtc:u}");
            _console.WriteLine($"Category: {entry.FinalCategory ?? "(none)"}");
        }
        _console.WriteLine("No prompts, plan targets, note contents, or credentials are shown.");
        return CliExitCodes.Success;
    }

    private async Task<int> WatchAsync(CliInvocation invocation, CancellationToken cancellationToken)
    {
        if (invocation.Arguments.Count != 2 || !Guid.TryParse(invocation.Arguments[1], out var id))
            return Usage("Usage: capri task watch <id> [--json]");
        return await TaskWatch.ObserveAsync(id, _services, _console, invocation.Json, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> DesktopStatusAsync(bool json, CancellationToken cancellationToken)
    {
        var status = await TaskStatusPipe.QueryAsync(cancellationToken).ConfigureAwait(false);
        if (status is null)
            return WriteJsonOrError(json, false, "No Capri desktop status listener is reachable for the current Windows user.", CliExitCodes.Error);

        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new
            {
                ok = true,
                connected = true,
                status.Version,
                status.CheckedUtc,
                status.Stopped,
                status.StopGeneration,
                status.ActiveTaskId,
                status.ActiveProcessId,
            }));
        }
        else
        {
            _console.WriteLine("Capri desktop: connected (read-only per-user status IPC).");
            _console.WriteLine($"Emergency stop: {(status.Stopped ? "latched" : "clear")} (generation {status.StopGeneration}).");
            _console.WriteLine(status.ActiveTaskId.HasValue
                ? $"Active task: {status.ActiveTaskId} (PID {status.ActiveProcessId})"
                : "Active task: none");
            _console.WriteLine("This command never executes actions or grants approval.");
        }
        return CliExitCodes.Success;
    }

    private async Task<int> HistoryAsync(bool json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var history = _services.TaskHistory?.GetRecent(50) ?? [];
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, tasks = history.Select(SafeTaskDto) }));
            return CliExitCodes.Success;
        }
        if (history.Count == 0) { _console.WriteLine("No task history metadata."); return CliExitCodes.Success; }
        foreach (var e in history)
            _console.WriteLine($"{e.TaskId}  {e.CreatedUtc:u}  {e.Phase}  step {e.StepIndex}{FormatTotal(e.TotalSteps)}  verified={FormatBool(e.LastStepVerified)}  dispatched={FormatBool(e.LastStepDispatched)}");
        return CliExitCodes.Success;
    }

    private int Stop(bool json)
    {
        var control = _services.TaskControl;
        if (control is null) return WriteJsonOrError(json, false, "Task control is unavailable.", CliExitCodes.Error);
        var state = control.RequestStop("cli task stop");
        var active = control.GetActive();
        var message = active is not null
            ? $"Emergency stop latched (generation {state.Generation}). Active task {active.TaskId} (PID {active.ProcessId}) will be cancelled at the next coordinator check; already-dispatched external actions may have occurred."
            : $"Emergency stop latched (generation {state.Generation}). No active task ownership found; future queued work is blocked until reset.";
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, stopped = true, generation = state.Generation, activeTaskId = active?.TaskId, activeProcessId = active?.ProcessId, message }));
            return CliExitCodes.Success;
        }
        _console.WriteLine(message);
        return CliExitCodes.Success;
    }

    private int ResetStop(bool json)
    {
        var control = _services.TaskControl;
        if (control is null) return WriteJsonOrError(json, false, "Task control is unavailable.", CliExitCodes.Error);
        var state = control.ResetStop();
        const string message = "Emergency stop reset for future tasks. Cancelled tasks are never revived or replayed.";
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, stopped = state.Stopped, generation = state.Generation, message }));
            return CliExitCodes.Success;
        }
        _console.WriteLine(message);
        return CliExitCodes.Success;
    }

    internal static object SafeTaskDto(TaskHistoryEntry e) => new
    {
        taskId = e.TaskId,
        createdUtc = e.CreatedUtc,
        updatedUtc = e.UpdatedUtc,
        phase = e.Phase,
        stepIndex = e.StepIndex,
        totalSteps = e.TotalSteps,
        providerId = e.ProviderId,
        modelId = e.ModelId,
        lastStepVerified = e.LastStepVerified,
        lastStepDispatched = e.LastStepDispatched,
        finalCategory = e.FinalCategory,
    };

    private static string FormatTotal(int? total) => total.HasValue ? $"/{total.Value}" : string.Empty;
    private static string FormatVerification(TaskHistoryEntry e) => $"verified={FormatBool(e.LastStepVerified)} dispatched={FormatBool(e.LastStepDispatched)}";
    private static string FormatBool(bool? value) => value.HasValue ? (value.Value ? "yes" : "no") : "unknown";

    private int WriteJsonOrError(bool json, bool ok, string message, int exitCode, object? extra = null)
    {
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { ok, message, data = extra }));
            return exitCode;
        }
        if (ok) _console.WriteLine(message); else _console.WriteErrorLine($"capri: {message}");
        return exitCode;
    }

    private int Usage(string message)
    {
        _console.WriteErrorLine($"capri: {message}");
        return CliExitCodes.Usage;
    }
}
