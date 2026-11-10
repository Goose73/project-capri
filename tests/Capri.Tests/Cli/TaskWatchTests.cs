using System.Diagnostics;
using Capri.Cli;
using Capri.Cli.Commands;
using Capri.Core.Autonomy;
using Capri.Security;
using Capri.Security.Autonomy;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class TaskWatchTests
{
    [Fact]
    public void ParserAcceptsWatchWithIdAndJson()
    {
        var id = Guid.NewGuid();
        var invocation = CliParser.Parse(["task", "watch", id.ToString(), "--json"]);
        Assert.True(invocation.IsValid);
        Assert.Equal(CliCommandKind.Task, invocation.Kind);
        Assert.Collection(invocation.Arguments,
            argument => Assert.Equal("watch", argument),
            argument => Assert.Equal(id.ToString(), argument));
        Assert.True(invocation.Json);
    }

    [Theory]
    [InlineData("watch")]
    [InlineData("watch invalid-guid")]
    [InlineData("watch 00000000-0000-0000-0000-000000000001 unexpected")]
    public void ParserRejectsMalformedWatchUsage(string command)
    {
        var input = new[] { "task" }.Concat(command.Split(' ')).ToArray();
        var invocation = CliParser.Parse(input);
        // Missing ID and surplus arguments must be rejected at parse time.
        // The ID's GUID format is validated by TaskCommand.
        if (command.EndsWith("invalid-guid", StringComparison.Ordinal))
        {
            Assert.True(invocation.IsValid);
        }
        else
        {
            Assert.False(invocation.IsValid);
        }
    }

    [Fact]
    public async Task CompletedTaskEmitsOneReadOnlyJsonSnapshotAndExits()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        var writer = new FileTaskHistoryStore(paths);
        var id = Guid.NewGuid();
        writer.Record(new TaskHistoryEntry(
            id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed",
            2, 2, "ollama", "qwen3.5:4b", true, true, "Completed"));

        var services = StubCliServices.CreateInTemp(temp.Path);
        services.TaskControl = new FileTaskControl(paths);
        services.TaskHistory = new FileTaskHistoryStore(paths);
        var console = new FakeCliConsole(interactive: false);
        var code = await new TaskCommand(console, services).ExecuteAsync(new CliInvocation
        {
            Kind = CliCommandKind.Task,
            Arguments = ["watch", id.ToString()],
            Json = true
        });

        Assert.Equal(CliExitCodes.Success, code);
        Assert.Single(console.Out.TrimEnd('\r', '\n').Split('\n'));
        using var document = System.Text.Json.JsonDocument.Parse(console.Out);
        var root = document.RootElement;
        Assert.Equal(id.ToString(), root.GetProperty("taskId").GetString());
        Assert.False(root.GetProperty("active").GetBoolean());
        Assert.Equal("Completed", root.GetProperty("metadata").GetProperty("phase").GetString());
        Assert.True(root.GetProperty("metadata").GetProperty("lastStepVerified").GetBoolean());
        Assert.DoesNotContain("\"target\"", console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"text\"", console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", console.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownTaskIsReportedWithoutNetworkOrActions()
    {
        using var temp = new TempDirectory();
        var services = StubCliServices.CreateInTemp(temp.Path);
        services.TaskControl = new FileTaskControl(new CapriPaths(temp.Path));
        services.TaskHistory = new FileTaskHistoryStore(new CapriPaths(temp.Path));
        var console = new FakeCliConsole(interactive: false);
        var result = await TaskWatch.ObserveAsync(
            Guid.NewGuid(), services, console, json: true, CancellationToken.None);

        Assert.Equal(CliExitCodes.Error, result);
        Assert.Empty(console.Out);
        Assert.Contains("not found", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunningTaskCanBeWatchedAndCancelledWithoutDispatchingActions()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        var control = new FileTaskControl(paths);
        var id = Guid.NewGuid();
        var claim = new TaskOwnership(id, Environment.ProcessId,
            Process.GetCurrentProcess().StartTime.ToUniversalTime(), Environment.MachineName);
        Assert.True(control.TryAcquire(claim));

        var history = new FileTaskHistoryStore(paths);
        history.Record(new TaskHistoryEntry(
            id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Planning",
            0, null, "ollama", "qwen3.5:4b", null, null, null));

        var services = StubCliServices.CreateInTemp(temp.Path);
        services.TaskControl = control;
        services.TaskHistory = history;
        var console = new FakeCliConsole(interactive: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TaskWatch.ObserveAsync(id, services, console, json: true, cts.Token));

        Assert.Contains("\"Planning\"", console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.True(control.GetActive()?.TaskId == id);
        control.Release(id);
    }
}
