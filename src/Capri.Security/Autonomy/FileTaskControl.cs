using System.Diagnostics;
using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;

namespace Capri.Security.Autonomy;

/// <summary>
/// Per-user file-based task coordination under <c>ICapriPaths.Root/tasks</c>.
/// Atomic writes, bounded metadata JSON, no secrets/prompts/plan contents.
/// </summary>
public sealed class FileTaskControl : ITaskControl
{
    private const int MaxJsonBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
    private readonly ICapriPaths _paths;
    private readonly TimeSpan _staleAfter;
    private static readonly object ProcessSync = new();

    public FileTaskControl(ICapriPaths paths, TimeSpan? staleAfter = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _staleAfter = staleAfter ?? TimeSpan.FromMinutes(30);
    }

    private string TasksDir => Path.Combine(_paths.Root, "tasks");
    private string StopPath => Path.Combine(TasksDir, "stop.json");
    private string ActivePath => Path.Combine(TasksDir, "active.json");

    public TaskStopState GetStopState() => ReadJson<TaskStopState>(StopPath) ?? new TaskStopState(false, 0, DateTimeOffset.UtcNow);

    public TaskStopState RequestStop(string? reason = null)
    {
        lock (ProcessSync)
        {
            Directory.CreateDirectory(TasksDir);
            var current = GetStopState();
            var next = new TaskStopState(true, current.Generation + 1, DateTimeOffset.UtcNow, SanitizeReason(reason));
            WriteJsonAtomic(StopPath, next);
            return next;
        }
    }

    public TaskStopState ResetStop()
    {
        lock (ProcessSync)
        {
            Directory.CreateDirectory(TasksDir);
            var current = GetStopState();
            // Reset clears the latch but preserves generation semantics so tasks that observed
            // the previous stop generation remain cancelled; new tasks capture current generation.
            var next = new TaskStopState(false, current.Generation, DateTimeOffset.UtcNow, "reset");
            WriteJsonAtomic(StopPath, next);
            return next;
        }
    }

    public bool IsStopLatched(long startGeneration)
    {
        var state = GetStopState();
        return state.Stopped || state.Generation != startGeneration;
    }

    public TaskOwnership? GetActive()
    {
        var active = ReadJson<TaskOwnership>(ActivePath);
        if (active is null) return null;
        if (IsStale(active))
        {
            // Best-effort stale cleanup; do not throw.
            TryDeleteIfMatches(active.TaskId);
            return null;
        }

        return active;
    }

    public bool TryAcquire(TaskOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        lock (ProcessSync)
        {
            Directory.CreateDirectory(TasksDir);
            var current = GetActive();
            if (current is not null && current.TaskId != ownership.TaskId)
                return false;
            WriteJsonAtomic(ActivePath, ownership);
            return true;
        }
    }

    public void Release(Guid taskId)
    {
        lock (ProcessSync)
        {
            TryDeleteIfMatches(taskId);
        }
    }

    private void TryDeleteIfMatches(Guid taskId)
    {
        if (!File.Exists(ActivePath)) return;
        try
        {
            var current = ReadJson<TaskOwnership>(ActivePath);
            if (current is null || current.TaskId == taskId)
                File.Delete(ActivePath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private bool IsStale(TaskOwnership ownership)
    {
        if (DateTimeOffset.UtcNow - ownership.StartedUtc > _staleAfter) return true;
        try
        {
            if (ownership.ProcessId == Environment.ProcessId) return false;
            using var process = Process.GetProcessById(ownership.ProcessId);
            if (process.HasExited) return true;
            // If start time is wildly off, treat as stale/reused PID.
            try
            {
                var started = process.StartTime.ToUniversalTime();
                var delta = Math.Abs((started - ownership.StartedUtc.UtcDateTime).TotalMinutes);
                if (delta > 10) return true;
            }
            catch { /* access/start time may be unavailable */ }
            return false;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (Exception) { return DateTimeOffset.UtcNow - ownership.StartedUtc > TimeSpan.FromMinutes(5); }
    }

    private static string? SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var s = new string(reason.Where(c => !char.IsControl(c)).Take(80).ToArray()).Trim();
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
        Directory.CreateDirectory(dir);
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
