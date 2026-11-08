using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Capri.Core.Autonomy;
using Capri.Security;
using Capri.Security.Autonomy;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Autonomy;

public sealed class TaskControlTests
{
    [Fact]
    public void StopAndResetUseGenerationSafely()
    {
        using var temp = new TempDirectory();
        var control = new FileTaskControl(new CapriPaths(temp.Path));
        Assert.False(control.GetStopState().Stopped);
        var g0 = control.GetStopState().Generation;

        var stopped = control.RequestStop("test");
        Assert.True(stopped.Stopped);
        Assert.True(stopped.Generation > g0);
        Assert.True(control.IsStopLatched(g0));
        Assert.True(control.IsStopLatched(stopped.Generation)); // latch is active while stopped

        var reset = control.ResetStop();
        Assert.False(reset.Stopped);
        Assert.Equal(stopped.Generation, reset.Generation); // never revive: old generation remains latched
        Assert.True(control.IsStopLatched(g0)); // tasks that started before stop remain latched
        Assert.False(control.IsStopLatched(reset.Generation)); // future tasks (current generation) can run after reset
    }

    [Fact]
    public void SingleActiveOwnershipRejectsConflictsAndReleases()
    {
        using var temp = new TempDirectory();
        var control = new FileTaskControl(new CapriPaths(temp.Path));
        var a = Claim();
        var b = Claim();

        Assert.True(control.TryAcquire(a));
        Assert.False(control.TryAcquire(b));
        Assert.Equal(a.TaskId, control.GetActive()?.TaskId);

        control.Release(a.TaskId);
        Assert.Null(control.GetActive());
        Assert.True(control.TryAcquire(b));
    }

    [Fact]
    public void ActiveOwnershipIsReadableAcrossInstances()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        var c1 = new FileTaskControl(paths);
        var c2 = new FileTaskControl(paths);
        var claim = Claim();

        Assert.True(c1.TryAcquire(claim));
        Assert.Equal(claim.TaskId, c2.GetActive()?.TaskId);
        c2.Release(claim.TaskId);
        Assert.Null(c1.GetActive());
    }

    [Fact]
    public void CrossProcessStyleConflictUsesSharedFiles()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        var writer = new FileTaskControl(paths);
        var reader = new FileTaskControl(paths);
        var claim = Claim();
        Assert.True(writer.TryAcquire(claim));
        var other = Claim();
        Assert.False(reader.TryAcquire(other));
        writer.Release(claim.TaskId);
        Assert.True(reader.TryAcquire(other));
    }

    [Fact]
    public void StopFromAnotherControlInstanceLatchesSharedState()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        var c1 = new FileTaskControl(paths);
        var c2 = new FileTaskControl(paths);
        var start = c1.GetStopState().Generation;
        c2.RequestStop("cli");
        Assert.True(c1.IsStopLatched(start));
        c2.ResetStop();
        Assert.False(c1.IsStopLatched(c2.GetStopState().Generation));
        Assert.True(c1.IsStopLatched(start));
    }

    [Fact]
    public async Task HistoryStoreIsMetadataOnlyAndBounded()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        var store = new FileTaskHistoryStore(paths);
        var id = Guid.NewGuid();
        store.Record(new TaskHistoryEntry(id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed", 1, 2, "ollama", "qwen3.5:4b", true, true, "ok"));

        var tasks = new DirectoryInfo(Path.Combine(temp.Path, "tasks"));
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var tasksSecurity = tasks.GetAccessControl();
            Assert.Equal(identity.User, tasksSecurity.GetOwner(typeof(SecurityIdentifier)));
            Assert.True(tasksSecurity.AreAccessRulesProtected);
            var historySecurity = new DirectoryInfo(Path.Combine(tasks.FullName, "history")).GetAccessControl();
            Assert.Equal(identity.User, historySecurity.GetOwner(typeof(SecurityIdentifier)));
            Assert.True(historySecurity.AreAccessRulesProtected);
        }

        // History may initialize shared task storage before task control is used.
        new FileTaskControl(paths).RequestStop("after history initialization");

        var entry = store.Find(id);
        Assert.NotNull(entry);
        Assert.Equal("Completed", entry!.Phase);
        var recent = store.GetRecent();
        Assert.Contains(recent, e => e.TaskId == id);

        var historyDir = Path.Combine(temp.Path, "tasks", "history");
        foreach (var file in Directory.GetFiles(historyDir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(file);
            Assert.DoesNotContain("note text", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"target\"", json, StringComparison.OrdinalIgnoreCase);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array);
        }
    }

    [Fact]
    public void ControlFilesAreAtomicJsonMetadata()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        var control = new FileTaskControl(paths);
        control.RequestStop("x");
        control.ResetStop();
        control.TryAcquire(Claim());

        var tasksDir = Path.Combine(temp.Path, "tasks");
        Assert.True(File.Exists(Path.Combine(tasksDir, "stop.json")));
        Assert.True(File.Exists(Path.Combine(tasksDir, "active.json")));
        Assert.Empty(Directory.GetFiles(tasksDir, "*.tmp"));
        var stopJson = File.ReadAllText(Path.Combine(tasksDir, "stop.json"));
        Assert.DoesNotContain("password", stopJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", stopJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewlyCreatedTasksDirectoryHasCurrentUserOwnerAndProtectedAcl()
    {
        using var temp = new TempDirectory();
        var control = new FileTaskControl(new CapriPaths(temp.Path));
        control.RequestStop("initialize");

        var tasks = new DirectoryInfo(Path.Combine(temp.Path, "tasks"));
        var security = tasks.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);
        Assert.All(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(), rule =>
        {
            if (rule.AccessControlType == AccessControlType.Allow &&
                !identity.User!.Equals(rule.IdentityReference) &&
                !rule.IdentityReference.Value.Equals("S-1-5-18", StringComparison.Ordinal) &&
                !rule.IdentityReference.Value.Equals("S-1-5-32-544", StringComparison.Ordinal))
                Assert.Equal((FileSystemRights)0, rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.FullControl | FileSystemRights.Delete));
        });
    }

    private static TaskOwnership Claim() => new(Guid.NewGuid(), Environment.ProcessId,
        System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), Environment.MachineName);

    [Fact]
    public void LiveOwnerDoesNotExpireAndCorruptionFailsClosed()
    {
        using var temp = new TempDirectory();
        var control = new FileTaskControl(new CapriPaths(temp.Path), TimeSpan.FromMilliseconds(1));
        var claim = Claim();
        Assert.True(control.TryAcquire(claim));
        Thread.Sleep(25);
        Assert.Equal(claim.TaskId, control.GetActive()?.TaskId);
        control.Release(claim.TaskId);
        var active = Path.Combine(temp.Path, "tasks", "active.json");
        File.WriteAllText(active, "broken");
        Assert.Throws<InvalidDataException>(() => control.TryAcquire(Claim()));
        Assert.True(File.Exists(active));
        File.WriteAllText(Path.Combine(temp.Path, "tasks", "stop.json"), "broken");
        Assert.Throws<InvalidDataException>(() => control.ResetStop());
        Assert.Throws<InvalidDataException>(() => control.RequestStop());
    }
}
