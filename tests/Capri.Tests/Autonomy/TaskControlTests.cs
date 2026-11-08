using System.IO;
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
        var a = new TaskOwnership(Guid.NewGuid(), Environment.ProcessId, DateTimeOffset.UtcNow, Environment.MachineName);
        var b = new TaskOwnership(Guid.NewGuid(), Environment.ProcessId, DateTimeOffset.UtcNow, Environment.MachineName);

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
        var claim = new TaskOwnership(Guid.NewGuid(), Environment.ProcessId, DateTimeOffset.UtcNow, Environment.MachineName);

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
        var claim = new TaskOwnership(Guid.NewGuid(), Environment.ProcessId, DateTimeOffset.UtcNow, Environment.MachineName);
        Assert.True(writer.TryAcquire(claim));
        var other = new TaskOwnership(Guid.NewGuid(), Environment.ProcessId, DateTimeOffset.UtcNow, Environment.MachineName);
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
        var store = new FileTaskHistoryStore(new CapriPaths(temp.Path));
        var id = Guid.NewGuid();
        store.Record(new TaskHistoryEntry(id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed", 1, 2, "ollama", "qwen3.5:4b", true, true, "ok"));

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
        control.TryAcquire(new TaskOwnership(Guid.NewGuid(), Environment.ProcessId, DateTimeOffset.UtcNow, Environment.MachineName));

        var tasksDir = Path.Combine(temp.Path, "tasks");
        Assert.True(File.Exists(Path.Combine(tasksDir, "stop.json")));
        Assert.True(File.Exists(Path.Combine(tasksDir, "active.json")));
        Assert.Empty(Directory.GetFiles(tasksDir, "*.tmp"));
        var stopJson = File.ReadAllText(Path.Combine(tasksDir, "stop.json"));
        Assert.DoesNotContain("password", stopJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", stopJson, StringComparison.OrdinalIgnoreCase);
    }
}
