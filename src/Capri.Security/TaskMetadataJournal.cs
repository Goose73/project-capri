using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;

namespace Capri.Security;

/// <summary>Metadata only: no prompt, plan, target, text, or credentials; never read for replay.</summary>
public sealed class TaskMetadataJournal : ITaskJournal
{
    private readonly ICapriPaths _paths;
    // Multiple coordinators may each resolve a journal instance for the same data root.
    private static readonly object Sync = new();

    public TaskMetadataJournal(ICapriPaths paths) => _paths = paths;

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
    }
}
