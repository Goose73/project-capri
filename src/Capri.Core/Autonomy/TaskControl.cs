using System.Text.Json.Serialization;

namespace Capri.Core.Autonomy;

/// <summary>Active task ownership claim for cross-process coordination (metadata only).</summary>
public sealed record TaskOwnership(
    Guid TaskId,
    int ProcessId,
    DateTimeOffset StartedUtc,
    [property: JsonPropertyName("host")] string Host);

/// <summary>Shared emergency-stop state (metadata only).</summary>
public sealed record TaskStopState(
    bool Stopped,
    long Generation,
    DateTimeOffset UpdatedUtc,
    string? Reason = null);

/// <summary>
/// File-backed cross-process coordination for task stop/ownership.
/// Implementations must use atomic writes, bounded JSON, and store no secrets or prompts.
/// </summary>
public interface ITaskControl
{
    TaskStopState GetStopState();
    TaskStopState RequestStop(string? reason = null);
    TaskStopState ResetStop();
    TaskOwnership? GetActive();
    /// <summary>Atomically acquires single-active ownership. Returns false if another live task owns it.</summary>
    bool TryAcquire(TaskOwnership ownership);
    void Release(Guid taskId);
    /// <summary>Returns true when shared stop/generation requires cancellation of a task started at <paramref name="startGeneration"/>.</summary>
    bool IsStopLatched(long startGeneration);
}

/// <summary>Per-task metadata history entry. No prompts, plans, targets, note text, or credentials.</summary>
public sealed record TaskHistoryEntry(
    Guid TaskId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string Phase,
    int StepIndex,
    int? TotalSteps,
    string? ProviderId,
    string? ModelId,
    bool? LastStepVerified,
    bool? LastStepDispatched,
    string? FinalCategory);

public interface ITaskHistoryStore
{
    void Record(TaskHistoryEntry entry);
    IReadOnlyList<TaskHistoryEntry> GetRecent(int maxCount = 20);
    TaskHistoryEntry? Find(Guid taskId);
}

/// <summary>Context shown to approval UI/CLI; never accepted as approval itself.</summary>
public sealed record TaskApprovalContext(
    string Goal,
    string? ProviderId,
    string? ModelId);
