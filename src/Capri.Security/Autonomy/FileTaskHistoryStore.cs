using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;

namespace Capri.Security.Autonomy;

/// <summary>
/// Bounded per-user task history metadata under <c>ICapriPaths.Root/tasks/history</c>.
/// Stores no prompts, plans, targets, note text, or credentials.
/// </summary>
public sealed class FileTaskHistoryStore : ITaskHistoryStore
{
    private const int MaxJsonBytes = 16 * 1024;
    private const int MaxEntriesRetained = 50;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static readonly object Sync = new();
    private readonly ICapriPaths _paths;

    public FileTaskHistoryStore(ICapriPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private string HistoryDir => Path.Combine(_paths.Root, "tasks", "history");
    private string IndexPath => Path.Combine(HistoryDir, "index.json");

    public void Record(TaskHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (Sync)
        {
            TaskDirectorySecurity.EnsurePrivateDirectory(_paths.Root);
            TaskDirectorySecurity.EnsurePrivateDirectory(Path.Combine(_paths.Root, "tasks"));
            TaskDirectorySecurity.EnsurePrivateDirectory(HistoryDir);
            var sanitized = Sanitize(entry);
            WriteJsonAtomic(Path.Combine(HistoryDir, sanitized.TaskId.ToString("N") + ".json"), sanitized);
            UpdateIndex(sanitized);
        }
    }

    public IReadOnlyList<TaskHistoryEntry> GetRecent(int maxCount = 20)
    {
        maxCount = Math.Clamp(maxCount, 1, MaxEntriesRetained);
        lock (Sync)
        {
            var ids = ReadJson<List<Guid>>(IndexPath) ?? [];
            var results = new List<TaskHistoryEntry>(Math.Min(ids.Count, maxCount));
            foreach (var id in ids.Take(maxCount))
            {
                var entry = Find(id);
                if (entry is not null) results.Add(entry);
            }
            return results;
        }
    }

    public TaskHistoryEntry? Find(Guid taskId)
    {
        lock (Sync)
        {
            return ReadJson<TaskHistoryEntry>(Path.Combine(HistoryDir, taskId.ToString("N") + ".json"));
        }
    }

    private void UpdateIndex(TaskHistoryEntry entry)
    {
        var ids = ReadJson<List<Guid>>(IndexPath) ?? [];
        ids.Remove(entry.TaskId);
        ids.Insert(0, entry.TaskId);
        if (ids.Count > MaxEntriesRetained) ids = ids.Take(MaxEntriesRetained).ToList();
        WriteJsonAtomic(IndexPath, ids);
        // Best-effort prune old files not in index.
        try
        {
            var keep = ids.Select(i => i.ToString("N") + ".json").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(HistoryDir, "*.json"))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, "index.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (!keep.Contains(name))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    private static TaskHistoryEntry Sanitize(TaskHistoryEntry entry)
    {
        return entry with
        {
            Phase = San(entry.Phase, 40) ?? "Unknown",
            ProviderId = San(entry.ProviderId, 60),
            ModelId = San(entry.ModelId, 120),
            FinalCategory = San(entry.FinalCategory, 120),
        };
    }

    private static string? San(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = new string(value.Where(c => !char.IsControl(c)).Take(max).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxJsonBytes) return null;
            var json = File.ReadAllText(path);
            if (json.Length > MaxJsonBytes) return null;
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch { return null; }
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path)!;
        TaskDirectorySecurity.EnsurePrivateDirectory(dir);
        var json = JsonSerializer.Serialize(value, JsonOptions);
        if (json.Length > MaxJsonBytes) throw new InvalidOperationException("Task metadata too large.");
        var temp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, json);
            for (var attempt = 0; ; attempt++)
            {
                try { File.Move(temp, path, overwrite: true); break; }
                catch (IOException) when (attempt < 5) { Thread.Sleep(15); }
                catch (UnauthorizedAccessException) when (attempt < 5) { Thread.Sleep(15); }
            }
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
