using System.IO;
using System.Text.Json;
using Capri.Core.Autonomy;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Autonomy;

public sealed class TaskMetadataJournalTests
{
    private static readonly string[] ExpectedFields = ["id", "phase", "stepIndex", "updatedUtc"];

    [Fact]
    public async Task ConcurrentWritersPublishOnlyTheExactMetadataSnapshot()
    {
        using var workspace = new TempDirectory();
        var paths = new CapriPaths(workspace.Path);
        var journals = Enumerable.Range(0, 4).Select(_ => new TaskMetadataJournal(paths)).ToArray();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
            journals[i % journals.Length].Record(Guid.NewGuid(), (TaskPhase)(i % 9), i))));

        var path = Path.Combine(workspace.Path, "task-latest.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var root = document.RootElement;
        var names = root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(ExpectedFields, names);
        Assert.True(Guid.TryParse(root.GetProperty("id").GetString(), out _));
        Assert.Contains(root.GetProperty("phase").GetString(), Enum.GetNames<TaskPhase>());
        Assert.InRange(root.GetProperty("stepIndex").GetInt32(), 0, 199);
        Assert.True(root.GetProperty("updatedUtc").TryGetDateTimeOffset(out _));
        Assert.Empty(Directory.GetFiles(workspace.Path, "*.tmp"));
        Assert.Single(Directory.GetFiles(workspace.Path));
    }
}
