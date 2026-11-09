using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Channels;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;
using Capri.Core.Autonomy.Authority;
using Capri.Core.Providers;

namespace Capri.Security.Autonomy.Authority;

/// <summary>
/// Per-user task authority host. Owns the real coordinator/executor stack; clients connect via current-user named pipe.
/// All approval requires an explicit client response bound to taskId + planHash + approvalId. No automatic replay.
/// </summary>
public sealed class NamedPipeTaskAuthorityServer : IAsyncDisposable
{
    private sealed class ClientConnection : IAsyncDisposable
    {
        public required string Id { get; init; }
        public required NamedPipeServerStream Pipe { get; init; }
        public required Channel<AuthorityEvent> Events { get; init; }
        public required SemaphoreSlim WriteLock { get; init; }
        public string? Name { get; set; }
        public bool CanApprove { get; set; }
        public bool Subscribed { get; set; }

        public async Task SendEventAsync(AuthorityEvent evt, CancellationToken ct)
        {
            if (!Subscribed) return;
            await WriteLock.WaitAsync(ct).ConfigureAwait(false);
            try { await JsonPipeFraming.WriteAsync(Pipe, new AuthorityEnvelope(Event: evt), TaskAuthorityProtocol.Json, TaskAuthorityProtocol.MaxFrameBytes, ct).ConfigureAwait(false); }
            finally { WriteLock.Release(); }
        }

        public ValueTask DisposeAsync()
        {
            try { Pipe.Dispose(); } catch { }
            WriteLock.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AuthorityApproval : ITaskApproval
    {
        private readonly NamedPipeTaskAuthorityServer _server;
        public AuthorityApproval(NamedPipeTaskAuthorityServer server) => _server = server;
        public void SetContext(TaskApprovalContext context) => _server.SetApprovalContext(context);
        public Task<string?> ApproveAsync(TaskPlan plan, CancellationToken cancellationToken) => _server.RequestApprovalAsync(plan, cancellationToken);
    }

    private sealed class AuthorityJournal : ITaskJournal
    {
        private readonly ITaskJournal _inner;
        private readonly NamedPipeTaskAuthorityServer _server;
        public AuthorityJournal(ITaskJournal inner, NamedPipeTaskAuthorityServer server) { _inner = inner; _server = server; }
        public void Record(Guid id, TaskPhase phase, int stepIndex) => _inner.Record(id, phase, stepIndex);
        public void RecordTaskStarted(Guid id, string? providerId, string? modelId) { _inner.RecordTaskStarted(id, providerId, modelId); _server.UpdateTaskIdentity(id, providerId, modelId); }
        public void RecordTaskPhase(Guid id, TaskPhase phase, int stepIndex, int? totalSteps = null, string? message = null, bool? verified = null, bool? dispatched = null)
        { _inner.RecordTaskPhase(id, phase, stepIndex, totalSteps, message, verified, dispatched); _server.UpdateTaskPhase(id, phase, stepIndex, totalSteps, message, verified, dispatched); }
        public void RecordTaskCompleted(Guid id, TaskPhase finalPhase, int stepIndex, int? totalSteps = null, bool? lastVerified = null, bool? lastDispatched = null)
        { _inner.RecordTaskCompleted(id, finalPhase, stepIndex, totalSteps, lastVerified, lastDispatched); _server.CompleteTask(id, finalPhase, stepIndex, totalSteps, lastVerified, lastDispatched); }
    }

    private readonly string _pipeName;
    private readonly ICapriPaths _paths;
    private readonly ITaskHistoryStore _history;
    private readonly Func<ITaskApproval, ITaskJournal, TaskCoordinator> _coordinatorFactory;
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateSync = new();
    private Task? _acceptLoop;
    private bool _stopped;
    private long _stopGeneration;
    private Guid? _activeTaskId;
    private string? _activeGoal;
    private string _activePhase = "idle";
    private int _activeStep;
    private int? _activeTotal;
    private string? _activeProvider;
    private string? _activeModel;
    private string? _activeMessage;
    private bool? _activeVerified;
    private bool? _activeDispatched;
    private DateTimeOffset _activeUpdated = DateTimeOffset.UtcNow;
    private TaskApprovalContext _approvalContext = new("(unknown)", null, null);
    private ApprovalRequestSnapshot? _pendingApproval;
    private TaskCompletionSource<ApprovalDecision?>? _pendingApprovalTcs;
    private CancellationTokenSource? _activeTaskCts;
    private TaskCoordinator? _coordinator;

    private sealed record ApprovalDecision(bool Approved, string? Workspace, Guid ApprovalId, string PlanHash);

    public NamedPipeTaskAuthorityServer(
        string pipeName,
        ICapriPaths paths,
        ITaskHistoryStore history,
        IChatProvider provider,
        IModelRouter router,
        ITaskExecutor executor,
        ITaskJournal journal,
        ITaskControl? control = null)
    {
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(journal);
        _coordinatorFactory = (approval, j) => control is null
            ? new TaskCoordinator(provider, router, approval, executor, j)
            : new TaskCoordinator(provider, router, approval, executor, j, control);
        _coordinator = _coordinatorFactory(new AuthorityApproval(this), new AuthorityJournal(journal, this));
    }

    public void Start()
    {
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public AuthorityStateSnapshot GetStateSnapshot()
    {
        lock (_stateSync)
        {
            return new AuthorityStateSnapshot(_stopped, _stopGeneration, BuildTaskSnapshot(), _pendingApproval, DateTimeOffset.UtcNow);
        }
    }

    private TaskSnapshot? BuildTaskSnapshot()
    {
        if (_activeTaskId is null) return null;
        return new TaskSnapshot(_activeTaskId.Value, _activeGoal ?? "", _activePhase, _activeStep, _activeTotal, _activeProvider, _activeModel, _activeMessage, _activeVerified, _activeDispatched, _pendingApproval is not null, _activeUpdated);
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var ps = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new InvalidOperationException("Current user SID unavailable.");
        ps.SetOwner(sid);
        ps.SetAccessRuleProtection(true, false);
        ps.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        // Allow SYSTEM/Admin for service/debug scenarios? No - requirement states current-user access restrictions; keep strict.
        return ps;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.WriteThrough, 0, 0, CreatePipeSecurity());
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var conn = new ClientConnection
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Pipe = pipe,
                    Events = Channel.CreateUnbounded<AuthorityEvent>(),
                    WriteLock = new SemaphoreSlim(1, 1)
                };
                _clients[conn.Id] = conn;
                pipe = null; // owned
                _ = Task.Run(() => HandleClientAsync(conn, ct), CancellationToken.None);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { pipe?.Dispose(); break; }
            catch { pipe?.Dispose(); try { await Task.Delay(100, ct).ConfigureAwait(false); } catch { } }
        }
    }

    private async Task HandleClientAsync(ClientConnection conn, CancellationToken serverCt)
    {
        try
        {
            while (!serverCt.IsCancellationRequested && conn.Pipe.IsConnected)
            {
                var req = await JsonPipeFraming.ReadAsync<AuthorityRequest>(conn.Pipe, TaskAuthorityProtocol.Json, TaskAuthorityProtocol.MaxFrameBytes, serverCt).ConfigureAwait(false);
                if (req is null) break;
                var resp = await HandleRequestAsync(conn, req, serverCt).ConfigureAwait(false);
                await conn.WriteLock.WaitAsync(serverCt).ConfigureAwait(false);
                try { await JsonPipeFraming.WriteAsync(conn.Pipe, new AuthorityEnvelope(Response: resp), TaskAuthorityProtocol.Json, TaskAuthorityProtocol.MaxFrameBytes, serverCt).ConfigureAwait(false); }
                finally { conn.WriteLock.Release(); }
            }
        }
        catch (OperationCanceledException) when (serverCt.IsCancellationRequested) { }
        catch { }
        finally
        {
            _clients.TryRemove(conn.Id, out _);
            await conn.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<AuthorityResponse> HandleRequestAsync(ClientConnection conn, AuthorityRequest req, CancellationToken ct)
    {
        try
        {
            switch (req.Kind)
            {
                case AuthorityRequestKind.Hello:
                    conn.Name = San(req.ClientName, 80);
                    conn.CanApprove = req.CanApprove == true;
                    return Ok(req, GetStateSnapshot());
                case AuthorityRequestKind.Subscribe:
                    conn.Subscribed = true;
                    await conn.SendEventAsync(new AuthorityEvent(AuthorityEventKind.Subscribed, GetStateSnapshot()), ct).ConfigureAwait(false);
                    return Ok(req, GetStateSnapshot());
                case AuthorityRequestKind.Unsubscribe:
                    conn.Subscribed = false;
                    return Ok(req, GetStateSnapshot());
                case AuthorityRequestKind.GetState:
                    return Ok(req, GetStateSnapshot());
                case AuthorityRequestKind.ListTasks:
                    return new AuthorityResponse(req.RequestId, true, State: GetStateSnapshot(), Tasks: _history.GetRecent(Math.Clamp(req.MaxCount ?? 20, 1, 50)));
                case AuthorityRequestKind.GetTask:
                    if (req.TaskId is null) return Fail(req, "taskId required.");
                    return new AuthorityResponse(req.RequestId, true, State: GetStateSnapshot(), Task: FindTask(req.TaskId.Value));
                case AuthorityRequestKind.SubmitTask:
                    return await SubmitTaskAsync(req, ct).ConfigureAwait(false);
                case AuthorityRequestKind.Approve:
                case AuthorityRequestKind.Deny:
                    return ResolveApproval(req);
                case AuthorityRequestKind.CancelTask:
                    return CancelActiveTask(req);
                case AuthorityRequestKind.Stop:
                    _coordinator?.Stop(); lock (_stateSync) { _stopped = true; _stopGeneration++; } BroadcastState(); return Ok(req, GetStateSnapshot());
                case AuthorityRequestKind.ResetStop:
                    _coordinator?.ResetStop(); lock (_stateSync) { _stopped = false; } BroadcastState(); return Ok(req, GetStateSnapshot());
                case AuthorityRequestKind.Ping:
                    return Ok(req, GetStateSnapshot());
                default:
                    return Fail(req, "Unsupported request.");
            }
        }
        catch (Exception)
        {
            return Fail(req, "Request failed.");
        }
    }

    private TaskSnapshot? FindTask(Guid taskId)
    {
        lock (_stateSync) { if (_activeTaskId == taskId) return BuildTaskSnapshot(); }
        var h = _history.Find(taskId);
        return h is null ? null : new TaskSnapshot(h.TaskId, "(recorded)", h.Phase, h.StepIndex, h.TotalSteps, h.ProviderId, h.ModelId, null, h.LastStepVerified, h.LastStepDispatched, false, h.UpdatedUtc);
    }

    private Task<AuthorityResponse> SubmitTaskAsync(AuthorityRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Goal)) return Task.FromResult(Fail(req, "goal required."));
        lock (_stateSync)
        {
            if (_stopped) return Task.FromResult(Fail(req, "Emergency stop is latched. Reset stop before submitting tasks."));
            if (_activeTaskId is not null) return Task.FromResult(Fail(req, "Another Capri task is already active."));
            _activeTaskId = Guid.NewGuid();
            _activeGoal = San(req.Goal, 2000);
            _activePhase = "created";
            _activeStep = 0; _activeTotal = null; _activeProvider = null; _activeModel = null; _activeMessage = "Task submitted to authority."; _activeVerified = null; _activeDispatched = null;
            _activeUpdated = DateTimeOffset.UtcNow;
            _pendingApproval = null;
            _activeTaskCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        }
        BroadcastState();
        var taskId = _activeTaskId!.Value;
        var goal = _activeGoal!;
        var taskCts = _activeTaskCts!;
        _ = Task.Run(async () => await RunTaskAsync(taskId, goal, taskCts.Token).ConfigureAwait(false), CancellationToken.None);
        return Task.FromResult(new AuthorityResponse(req.RequestId, true, State: GetStateSnapshot(), Task: GetStateSnapshot().ActiveTask));
    }

    private async Task RunTaskAsync(Guid taskId, string goal, CancellationToken ct)
    {
        try
        {
            if (_coordinator is null) throw new InvalidOperationException("Authority coordinator unavailable.");
            await foreach (var progress in _coordinator.RunAsync(goal, taskId, ct).ConfigureAwait(false))
            {
                lock (_stateSync)
                {
                    if (_activeTaskId == taskId)
                    {
                        _activePhase = progress.Phase.ToString();
                        _activeMessage = San(progress.Message, 500);
                        _activeStep = progress.StepIndex ?? _activeStep;
                        _activeTotal = progress.TotalSteps ?? _activeTotal;
                        _activeProvider = progress.ProviderId ?? _activeProvider;
                        _activeModel = progress.ModelId ?? _activeModel;
                        _activeVerified = progress.Verified;
                        _activeDispatched = progress.Dispatched;
                        _activeUpdated = DateTimeOffset.UtcNow;
                    }
                }
                Broadcast(new AuthorityEvent(AuthorityEventKind.TaskProgress, GetStateSnapshot(), GetStateSnapshot().ActiveTask, Progress: progress));
            }
        }
        catch { }
        finally
        {
            AuthorityEvent completed;
            lock (_stateSync)
            {
                var snap = _activeTaskId == taskId ? BuildTaskSnapshot() : null;
                if (_activeTaskId == taskId)
                {
                    _activeTaskCts?.Dispose();
                    _activeTaskCts = null;
                    _pendingApproval = null;
                    _pendingApprovalTcs = null;
                    _activeTaskId = null;
                    _activeGoal = null;
                    _activePhase = "idle";
                }
                completed = new AuthorityEvent(AuthorityEventKind.TaskCompleted, GetStateSnapshot(), snap);
            }
            Broadcast(completed);
        }
    }

    private AuthorityResponse ResolveApproval(AuthorityRequest req)
    {
        if (req.TaskId is null || string.IsNullOrWhiteSpace(req.PlanHash))
            return Fail(req, "taskId and planHash required.");
        lock (_stateSync)
        {
            var pending = _pendingApproval;
            var tcs = _pendingApprovalTcs;
            if (pending is null || tcs is null || pending.TaskId != req.TaskId.Value)
                return Fail(req, "No matching pending approval.");
            if (req.ApprovalId is not null && req.ApprovalId.Value != pending.ApprovalId)
                return Fail(req, "Approval id does not match pending request.");
            if (!string.Equals(pending.PlanHash, req.PlanHash, StringComparison.Ordinal))
                return Fail(req, "Approval plan hash does not match pending request.");
            if (pending.Plan.Steps.Any(s => s.Tool is TaskTool.CreateNote or TaskTool.OpenNote))
            {
                if (req.Kind == AuthorityRequestKind.Approve && (string.IsNullOrWhiteSpace(req.Workspace) || !Path.IsPathFullyQualified(req.Workspace!) || !Directory.Exists(req.Workspace)))
                    return Fail(req, "A selected existing workspace is required for note actions.");
            }
            var decision = new ApprovalDecision(req.Kind == AuthorityRequestKind.Approve, req.Kind == AuthorityRequestKind.Approve ? req.Workspace : null, pending.ApprovalId, pending.PlanHash);
            _pendingApproval = null;
            _pendingApprovalTcs = null;
            tcs.TrySetResult(decision);
            Broadcast(new AuthorityEvent(AuthorityEventKind.ApprovalResolved, GetStateSnapshot(), BuildTaskSnapshot()));
            return Ok(req, GetStateSnapshot());
        }
    }

    private AuthorityResponse CancelActiveTask(AuthorityRequest req)
    {
        CancellationTokenSource? cts;
        lock (_stateSync)
        {
            if (req.TaskId is not null && _activeTaskId != req.TaskId) return Fail(req, "Task is not active.");
            cts = _activeTaskCts;
            _pendingApprovalTcs?.TrySetResult(null);
        }
        try { cts?.Cancel(); } catch { }
        return Ok(req, GetStateSnapshot());
    }

    private void SetApprovalContext(TaskApprovalContext context) => _approvalContext = context;

    private void UpdateTaskIdentity(Guid id, string? providerId, string? modelId)
    {
        lock (_stateSync) { if (_activeTaskId == id) { _activeProvider = providerId; _activeModel = modelId; _activeUpdated = DateTimeOffset.UtcNow; } }
    }

    private void UpdateTaskPhase(Guid id, TaskPhase phase, int stepIndex, int? totalSteps, string? message, bool? verified, bool? dispatched)
    {
        lock (_stateSync)
        {
            if (_activeTaskId == id)
            {
                _activePhase = phase.ToString(); _activeStep = stepIndex; _activeTotal = totalSteps ?? _activeTotal;
                _activeMessage = San(message, 500); _activeVerified = verified; _activeDispatched = dispatched; _activeUpdated = DateTimeOffset.UtcNow;
            }
        }
    }

    private void CompleteTask(Guid id, TaskPhase phase, int stepIndex, int? totalSteps, bool? verified, bool? dispatched)
    {
        UpdateTaskPhase(id, phase, stepIndex, totalSteps, phase.ToString(), verified, dispatched);
    }

    private async Task<string?> RequestApprovalAsync(TaskPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var tcs = new TaskCompletionSource<ApprovalDecision?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ApprovalRequestSnapshot approval;
        lock (_stateSync)
        {
            if (_activeTaskId is null) throw new InvalidOperationException("No active task for approval.");
            approval = new ApprovalRequestSnapshot(
                _activeTaskId.Value,
                Guid.NewGuid(),
                _activeGoal ?? _approvalContext.Goal,
                _activeProvider ?? _approvalContext.ProviderId,
                _activeModel ?? _approvalContext.ModelId,
                plan,
                TaskAuthorityProtocol.ComputePlanHash(plan),
                DateTimeOffset.UtcNow);
            _pendingApproval = approval;
            _pendingApprovalTcs = tcs;
            _activePhase = TaskPhase.AwaitingPermission.ToString();
            _activeMessage = "Waiting for explicit approval of exact plan.";
            _activeUpdated = DateTimeOffset.UtcNow;
        }
        Broadcast(new AuthorityEvent(AuthorityEventKind.ApprovalRequired, GetStateSnapshot(), BuildTaskSnapshot(), Approval: approval));

        using var reg = cancellationToken.Register(() => tcs.TrySetResult(null));
        var decision = await tcs.Task.ConfigureAwait(false);
        // Decision is already consumed once; no replay.
        if (decision is null || !decision.Approved) return null;
        if (decision.ApprovalId != approval.ApprovalId || !string.Equals(decision.PlanHash, approval.PlanHash, StringComparison.Ordinal)) return null;
        return decision.Workspace ?? string.Empty; // non-null = approved; coordinator requires workspace only for note plans
    }

    private void BroadcastState() => Broadcast(new AuthorityEvent(AuthorityEventKind.StateChanged, GetStateSnapshot(), GetStateSnapshot().ActiveTask));

    private void Broadcast(AuthorityEvent evt)
    {
        foreach (var c in _clients.Values)
            _ = c.SendEventAsync(evt, CancellationToken.None);
    }

    private static AuthorityResponse Ok(AuthorityRequest req, AuthorityStateSnapshot state) => new(req.RequestId, true, State: state);
    private static AuthorityResponse Fail(AuthorityRequest req, string error) => new(req.RequestId, false, Error: error);
    private static string? San(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = new string(value.Where(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t').Take(max).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        try { _activeTaskCts?.Cancel(); } catch { }
        if (_acceptLoop is not null) try { await _acceptLoop.ConfigureAwait(false); } catch { }
        foreach (var c in _clients.Values) await c.DisposeAsync().ConfigureAwait(false);
        _clients.Clear();
        _cts.Dispose();
        _activeTaskCts?.Dispose();
    }
}
