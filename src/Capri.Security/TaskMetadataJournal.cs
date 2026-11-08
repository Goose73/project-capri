using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;

namespace Capri.Security;

/// <summary>Metadata only: no prompt, plan, target, text, or credentials; never read for replay.</summary>
public sealed class TaskMetadataJournal : ITaskJournal
{
    private readonly ICapriPaths _paths;
    private readonly ITaskHistoryStore? _history;
    // Multiple coordinators may each resolve a journal instance for the same data root.
    private static readonly object Sync = new();

    public TaskMetadataJournal(ICapriPaths paths) : this(paths, history: null) { }

    public TaskMetadataJournal(ICapriPaths paths, ITaskHistoryStore? history)
    {
        _paths = paths;
        _history = history;
    }

    public void Record(Guid id, TaskPhase phase, int stepIndex)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(_paths.Root);
            // One bounded latest-state slot; deliberately no plan/actions to replay.
            var path = Path.Combine(_paths.Root, "task-latest.json");
            var temporaryPath = Path.Combine(_paths.Root, $"task-latest.{Guid.NewGuid():N}.tmp");
            var data = JsonSerializer.Serialize(new { id, phase = phase.ToString(), stepIndex, updatedUtc = DateTimeOffset.UtcNow });

            try
            {
                File.WriteAllText(temporaryPath, data);
                // Same-directory move replaces the completed snapshot, never a partially written one.
                // Readers (or Windows indexing) can briefly hold the old snapshot open.
                // Retry only publication of metadata, never an external task action.
                for (var attempt = 0; ; attempt++)
                {
                    try { File.Move(temporaryPath, path, overwrite: true); break; }
                    catch (IOException) when (attempt < 5) { Thread.Sleep(20); }
                    catch (UnauthorizedAccessException) when (attempt < 5) { Thread.Sleep(20); }
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        TryRecordHistory(id, phase, stepIndex, totalSteps: null, providerId: null, modelId: null, verified: null, dispatched: null, finalCategory: null);
    }

    public void RecordTaskStarted(Guid id, string? providerId, string? modelId)
    {
        TryRecordHistory(id, TaskPhase.Created, 0, totalSteps: null, providerId, modelId, verified: null, dispatched: null, finalCategory: null, created: DateTimeOffset.UtcNow);
    }

    public void RecordTaskPhase(
        Guid id,
        TaskPhase phase,
        int stepIndex,
        int? totalSteps = null,
        string? message = null,
        bool? verified = null,
        bool? dispatched = null)
    {
        Record(id, phase, stepIndex);
        TryRecordHistory(id, phase, stepIndex, totalSteps, null, null, verified, dispatched, message);
    }

    public void RecordTaskCompleted(
        Guid id,
        TaskPhase finalPhase,
        int stepIndex,
        int? totalSteps = null,
        bool? lastVerified = null,
        bool? lastDispatched = null)
    {
        Record(id, finalPhase, stepIndex);
        TryRecordHistory(id, finalPhase, stepIndex, totalSteps, null, null, lastVerified, lastDispatched, finalPhase.ToString());
    }

    private void TryRecordHistory(
        Guid id,
        TaskPhase phase,
        int stepIndex,
        int? totalSteps,
        string? providerId,
        string? modelId,
        bool? verified,
        bool? dispatched,
        string? finalCategory,
        DateTimeOffset? created = null)
    {
        if (_history is null) return;
        try
        {
            var existing = _history.Find(id);
            _history.Record(new TaskHistoryEntry(
                id,
                existing?.CreatedUtc ?? created ?? DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                phase.ToString(),
                stepIndex,
                totalSteps ?? existing?.TotalSteps,
                providerId ?? existing?.ProviderId,
                modelId ?? existing?.ModelId,
                verified ?? existing?.LastStepVerified,
                dispatched ?? existing?.LastStepDispatched,
                finalCategory ?? existing?.FinalCategory));
        }
        catch
        {
            // History is auxiliary metadata; never fail the task or latest snapshot.
        }
    }
}
