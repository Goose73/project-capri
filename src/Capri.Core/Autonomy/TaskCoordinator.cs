using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Threading.Channels;
using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Core.Autonomy;

public interface ITaskApproval
{
    /// <summary>Returns selected existing disposable workspace for note plans; null means denied.</summary>
    Task<string?> ApproveAsync(TaskPlan plan, CancellationToken cancellationToken);
}

public interface ITaskExecutor
{
    Task<TaskOutcome> ExecuteAsync(TaskStep action, string? workspace, CancellationToken cancellationToken);
}

public interface ITaskJournal
{
    void Record(Guid id, TaskPhase phase, int stepIndex);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Stop cancellation source is replaced on ResetStop and retained for the coordinator lifetime.")]
public sealed class TaskCoordinator
{
    private readonly IChatProvider _provider;
    private readonly IModelRouter _router;
    private readonly ITaskApproval _approval;
    private readonly ITaskExecutor _executor;
    private readonly ITaskJournal _journal;
    private readonly object _sync = new();
    private bool _stopped;
    private long _stopGeneration;
    private CancellationTokenSource _stopCancellation = new();
    public bool IsStopped { get { lock (_sync) return _stopped; } }

    public TaskCoordinator(IChatProvider provider, IModelRouter router, ITaskApproval approval, ITaskExecutor executor, ITaskJournal journal)
    { _provider = provider; _router = router; _approval = approval; _executor = executor; _journal = journal; }

    public void Stop() { lock (_sync) { _stopped = true; _stopGeneration++; _stopCancellation.Cancel(); } }
    public void ResetStop() { lock (_sync) { if (_stopped) { _stopCancellation.Dispose(); _stopCancellation = new CancellationTokenSource(); } _stopped = false; } }

    public async IAsyncEnumerable<TaskProgress> RunAsync(string goal, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<TaskProgress>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var worker = RunCoreAsync(goal, channel.Writer, cancellationToken);
        // Drain the worker's final cancellation status even if the caller cancelled execution.
        await foreach (var update in channel.Reader.ReadAllAsync(CancellationToken.None)) yield return update;
        await worker;
    }

    private async Task RunCoreAsync(string goal, ChannelWriter<TaskProgress> output, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        long generation;
        CancellationToken stopToken;
        lock (_sync) { generation = _stopGeneration; stopToken = _stopCancellation.Token; }
        var phase = TaskPhase.Created;
        var index = 0;
        void Record(TaskPhase next) { phase = next; _journal.Record(id, phase, index); }
        void Emit(string message) => output.TryWrite(new TaskProgress(phase, message));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopToken);
        var activeBudget = Stopwatch.StartNew();
        var activeBudgetLimit = TimeSpan.FromMinutes(2);
        limit.CancelAfter(activeBudgetLimit);
        var ct = limit.Token;
        try
        {
            Record(phase);
            Emit("Task created.");
            CheckStop(generation, ct);
            if (string.IsNullOrWhiteSpace(goal) || goal.Length > 2000) throw new FormatException("Task description must be 1–2000 characters.");
            Record(TaskPhase.Planning);
            Emit("Planning (no actions yet)...");
            var route = await _router.ResolveAsync(new ModelRouteRequest(RequiresStreaming: false), ct);
            var request = new ChatRequest([
                new ChatMessage(ChatRole.System, "Return ONLY one JSON object: {\"steps\":[{\"tool\":\"create_note\",\"target\":\"name.txt\",\"text\":\"literal text\"}]}. 1 to 8 steps. Allowed tools: launch_app (target: notepad/calculator/explorer/paint), open_url (HTTPS URL), brave_search (query), create_note (flat .txt filename and literal text), open_note (filename created earlier). No markdown, extra fields or commands. If request cannot be expressed with these tools, return {\"steps\":[]}. Never invent an existing file or assert an action succeeded."),
                new ChatMessage(ChatRole.User, goal)
            ], route.ModelId, Temperature: 0, MaxOutputTokens: 1800);
            var completion = await _provider.CompleteAsync(request, ct);
            if (completion.Error is not null) throw new InvalidOperationException("Planner provider failed.");
            var plan = TaskPlanParser.Parse(completion.Message.Content);
            CheckStop(generation, ct);
            Record(TaskPhase.AwaitingPermission);
            Emit("Waiting for approval of exact plan and targets...");
            // Human review has its own bounded grace period; it does not consume the
            // planning/execution budget, but caller cancellation and emergency stop still cancel it.
            activeBudget.Stop();
            var remainingBudget = activeBudgetLimit - activeBudget.Elapsed;
            limit.CancelAfter(Timeout.InfiniteTimeSpan);
            using var approvalLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopToken);
            approvalLimit.CancelAfter(TimeSpan.FromMinutes(5));
            var workspace = await _approval.ApproveAsync(plan, approvalLimit.Token);
            CheckStop(generation, ct);
            if (workspace is null) { Record(TaskPhase.Cancelled); Emit("Task denied; no actions dispatched."); return; }
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
                Record(TaskPhase.Observing);
                Emit(outcome.Message);
                if (!outcome.Succeeded) throw new InvalidOperationException("Step failed; remaining steps skipped.");
                Record(TaskPhase.Verifying);
                Emit(outcome.Verified ? "Independently verified." : "Dispatched only; outcome not independently verified.");
            }
            Record(TaskPhase.Completed);
            Emit("Plan dispatch finished; see per-step observed verification. Unverified actions may not have completed.");
        }
        catch (OperationCanceledException)
        {
            Record(TaskPhase.Cancelled);
            Emit("Task cancelled; in-flight external actions may already have occurred. No automatic replay.");
        }
        catch (Exception) 
        {
            Record(TaskPhase.Failed);
            Emit("Task failed or unavailable; remaining steps skipped. No automatic replay.");
        }
        finally { output.TryComplete(); }
    }

    private void CheckStop(long generation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
            if (_stopped || generation != _stopGeneration) throw new OperationCanceledException("Emergency stop latched.");
    }
}
