using System.Text.Json;
using Capri.Cli.Services;
using Capri.Core.Autonomy;
using Capri.Core.Autonomy.Authority;
using Capri.Security.Autonomy;
using Capri.Security.Autonomy.Authority;

namespace Capri.Cli.Commands;

/// <summary>
/// M4 task execution/control using the existing coordinator, plus M5 authority-client routing.
/// Read-only watch/desktop-status (#9/#10) remain available. In test/local fallback mode,
/// authority auto-connection is disabled to preserve isolation; production uses the per-user authority.
/// </summary>
public sealed class TaskCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ICliConsole _console;
    private readonly ICliServices _services;
    private readonly ITaskAuthorityClient? _authority;
    private readonly bool _allowLocalFallback;

    public TaskCommand(ICliConsole console, ICliServices services, ITaskAuthorityClient? authority = null, bool allowLocalFallback = true)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _authority = authority ?? services.TaskAuthority;
        _allowLocalFallback = allowLocalFallback;
    }

    public async Task<int> ExecuteAsync(CliInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        if (invocation.Kind == CliCommandKind.Tasks)
            return await ListTasksAsync(invocation.Json, cancellationToken).ConfigureAwait(false);

        if (invocation.Arguments.Count == 0)
            return Usage("Usage: capri task \"<request>\" | capri task status|watch <id> [--json] | desktop-status|stop|reset-stop|history [--json]");

        var sub = invocation.Arguments[0].ToLowerInvariant();
        return sub switch
        {
            "status" => await StatusAsync(invocation, cancellationToken).ConfigureAwait(false),
            "watch" => await WatchAsync(invocation, cancellationToken).ConfigureAwait(false),
            "desktop-status" => await DesktopStatusAsync(invocation.Json, cancellationToken).ConfigureAwait(false),
            "stop" => await StopAsync(invocation.Json, cancellationToken).ConfigureAwait(false),
            "reset-stop" => await ResetStopAsync(invocation.Json, cancellationToken).ConfigureAwait(false),
            "history" => await HistoryAsync(invocation.Json, cancellationToken).ConfigureAwait(false),
            _ => await RunTaskAsync(invocation.Arguments[0], cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<ITaskAuthorityClient?> TryGetAuthorityAsync(bool startIfMissing, CancellationToken ct)
    {
        if (_authority is not null) return _authority;
        if (_allowLocalFallback) return null; // preserve test isolation / local behavior
        var timeout = startIfMissing ? TimeSpan.FromSeconds(8) : TimeSpan.Zero;
        if (timeout == TimeSpan.Zero)
        {
            try
            {
                var quick = new NamedPipeTaskAuthorityClient(NamedPipeTaskAuthorityClient.CurrentUserPipeName(), "capri-cli", canApprove: true, connectTimeout: TimeSpan.FromMilliseconds(250));
                await quick.EnsureConnectedAsync(ct).ConfigureAwait(false);
                return quick;
            }
            catch { return null; }
        }
        return await TaskAuthorityConnector.TryConnectAsync("capri-cli", canApprove: true, timeout, ct).ConfigureAwait(false);
    }

    private async Task<int> RunTaskAsync(string request, CancellationToken cancellationToken)
    {
        if (!_console.IsInteractive)
        {
            _console.WriteErrorLine("capri: `capri task` requires an interactive terminal for safe approval. No actions were dispatched.");
            _console.WriteErrorLine("Run from a terminal, review the exact plan, and type `approve PLAN` to proceed.");
            return CliExitCodes.Usage;
        }

        var authority = await TryGetAuthorityAsync(startIfMissing: !_allowLocalFallback, cancellationToken).ConfigureAwait(false);
        if (authority is not null)
            return await RunTaskViaAuthorityAsync(authority, request, cancellationToken).ConfigureAwait(false);

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

    private async Task<int> RunTaskViaAuthorityAsync(ITaskAuthorityClient authority, string request, CancellationToken cancellationToken)
    {
        _console.WriteLine("Project Capri M5: routing task through the per-user task authority. No actions occur without explicit approval of the exact plan.");
        _console.WriteLine($"Request: {request}");
        _console.WriteLine("");
        var reqId = authority is NamedPipeTaskAuthorityClient nc ? nc.NextId() : Guid.NewGuid().ToString("N");
        var submit = await authority.SendAsync(new AuthorityRequest(reqId, AuthorityRequestKind.SubmitTask, Goal: request), cancellationToken).ConfigureAwait(false);
        if (!submit.Ok)
        {
            _console.WriteErrorLine($"capri: {submit.Error ?? "Task submission failed."}");
            return CliExitCodes.Error;
        }
        var taskId = submit.Task?.TaskId ?? submit.State?.ActiveTask?.TaskId;
        if (taskId is null) { _console.WriteErrorLine("capri: authority did not return a task id."); return CliExitCodes.Error; }
        _console.WriteLine($"Task: {taskId}");

        TaskPhase? finalPhase = null;
        try
        {
            await foreach (var evt in authority.Events(cancellationToken).ConfigureAwait(false))
            {
                if (evt.Approval is { } approval && approval.TaskId == taskId.Value)
                {
                    var decision = await HandleAuthorityApprovalAsync(approval, cancellationToken).ConfigureAwait(false);
                    var resp = await authority.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"),
                        decision.approved ? AuthorityRequestKind.Approve : AuthorityRequestKind.Deny,
                        TaskId: approval.TaskId, ApprovalId: approval.ApprovalId, PlanHash: approval.PlanHash, Workspace: decision.workspace), cancellationToken).ConfigureAwait(false);
                    if (!resp.Ok) _console.WriteErrorLine($"capri: approval rejected by authority: {resp.Error}");
                }
                if (evt.Progress is { } progress && (progress.TaskId == taskId.Value || progress.TaskId is null))
                {
                    finalPhase = progress.Phase;
                    RenderProgress(progress);
                }
                if (evt.Kind == AuthorityEventKind.TaskCompleted && (evt.Task?.TaskId == taskId.Value || evt.State?.ActiveTask is null))
                {
                    if (Enum.TryParse<TaskPhase>(evt.Task?.Phase, true, out var p)) finalPhase = p;
                    break;
                }
                if (finalPhase is TaskPhase.Completed or TaskPhase.Failed or TaskPhase.Cancelled && evt.State?.ActiveTask is null)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { await authority.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.CancelTask, TaskId: taskId), CancellationToken.None).ConfigureAwait(false); } catch { }
            _console.WriteLine("[Cancelled] Task cancellation requested through authority. Dispatched external actions may have occurred. No automatic replay.");
            return CliExitCodes.Cancelled;
        }

        return finalPhase switch
        {
            TaskPhase.Completed => CliExitCodes.Success,
            TaskPhase.Cancelled => cancellationToken.IsCancellationRequested ? CliExitCodes.Cancelled : CliExitCodes.Error,
            _ => CliExitCodes.Error,
        };
    }

    private async Task<(bool approved, string? workspace)> HandleAuthorityApprovalAsync(ApprovalRequestSnapshot approval, CancellationToken ct)
    {
        _console.WriteLine("");
        _console.WriteLine("=== Proposed task plan (exact; review carefully) ===");
        _console.WriteLine($"Goal: {approval.Goal}");
        _console.WriteLine($"Provider/model: {approval.ProviderId ?? "?"}/{approval.ModelId ?? "?"}");
        _console.WriteLine($"Plan hash (binds approval): {approval.PlanHash}");
        for (var i = 0; i < approval.Plan.Steps.Count; i++)
        {
            var step = approval.Plan.Steps[i];
            _console.WriteLine($"  {i + 1}. {step.Tool} target={step.Target}");
            if (step.Tool == TaskTool.CreateNote && step.Text is not null) _console.WriteLine($"     text: \"{Truncate(step.Text, 200)}\"");
        }
        _console.WriteLine("NO actions will be taken unless you explicitly approve this exact plan.");
        _console.Write("Approve? Type `approve PLAN` to continue, or press Enter to deny: ");
        var line = await _console.ReadLineAsync(ct).ConfigureAwait(false);
        var approved = string.Equals(line?.Trim(), "approve PLAN", StringComparison.Ordinal);
        if (!approved) { _console.WriteLine("Denied. No actions dispatched."); return (false, null); }
        string? workspace = null;
        if (approval.Plan.Steps.Any(s => s.Tool is TaskTool.CreateNote or TaskTool.OpenNote))
        {
            workspace = await PromptWorkspaceAsync(ct).ConfigureAwait(false);
            if (workspace is null) { _console.WriteLine("Denied (no valid workspace). No actions dispatched."); return (false, null); }
        }
        return (true, workspace);
    }

    private async Task<string?> PromptWorkspaceAsync(CancellationToken ct)
    {
        var candidates = new List<string>();
        try { var t = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CapriNotes")); candidates.Add(t); } catch { }
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop)) candidates.Add(desktop);
        _console.WriteLine("Select an existing disposable workspace folder for note actions (Capri will not create folders):");
        for (var i = 0; i < candidates.Count; i++) _console.WriteLine($"  {i + 1}. {candidates[i]}");
        _console.WriteLine($"  {candidates.Count + 1}. Enter a full path");
        _console.WriteLine($"  {candidates.Count + 2}. Cancel");
        _console.Write("> ");
        var choice = await _console.ReadLineAsync(ct).ConfigureAwait(false);
        if (!int.TryParse(choice?.Trim(), out var n)) return null;
        string? selected = null;
        if (n >= 1 && n <= candidates.Count) selected = candidates[n - 1];
        else if (n == candidates.Count + 1) { _console.Write("Path: "); selected = await _console.ReadLineAsync(ct).ConfigureAwait(false); }
        if (string.IsNullOrWhiteSpace(selected)) return null;
        try { selected = Path.GetFullPath(selected.Trim().Trim('"')); } catch { return null; }
        if (!Path.IsPathFullyQualified(selected) || selected.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Directory.Exists(selected))
        { _console.WriteErrorLine("capri: refusing unsafe or missing workspace path."); return null; }
        return selected;
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
        var authority = await TryGetAuthorityAsync(false, cancellationToken).ConfigureAwait(false);
        if (authority is not null)
        {
            var resp = await authority.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.ListTasks, MaxCount: 20), cancellationToken).ConfigureAwait(false);
            if (json) { _console.WriteLine(JsonSerializer.Serialize(new { ok = resp.Ok, stop = resp.State is null ? null : new { resp.State.Stopped, resp.State.StopGeneration, updatedUtc = resp.State.UpdatedUtc }, active = resp.State?.ActiveTask is null ? null : new { taskId = resp.State.ActiveTask.TaskId, phase = resp.State.ActiveTask.Phase }, tasks = (resp.Tasks ?? []).Select(SafeTaskDto) }, JsonOpts)); return resp.Ok ? CliExitCodes.Success : CliExitCodes.Error; }
        }
        var history = _services.TaskHistory?.GetRecent(20) ?? [];
        var active = _services.TaskControl?.GetActive();
        var stop = _services.TaskControl?.GetStopState();
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, stop = stop is null ? null : new { stop.Stopped, stop.Generation, updatedUtc = stop.UpdatedUtc }, active = active is null ? null : new { taskId = active.TaskId, processId = active.ProcessId, startedUtc = active.StartedUtc }, tasks = history.Select(SafeTaskDto) }));
            return CliExitCodes.Success;
        }
        _console.WriteLine($"Emergency stop: {(stop?.Stopped == true ? $"LATCHED (generation {stop.Generation})" : "not latched")}. Use `capri task reset-stop` for future tasks.");
        if (active is not null) _console.WriteLine($"Active task: {active.TaskId} (PID {active.ProcessId}, started {active.StartedUtc:u})"); else _console.WriteLine("Active task: none");
        _console.WriteLine("");
        if (history.Count == 0) { _console.WriteLine("No task history metadata."); return CliExitCodes.Success; }
        foreach (var entry in history) _console.WriteLine($"- {entry.TaskId}  {entry.Phase,-18} step {entry.StepIndex}{FormatTotal(entry.TotalSteps)}  updated {entry.UpdatedUtc:u}  {FormatVerification(entry)}");
        return CliExitCodes.Success;
    }

    private async Task<int> StatusAsync(CliInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.Arguments.Count < 2 || !Guid.TryParse(invocation.Arguments[1], out var id))
            return WriteJsonOrError(invocation.Json, false, "Usage: capri task status <id> [--json]", CliExitCodes.Usage);
        var entry = _services.TaskHistory?.Find(id);
        var active = _services.TaskControl?.GetActive();
        var isActive = active?.TaskId == id;
        if (entry is null && !isActive)
            return WriteJsonOrError(invocation.Json, false, "Task id not found in local metadata history.", CliExitCodes.Error, new { taskId = id });
        if (invocation.Json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, taskId = id, active = isActive, metadata = entry is null ? null : SafeTaskDto(entry), note = "Metadata only: no prompts, plans, targets, note text, or credentials." }));
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
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, connected = true, status.Version, status.CheckedUtc, status.Stopped, status.StopGeneration, status.ActiveTaskId, status.ActiveProcessId }));
        }
        else
        {
            _console.WriteLine("Capri desktop: connected (read-only per-user status IPC).");
            _console.WriteLine($"Emergency stop: {(status.Stopped ? "latched" : "clear")} (generation {status.StopGeneration}).");
            _console.WriteLine(status.ActiveTaskId.HasValue ? $"Active task: {status.ActiveTaskId} (PID {status.ActiveProcessId})" : "Active task: none");
            _console.WriteLine("This command never executes actions or grants approval.");
        }
        return CliExitCodes.Success;
    }

    private async Task<int> HistoryAsync(bool json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authority = await TryGetAuthorityAsync(false, cancellationToken).ConfigureAwait(false);
        if (authority is not null)
        {
            var resp = await authority.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.ListTasks, MaxCount: 50), cancellationToken).ConfigureAwait(false);
            if (json) { _console.WriteLine(JsonOutput.Serialize(new { ok = resp.Ok, tasks = (resp.Tasks ?? []).Select(SafeTaskDto) })); return resp.Ok ? CliExitCodes.Success : CliExitCodes.Error; }
        }
        var history = _services.TaskHistory?.GetRecent(50) ?? [];
        if (json) { _console.WriteLine(JsonOutput.Serialize(new { ok = true, tasks = history.Select(SafeTaskDto) })); return CliExitCodes.Success; }
        if (history.Count == 0) { _console.WriteLine("No task history metadata."); return CliExitCodes.Success; }
        foreach (var e in history) _console.WriteLine($"{e.TaskId}  {e.CreatedUtc:u}  {e.Phase}  step {e.StepIndex}{FormatTotal(e.TotalSteps)}  verified={FormatBool(e.LastStepVerified)}  dispatched={FormatBool(e.LastStepDispatched)}");
        return CliExitCodes.Success;
    }

    private async Task<int> StopAsync(bool json, CancellationToken ct)
    {
        var authority = await TryGetAuthorityAsync(false, ct).ConfigureAwait(false);
        if (authority is not null)
        {
            var resp = await authority.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.Stop), ct).ConfigureAwait(false);
            if (json) { _console.WriteLine(JsonSerializer.Serialize(new { ok = resp.Ok, stopped = true, generation = resp.State?.StopGeneration ?? 0L, activeTaskId = resp.State?.ActiveTask?.TaskId, activeProcessId = (int?)null, message = resp.Error ?? "Emergency stop requested through authority." }, JsonOpts)); return resp.Ok ? CliExitCodes.Success : CliExitCodes.Error; }
            _console.WriteLine(resp.Ok ? "Emergency stop latched via authority. Running tasks will cancel when possible; dispatched external actions may have occurred." : $"capri: {resp.Error}");
            return resp.Ok ? CliExitCodes.Success : CliExitCodes.Error;
        }
        return Stop(json);
    }

    private async Task<int> ResetStopAsync(bool json, CancellationToken ct)
    {
        var authority = await TryGetAuthorityAsync(false, ct).ConfigureAwait(false);
        if (authority is not null)
        {
            var resp = await authority.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.ResetStop), ct).ConfigureAwait(false);
            const string msg = "Emergency stop reset for future tasks. Cancelled tasks are never revived or replayed.";
            if (json) { _console.WriteLine(JsonSerializer.Serialize(new { ok = resp.Ok, stopped = resp.State?.Stopped ?? false, generation = resp.State?.StopGeneration ?? 0L, message = msg }, JsonOpts)); return resp.Ok ? CliExitCodes.Success : CliExitCodes.Error; }
            _console.WriteLine(resp.Ok ? msg : $"capri: {resp.Error}");
            return resp.Ok ? CliExitCodes.Success : CliExitCodes.Error;
        }
        return ResetStop(json);
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
        if (json) { _console.WriteLine(JsonOutput.Serialize(new { ok = true, stopped = true, generation = state.Generation, activeTaskId = active?.TaskId, activeProcessId = active?.ProcessId, message })); return CliExitCodes.Success; }
        _console.WriteLine(message);
        return CliExitCodes.Success;
    }

    private int ResetStop(bool json)
    {
        var control = _services.TaskControl;
        if (control is null) return WriteJsonOrError(json, false, "Task control is unavailable.", CliExitCodes.Error);
        var state = control.ResetStop();
        const string message = "Emergency stop reset for future tasks. Cancelled tasks are never revived or replayed.";
        if (json) { _console.WriteLine(JsonOutput.Serialize(new { ok = true, stopped = state.Stopped, generation = state.Generation, message })); return CliExitCodes.Success; }
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
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    private int WriteJsonOrError(bool json, bool ok, string message, int exitCode, object? extra = null)
    {
        if (json) { _console.WriteLine(JsonOutput.Serialize(new { ok, message, data = extra })); return exitCode; }
        if (ok) _console.WriteLine(message); else _console.WriteErrorLine($"capri: {message}");
        return exitCode;
    }

    private int Usage(string message)
    {
        _console.WriteErrorLine($"capri: {message}");
        return CliExitCodes.Usage;
    }
}
