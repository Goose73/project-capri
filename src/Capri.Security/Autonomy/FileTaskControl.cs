using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;

namespace Capri.Security.Autonomy;

/// <summary>
/// Per-user file-based task coordination under <c>ICapriPaths.Root/tasks</c>.
/// Atomic writes, bounded metadata JSON, no secrets/prompts/plan contents.
/// Cooperative coordination among processes running as the same signed-in user;
/// the user (or an administrator) can replace files between pathname checks and opens.
/// This is not a security boundary against malicious code running as that user.
/// </summary>
public sealed class FileTaskControl : ITaskControl
{
    private const int MaxJsonBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
    private readonly ICapriPaths _paths;
    private readonly TimeSpan _staleAfter; // retained for source compatibility; a live process never expires by age.

    public FileTaskControl(ICapriPaths paths, TimeSpan? staleAfter = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _staleAfter = staleAfter ?? TimeSpan.FromMinutes(30);
    }

    private string TasksDir => Path.Combine(_paths.Root, "tasks");
    private string StopPath => Path.Combine(TasksDir, "stop.json");
    private string ActivePath => Path.Combine(TasksDir, "active.json");
    private string LockPath => Path.Combine(TasksDir, "coordination.lock");

    public TaskStopState GetStopState()
    {
        if (Directory.Exists(_paths.Root)) ValidateDirectory(_paths.Root);
        if (Directory.Exists(TasksDir)) ValidateDirectory(TasksDir);
        return ReadJson<TaskStopState>(StopPath) ?? new TaskStopState(false, 0, DateTimeOffset.UtcNow);
    }

    public TaskStopState RequestStop(string? reason = null)
    {
        using (AcquireLock())
        {
            var current = GetStopState();
            var next = new TaskStopState(true, current.Generation + 1, DateTimeOffset.UtcNow, SanitizeReason(reason));
            WriteJsonAtomic(StopPath, next);
            return next;
        }
    }

    public TaskStopState ResetStop()
    {
        using (AcquireLock())
        {
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
        using var guard = AcquireLock();
        return GetActiveLocked();
    }

    private TaskOwnership? GetActiveLocked()
    {
        var active = ReadJson<TaskOwnership>(ActivePath);
        if (active is null) return null;
        if (IsStale(active))
        {
            // Best-effort stale cleanup; do not throw.
            File.Delete(ActivePath);
            return null;
        }

        return active;
    }

    public bool TryAcquire(TaskOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (ownership.TaskId == Guid.Empty || ownership.ProcessId != Environment.ProcessId ||
            !string.Equals(ownership.Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
            Math.Abs((Process.GetCurrentProcess().StartTime.ToUniversalTime() - ownership.StartedUtc.UtcDateTime).TotalSeconds) > 2)
            throw new ArgumentException("Ownership must identify the current process and its actual start time.", nameof(ownership));
        using (AcquireLock())
        {
            if (GetStopState().Stopped) return false;
            var current = GetActiveLocked();
            if (current is not null)
                return false;
            WriteJsonAtomic(ActivePath, ownership);
            return true;
        }
    }

    public void Release(Guid taskId)
    {
        using (AcquireLock())
        {
            TryDeleteIfMatches(taskId);
        }
    }

    private void TryDeleteIfMatches(Guid taskId)
    {
        if (!ValidatePath(ActivePath)) return;
        try
        {
            var current = ReadJson<TaskOwnership>(ActivePath);
            if (current?.TaskId == taskId && current.ProcessId == Environment.ProcessId && !IsStale(current))
                File.Delete(ActivePath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private bool IsStale(TaskOwnership ownership)
    {
        _ = _staleAfter;
        if (ownership.TaskId == Guid.Empty || ownership.ProcessId <= 0 || ownership.StartedUtc == default ||
            !string.Equals(ownership.Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return false; // unknown/corrupt identity must not be removed automatically
        try
        {
            using var process = Process.GetProcessById(ownership.ProcessId);
            if (process.HasExited) return true;
            try
            {
                var started = process.StartTime.ToUniversalTime();
                // Persist the real OS start time, not task start time. PID reuse is identifiable
                // only if the OS supplies a start time; otherwise conservatively keep the claim.
                if (Math.Abs((started - ownership.StartedUtc.UtcDateTime).TotalSeconds) > 2) return true;
            }
            catch { /* access/start time may be unavailable */ }
            return false;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (Exception) { return false; }
    }

    private static string? SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var s = new string(reason.Where(c => !char.IsControl(c)).Take(80).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        if (!ValidatePath(path)) return null;
        try
        {
            // Open once and parse only bytes from that handle. An atomic replacement
            // cannot swap the bytes between separate length/read pathname operations.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length <= 0 || file.Length > MaxJsonBytes) throw new InvalidDataException("Invalid task metadata size.");
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                (typeof(T) == typeof(TaskStopState) &&
                    (!root.TryGetProperty("stopped", out var stopped) || stopped.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                     !root.TryGetProperty("generation", out var generation) || generation.ValueKind != JsonValueKind.Number ||
                     !root.TryGetProperty("updatedUtc", out var updated) || updated.ValueKind != JsonValueKind.String)) ||
                (typeof(T) == typeof(TaskOwnership) &&
                    (!root.TryGetProperty("taskId", out var id) || id.ValueKind != JsonValueKind.String ||
                     !root.TryGetProperty("processId", out var pid) || pid.ValueKind != JsonValueKind.Number ||
                     !root.TryGetProperty("startedUtc", out var started) || started.ValueKind != JsonValueKind.String ||
                     !root.TryGetProperty("host", out var host) || host.ValueKind != JsonValueKind.String)))
                throw new InvalidDataException("Incomplete task metadata; manual recovery required.");
            var value = JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidDataException("Null task metadata.");
            if (value is TaskStopState stop && (stop.Generation < 0 || stop.UpdatedUtc == default)) throw new InvalidDataException("Invalid stop state.");
            if (value is TaskOwnership owner && (owner.TaskId == Guid.Empty || owner.ProcessId <= 0 || owner.StartedUtc == default || string.IsNullOrWhiteSpace(owner.Host))) throw new InvalidDataException("Invalid owner state.");
            return value;
        }
        catch (JsonException ex) { throw new InvalidDataException("Corrupt task metadata; manual recovery required.", ex); }
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        ValidatePath(path);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(value, JsonOptions);
        if (json.Length > MaxJsonBytes) throw new InvalidOperationException("Task metadata too large.");
        var temp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file)) writer.Write(json);
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

    private FileStream AcquireLock()
    {
        // Root must be private to the signed-in user. Never follow a junction/symlink
        // into an attacker-controlled coordination directory or metadata file.
        Directory.CreateDirectory(_paths.Root);
        ValidateDirectory(_paths.Root);
        Directory.CreateDirectory(TasksDir);
        ValidateDirectory(TasksDir);
        ValidatePath(LockPath);
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (deadline.Elapsed < TimeSpan.FromSeconds(10)) { Thread.Sleep(10); }
        }
    }

    private static void ValidateDirectory(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null)
        {
            if (new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Task coordination refuses reparse paths.");
            current = Directory.GetParent(current)?.FullName;
        }
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new UnauthorizedAccessException("Current user identity unavailable.");
        var security = new DirectoryInfo(path).GetAccessControl();
        if (!sid.Equals(security.GetOwner(typeof(SecurityIdentifier))))
            throw new UnauthorizedAccessException("Task coordination directory is not owned by the current user.");
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow &&
                !sid.Equals(rule.IdentityReference) && !rule.IdentityReference.Value.Equals("S-1-5-18", StringComparison.Ordinal) &&
                !rule.IdentityReference.Value.Equals("S-1-5-32-544", StringComparison.Ordinal) &&
                (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.FullControl | FileSystemRights.Delete)) != 0)
                throw new UnauthorizedAccessException("Task coordination directory is writable by another principal.");
        }
    }

    private static bool ValidatePath(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if (attributes.HasFlag(FileAttributes.ReparsePoint) || attributes.HasFlag(FileAttributes.Directory))
            throw new InvalidOperationException("Task coordination refuses reparse files or directories in metadata paths.");
        return true;
    }
}
