using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Core.Providers;

namespace Capri.Core.Autonomy;

public interface ITaskApproval
{
    /// <summary>Returns selected existing disposable workspace for note plans; null means denied.</summary>
    Task<string?> ApproveAsync(TaskPlan plan, CancellationToken cancellationToken);

    /// <summary>Optional display context for approval UIs. Default is no-op for compatibility.</summary>
    void SetContext(TaskApprovalContext context) { }
}

public interface ITaskExecutor
{
    Task<TaskOutcome> ExecuteAsync(TaskStep action, string? workspace, CancellationToken cancellationToken);
}

public interface ITaskJournal
{
    void Record(Guid id, TaskPhase phase, int stepIndex);

    void RecordTaskStarted(Guid id, string? providerId, string? modelId) { }

    void RecordTaskPhase(
        Guid id,
        TaskPhase phase,
        int stepIndex,
        int? totalSteps = null,
        string? message = null,
        bool? verified = null,
        bool? dispatched = null) => Record(id, phase, stepIndex);

    void RecordTaskCompleted(
        Guid id,
        TaskPhase finalPhase,
        int stepIndex,
        int? totalSteps = null,
        bool? lastVerified = null,
        bool? lastDispatched = null) => Record(id, finalPhase, stepIndex);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Stop cancellation source is replaced on ResetStop and retained for the coordinator lifetime.")]
public sealed class TaskCoordinator
{
    // Absolute safety bounds. Local providers may receive a modestly larger
    // planning/execution budget, but never an unbounded one.
    private static readonly TimeSpan DefaultActiveBudget = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LocalActiveBudget = TimeSpan.FromMinutes(4);
    private const int DefaultPlanMaxOutputTokens = 1800;
    private const int LocalPlanMaxOutputTokens = 2048;
    private const int AbsolutePlanMaxOutputTokens = 4096;

    private const string PlanningSystemPrompt =
        "Return ONLY one JSON object: {\"steps\":[{\"tool\":\"create_note\",\"target\":\"name.txt\",\"text\":\"literal text\"}]}. " +
        "1 to 8 steps. Allowed tools: launch_app (target: notepad/calculator/explorer/paint), open_url (HTTPS URL), " +
        "brave_search (query), create_note (flat .txt filename and literal text), open_note (filename created earlier). " +
        "No markdown, extra fields or commands. If request cannot be expressed with these tools, return {\"steps\":[]}. " +
        "Never invent an existing file or assert an action succeeded.";

    private readonly IChatProvider _provider;
    private readonly IModelRouter _router;
    private readonly ITaskApproval _approval;
    private readonly ITaskExecutor _executor;
    private readonly ITaskJournal _journal;
    private readonly ITaskControl? _control;
    private readonly object _sync = new();
    private bool _stopped;
    private long _stopGeneration;
    private CancellationTokenSource _stopCancellation = new();

    public bool IsStopped
    {
        get
        {
            if (_control is not null) return _control.GetStopState().Stopped;
            lock (_sync) return _stopped;
        }
    }

    public TaskCoordinator(IChatProvider provider, IModelRouter router, ITaskApproval approval, ITaskExecutor executor, ITaskJournal journal)
        : this(provider, router, approval, executor, journal, control: null)
    {
    }

    public TaskCoordinator(
        IChatProvider provider,
        IModelRouter router,
        ITaskApproval approval,
        ITaskExecutor executor,
        ITaskJournal journal,
        ITaskControl? control)
    {
        _provider = provider;
        _router = router;
        _approval = approval;
        _executor = executor;
        _journal = journal;
        _control = control;
    }

    public void Stop()
    {
        if (_control is not null)
        {
            _control.RequestStop(reason: "stop");
            lock (_sync) { _stopped = true; _stopGeneration++; _stopCancellation.Cancel(); }
            return;
        }

        lock (_sync) { _stopped = true; _stopGeneration++; _stopCancellation.Cancel(); }
    }

    public void ResetStop()
    {
        if (_control is not null)
        {
            _control.ResetStop();
            lock (_sync) { if (_stopped) { _stopCancellation.Dispose(); _stopCancellation = new CancellationTokenSource(); } _stopped = false; }
            return;
        }

        lock (_sync) { if (_stopped) { _stopCancellation.Dispose(); _stopCancellation = new CancellationTokenSource(); } _stopped = false; }
    }

    public IAsyncEnumerable<TaskProgress> RunAsync(string goal, CancellationToken cancellationToken = default)
        => RunAsync(goal, Guid.NewGuid(), cancellationToken);

    public async IAsyncEnumerable<TaskProgress> RunAsync(
        string goal,
        Guid taskId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<TaskProgress>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var worker = RunCoreAsync(goal, taskId, channel.Writer, cancellationToken);
        // Drain the worker's final cancellation status even if the caller cancelled execution.
        await foreach (var update in channel.Reader.ReadAllAsync(CancellationToken.None)) yield return update;
        await worker;
    }

    private async Task RunCoreAsync(string goal, Guid id, ChannelWriter<TaskProgress> output, CancellationToken cancellationToken)
    {
        long generation;
        CancellationToken stopToken;
        lock (_sync) { generation = _stopGeneration; stopToken = _stopCancellation.Token; }
        var phase = TaskPhase.Created;
        var index = 0;
        var totalSteps = 0;
        string? providerId = null;
        string? modelId = null;
        bool? lastVerified = null;
        bool? lastDispatched = null;
        var ownsActive = false;

        void Record(TaskPhase next, string? message = null, bool? verified = null, bool? dispatched = null)
        {
            phase = next;
            if (verified.HasValue) lastVerified = verified;
            if (dispatched.HasValue) lastDispatched = dispatched;
            _journal.Record(id, phase, index);
            _journal.RecordTaskPhase(id, phase, index, totalSteps > 0 ? totalSteps : null, message, verified, dispatched);
        }

        void Emit(string message, bool? verified = null, bool? dispatched = null)
            => output.TryWrite(new TaskProgress(phase, message, id, index, totalSteps > 0 ? totalSteps : null, providerId, modelId, verified, dispatched));

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopToken);
        using var monitorEnd = new CancellationTokenSource();
        var monitor = Task.CompletedTask;
        var activeBudget = Stopwatch.StartNew();
        var activeBudgetLimit = DefaultActiveBudget;
        limit.CancelAfter(activeBudgetLimit);
        var ct = limit.Token;
        try
        {
            if (_control is not null)
            {
                generation = _control.GetStopState().Generation;
                monitor = MonitorStopAsync(_control, generation, limit, monitorEnd.Token);
                var claim = new TaskOwnership(id, Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime(), Environment.MachineName);
                if (!_control.TryAcquire(claim))
                    throw new InvalidOperationException("Another Capri task is already active. Use `capri task status`/`capri tasks` or wait for it to finish.");
                ownsActive = true;
            }

            Record(phase);
            Emit("Task created.");
            CheckStop(generation, ct);
            if (string.IsNullOrWhiteSpace(goal) || goal.Length > 2000) throw new FormatException("Task description must be 1–2000 characters.");
            Record(TaskPhase.Planning);
            Emit("Planning (no actions yet)...");
            var route = await _router.ResolveAsync(new ModelRouteRequest(RequiresStreaming: false), ct).AsTask().WaitAsync(ct);
            providerId = route.ProviderId;
            modelId = route.ModelId;
            _journal.RecordTaskStarted(id, providerId, modelId);
            var localProfile = ProviderCatalog.IsLocal(route.ProviderId);
            if (localProfile)
            {
                // Local models can be slower; allow a bounded larger budget. The
                // elapsed planning time still counts against this same budget.
                activeBudgetLimit = LocalActiveBudget;
                var localRemaining = activeBudgetLimit - activeBudget.Elapsed;
                limit.CancelAfter(localRemaining > TimeSpan.Zero ? localRemaining : TimeSpan.Zero);
            }

            var plan = await PlanWithSingleRepairAsync(goal, route, localProfile, generation, Emit, ct);
            totalSteps = plan.Steps.Count;
            CheckStop(generation, ct);
            Record(TaskPhase.AwaitingPermission);
            Emit("Waiting for approval of exact plan and targets...");
            // Human review has its own bounded grace period; it does not consume the
            // planning/execution budget, but caller cancellation and emergency stop still cancel it.
            activeBudget.Stop();
            var remainingBudget = activeBudgetLimit - activeBudget.Elapsed;
            limit.CancelAfter(Timeout.InfiniteTimeSpan);
            using var approvalLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            approvalLimit.CancelAfter(TimeSpan.FromMinutes(5));
            _approval.SetContext(new TaskApprovalContext(goal, providerId, modelId));
            var workspace = await _approval.ApproveAsync(plan, approvalLimit.Token).WaitAsync(approvalLimit.Token);
            CheckStop(generation, ct);
            if (workspace is null) { Record(TaskPhase.Cancelled, "Task denied; no actions dispatched."); Emit("Task denied; no actions dispatched."); return; }
            if (plan.Steps.Any(s => s.Tool is TaskTool.CreateNote or TaskTool.OpenNote) && (string.IsNullOrWhiteSpace(workspace) || !Path.IsPathFullyQualified(workspace)))
                throw new InvalidOperationException("A selected workspace is required.");
            activeBudget.Start();
            limit.CancelAfter(remainingBudget > TimeSpan.Zero ? remainingBudget : TimeSpan.Zero);
            for (index = 0; index < plan.Steps.Count; index++)
            {
                CheckStop(generation, ct);
                // Journal an in-flight step BEFORE dispatch. Never reload/replay journaled steps.
                Record(TaskPhase.Executing);
                Emit($"Dispatching step {index + 1}/{plan.Steps.Count}: {plan.Steps[index].Tool}.");
                using var stepLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stepLimit.CancelAfter(TimeSpan.FromSeconds(20));
                var outcome = await _executor.ExecuteAsync(plan.Steps[index], workspace, stepLimit.Token).WaitAsync(stepLimit.Token);
                CheckStop(generation, ct);
                Record(TaskPhase.Observing, outcome.Message, outcome.Verified, outcome.Succeeded);
                Emit(outcome.Message, outcome.Verified, outcome.Succeeded);
                if (!outcome.Succeeded) throw new InvalidOperationException("Step failed; remaining steps skipped.");
                Record(TaskPhase.Verifying, null, outcome.Verified, outcome.Succeeded);
                Emit(outcome.Verified ? "Independently verified." : "Dispatched only; outcome not independently verified.", outcome.Verified, outcome.Succeeded);
            }
            Record(TaskPhase.Completed, "Plan dispatch finished.");
            Emit("Plan dispatch finished; see per-step observed verification. Unverified actions may not have completed.");
        }
        catch (OperationCanceledException)
        {
            Record(TaskPhase.Cancelled, "Task cancelled.");
            Emit("Task cancelled; in-flight external actions may already have occurred. No automatic replay.");
        }
        catch (Exception)
        {
            Record(TaskPhase.Failed, "Task failed.");
            Emit("Task failed or unavailable; remaining steps skipped. No automatic replay.");
        }
        finally
        {
            monitorEnd.Cancel();
            await monitor;
            try { _journal.RecordTaskCompleted(id, phase, index, totalSteps > 0 ? totalSteps : null, lastVerified, lastDispatched); }
            catch { /* metadata persistence must not hide task outcome */ }
            if (ownsActive && _control is not null)
            {
                try { _control.Release(id); } catch { }
            }
            output.TryComplete();
        }
    }

    private async Task<TaskPlan> PlanWithSingleRepairAsync(
        string goal,
        ModelRoute route,
        bool localProfile,
        long generation,
        Action<string, bool?, bool?> emit,
        CancellationToken ct)
    {
        var completion = await _provider.CompleteAsync(
            BuildPlanningRequest(goal, route, localProfile, repairCategory: null),
            ct).WaitAsync(ct);
        if (completion.Error is not null) throw new InvalidOperationException("Planner provider failed.");
        CheckStop(generation, ct);

        try
        {
            return TaskPlanParser.Parse(completion.Message.Content);
        }
        catch (Exception parseFailure) when (parseFailure is FormatException or JsonException)
        {
            // Exactly one bounded repair attempt, strictly before approval and
            // before any journaled step. Only a coarse, fixed category is echoed
            // back; raw untrusted model output is never re-sent.
            var category = parseFailure is JsonException ? "malformed JSON" : "schema mismatch";
            emit($"Planner response was invalid ({category}). Requesting one bounded JSON repair...", null, null);
            CheckStop(generation, ct);

            var repair = await _provider.CompleteAsync(
                BuildPlanningRequest(goal, route, localProfile, category),
                ct).WaitAsync(ct);
            if (repair.Error is not null) throw new InvalidOperationException("Planner repair provider failed.");
            CheckStop(generation, ct);

            emit("Repair response received; validating.", null, null);
            return TaskPlanParser.Parse(repair.Message.Content);
        }
    }

    private static ChatRequest BuildPlanningRequest(
        string goal,
        ModelRoute route,
        bool localProfile,
        string? repairCategory)
    {
        var systemPrompt = PlanningSystemPrompt;
        if (localProfile)
        {
            systemPrompt += " Local model note: emit compact JSON only, without prose or code fences.";
        }

        if (repairCategory is not null)
        {
            systemPrompt += $" Your previous reply failed validation ({repairCategory}). Return ONLY valid JSON matching the schema. Do not include prose, markdown, or code fences.";
        }

        var maxOutputTokens = Math.Min(
            localProfile ? LocalPlanMaxOutputTokens : DefaultPlanMaxOutputTokens,
            AbsolutePlanMaxOutputTokens);

        return new ChatRequest(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, goal),
            ],
            route.ModelId,
            Temperature: 0,
            MaxOutputTokens: maxOutputTokens,
            RequireJsonObject: true);
    }

    private void CheckStop(long generation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_control is not null && _control.IsStopLatched(generation))
            throw new OperationCanceledException("Emergency stop latched.");
        lock (_sync)
            if (_stopped || generation != _stopGeneration) throw new OperationCanceledException("Emergency stop latched.");
    }

    private static async Task MonitorStopAsync(ITaskControl control, long generation, CancellationTokenSource limit, CancellationToken end)
    {
        while (!end.IsCancellationRequested && !limit.IsCancellationRequested)
        {
            try
            {
                if (control.IsStopLatched(generation)) { limit.Cancel(); break; }
            }
            catch { limit.Cancel(); break; } // corrupt/unavailable state fails closed
            // ACL/path validation is performed by each read; 10 Hz bounds that work
            // while still cancelling in-flight operations promptly.
            try { await Task.Delay(100, end); }
            catch (OperationCanceledException) { break; }
        }
    }
}
