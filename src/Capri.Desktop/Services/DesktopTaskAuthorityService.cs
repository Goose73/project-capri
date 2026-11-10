using System.IO;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;
using Capri.Core.Autonomy.Authority;
using Capri.Core.Providers;
using Capri.Security.Autonomy;
using Capri.Security.Autonomy.Authority;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Capri.Desktop.Services;

/// <summary>
/// Desktop integration with the per-user task authority. Prefers connecting to an existing authority;
/// if none is reachable, hosts an in-process authority so the desktop can be the authoritative owner.
/// Subscribes to events and routes ApprovalRequired to the UI bridge (explicit user action only).
/// </summary>
public sealed class DesktopTaskAuthorityService : BackgroundService, ITaskRunner
{
    private static readonly Action<ILogger, Exception?> LogHostFailed =
        LoggerMessage.Define(LogLevel.Warning, new EventId(5201, "AuthorityHostFailed"), "Desktop could not host task authority; will retry as client.");
    private static readonly Action<ILogger, string, Exception?> LogInfo =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(5202, "AuthorityInfo"), "{Message}");

    private readonly ICapriPaths _paths;
    private readonly ITaskHistoryStore _history;
    private readonly IChatProvider _provider;
    private readonly IModelRouter _router;
    private readonly ITaskExecutor _executor;
    private readonly ITaskJournal _journal;
    private readonly ITaskControl _control;
    private readonly ITaskAuthorityUiBridge _ui;
    private readonly ILogger<DesktopTaskAuthorityService> _logger;
    private readonly string _pipeName;
    private readonly object _sync = new();
    private NamedPipeTaskAuthorityServer? _host;
    private NamedPipeTaskAuthorityClient? _client;
    private AuthorityStateSnapshot? _lastState;
    private string _status = "Not connected";
    private CancellationTokenSource? _pendingApprovalCancellation;
    private string? _pendingApprovalTaskId;

    public DesktopTaskAuthorityService(
        ICapriPaths paths,
        ITaskHistoryStore history,
        IChatProvider provider,
        IModelRouter router,
        ITaskExecutor executor,
        ITaskJournal journal,
        ITaskControl control,
        ITaskAuthorityUiBridge ui,
        ILogger<DesktopTaskAuthorityService> logger,
        string? pipeName = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipeName = pipeName ?? NamedPipeTaskAuthorityClient.CurrentUserPipeName();
    }

    public event EventHandler? StateChanged;
    public AuthorityStateSnapshot? LastState { get { lock (_sync) return _lastState; } }
    public string ConnectionStatus { get { lock (_sync) return _status; } }
    public bool IsHosting { get { lock (_sync) return _host is not null; } }

    bool ITaskRunner.IsStopped => LastState?.Stopped == true;

    void ITaskRunner.Stop() => _ = SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.Stop));

    void ITaskRunner.ResetStop() => _ = SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.ResetStop));

    IAsyncEnumerable<TaskProgress> ITaskRunner.RunAsync(string goal, CancellationToken cancellationToken) => SubmitAndWatchAsync(goal, cancellationToken);

    public async Task<AuthorityResponse> SendAsync(AuthorityRequest request, CancellationToken ct = default)
    {
        var client = Volatile.Read(ref _client);
        if (client is null) return new AuthorityResponse(request.RequestId, false, Error: "Task authority not connected.");
        return await client.SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>Submits a task through the authority and yields progress. Approvals are handled by the service's UI bridge.</summary>
    public async IAsyncEnumerable<TaskProgress> SubmitAndWatchAsync(string goal, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(goal)) yield break;
        await using var watcher = new NamedPipeTaskAuthorityClient(_pipeName, "capri-desktop-submit", canApprove: false, connectTimeout: TimeSpan.FromSeconds(5));
        await watcher.EnsureConnectedAsync(ct).ConfigureAwait(false);
        await watcher.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.Subscribe), ct).ConfigureAwait(false);
        var submit = await watcher.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.SubmitTask, Goal: goal), ct).ConfigureAwait(false);
        if (!submit.Ok)
        {
            yield return new TaskProgress(TaskPhase.Failed, submit.Error ?? "Task submission failed.");
            yield break;
        }
        var taskId = submit.Task?.TaskId ?? submit.State?.ActiveTask?.TaskId;
        if (taskId is null)
        {
            yield return new TaskProgress(TaskPhase.Failed, "Authority did not return a task id.");
            yield break;
        }
        await foreach (var evt in watcher.Events(ct).ConfigureAwait(false))
        {
            if (evt.Progress is { } p && (p.TaskId == taskId || p.TaskId is null))
                yield return p with { TaskId = taskId };
            if (evt.Kind == AuthorityEventKind.TaskCompleted && (evt.Task?.TaskId == taskId || evt.State?.ActiveTask is null))
            {
                if (evt.Task?.TaskId == taskId) yield return new TaskProgress(Enum.TryParse<TaskPhase>(evt.Task.Phase, true, out var tp) ? tp : TaskPhase.Completed, evt.Task.Message ?? "Task finished.", taskId, evt.Task.StepIndex, evt.Task.TotalSteps, evt.Task.ProviderId, evt.Task.ModelId, evt.Task.LastVerified, evt.Task.LastDispatched);
                yield break;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectOrHostAsync(stoppingToken).ConfigureAwait(false);
                await RunEventLoopAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                LogHostFailed(_logger, ex);
                SetStatus("Disconnected; retrying...");
                await CleanupAsync().ConfigureAwait(false);
                try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false); } catch { break; }
            }
        }
        await CleanupAsync().ConfigureAwait(false);
    }

    private async Task ConnectOrHostAsync(CancellationToken ct)
    {
        var pipeName = _pipeName;
        // Try existing authority first.
        try
        {
            var client = new NamedPipeTaskAuthorityClient(pipeName, "capri-desktop", canApprove: true, connectTimeout: TimeSpan.FromMilliseconds(500));
            await client.EnsureConnectedAsync(ct).ConfigureAwait(false);
            await client.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.Hello, ClientName: "capri-desktop", CanApprove: true), ct).ConfigureAwait(false);
            await client.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.Subscribe), ct).ConfigureAwait(false);
            var state = await client.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.GetState), ct).ConfigureAwait(false);
            Volatile.Write(ref _client, client);
            lock (_sync) { _host = null; _lastState = state.State; _status = "Connected to task authority (client)."; }
            LogInfo(_logger, "Desktop connected to existing task authority.", null);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        catch
        {
            // No existing authority; host in-process.
        }

        NamedPipeTaskAuthorityServer? host = null;
        try
        {
            host = new NamedPipeTaskAuthorityServer(pipeName, _paths, _history, _provider, _router, _executor, _journal, _control);
            host.Start();
            // Connect to our own host for uniform event/command handling.
            var client = new NamedPipeTaskAuthorityClient(pipeName, "capri-desktop-host", canApprove: true, connectTimeout: TimeSpan.FromSeconds(3));
            await client.EnsureConnectedAsync(ct).ConfigureAwait(false);
            await client.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.Hello, ClientName: "capri-desktop", CanApprove: true), ct).ConfigureAwait(false);
            await client.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.Subscribe), ct).ConfigureAwait(false);
            var state = await client.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.GetState), ct).ConfigureAwait(false);
            Volatile.Write(ref _client, client);
            lock (_sync) { _host = host; _lastState = state.State; _status = "Hosting task authority (current user)."; }
            host = null;
            LogInfo(_logger, "Desktop hosting in-process task authority.", null);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (host is not null) await host.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunEventLoopAsync(CancellationToken ct)
    {
        var client = Volatile.Read(ref _client);
        if (client is null) return;
        await foreach (var evt in client.Events(ct).ConfigureAwait(false))
        {
            lock (_sync)
            {
                _lastState = evt.State;
                if (_pendingApprovalCancellation is not null && evt.State is { } state &&
                    (state.Stopped || state.ActiveTask?.TaskId != _pendingApprovalTaskId))
                    _pendingApprovalCancellation.Cancel();
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            if (evt.Kind == AuthorityEventKind.ApprovalRequired && evt.Approval is { } approval)
            {
                _ = Task.Run(async () => await HandleApprovalAsync(approval, ct).ConfigureAwait(false), CancellationToken.None);
            }
        }
    }

    private async Task HandleApprovalAsync(ApprovalRequestSnapshot approval, CancellationToken serviceCt)
    {
        var originatingClient = Volatile.Read(ref _client);
        if (originatingClient is null) return;
        using var approvalCts = CancellationTokenSource.CreateLinkedTokenSource(serviceCt);
        lock (_sync)
        {
            // Never leave two independently actionable dialogs for the same authority.
            _pendingApprovalCancellation?.Cancel();
            _pendingApprovalCancellation = approvalCts;
            _pendingApprovalTaskId = approval.TaskId;
            if (_lastState is { } state && (state.Stopped || state.ActiveTask?.TaskId != approval.TaskId))
                approvalCts.Cancel();
        }
        AuthorityApprovalDecision? decision;
        try { decision = await _ui.RequestApprovalAsync(approval, approvalCts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { decision = null; }
        catch { decision = null; }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pendingApprovalCancellation, approvalCts))
                {
                    _pendingApprovalCancellation = null;
                    _pendingApprovalTaskId = null;
                }
            }
        }

        // A dismissed/cancelled prompt is not a fresh user decision. In particular,
        // never forward a decision from an old authority connection to a new one.
        if (decision is null || approvalCts.IsCancellationRequested || serviceCt.IsCancellationRequested) return;
        var client = Volatile.Read(ref _client);
        if (!ReferenceEquals(client, originatingClient)) return;
        // Re-read authoritative state after the human has interacted with the dialog.
        // A cancellation, stop, or task replacement may have occurred while it was open.
        // The server remains responsible for final approvalId/planHash validation.
        AuthorityResponse current;
        try
        {
            current = await client.SendAsync(new AuthorityRequest(Guid.NewGuid().ToString("N"), AuthorityRequestKind.GetState), serviceCt).ConfigureAwait(false);
        }
        catch { return; }
        if (!current.Ok || current.State is null || current.State.Stopped ||
            current.State.ActiveTask?.TaskId != approval.TaskId ||
            !ReferenceEquals(Volatile.Read(ref _client), originatingClient)) return;

        var approved = decision.Approved;
        var req = new AuthorityRequest(Guid.NewGuid().ToString("N"),
            approved ? AuthorityRequestKind.Approve : AuthorityRequestKind.Deny,
            TaskId: approval.TaskId,
            ApprovalId: approval.ApprovalId,
            PlanHash: approval.PlanHash,
            Workspace: approved ? decision?.Workspace : null);
        try { await client.SendAsync(req, CancellationToken.None).ConfigureAwait(false); } catch { }
    }

    private void SetStatus(string status)
    {
        lock (_sync) _status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task CleanupAsync()
    {
        lock (_sync) _pendingApprovalCancellation?.Cancel();
        var client = Interlocked.Exchange(ref _client, null);
        if (client is not null) try { await client.DisposeAsync().ConfigureAwait(false); } catch { }
        NamedPipeTaskAuthorityServer? host;
        lock (_sync) { host = _host; _host = null; }
        if (host is not null) try { await host.DisposeAsync().ConfigureAwait(false); } catch { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await CleanupAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
