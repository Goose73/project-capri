using System.Diagnostics;
using System.IO;
using Capri.Cli;
using Capri.Cli.Services;
using Capri.Security.Autonomy;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class DesktopRestarterTests
{
    [Theory]
    [InlineData("restart")]
    [InlineData("RESTART")]
    public void RestartParsesWithoutUnexpectedOptions(string command)
    {
        var parsed = CliParser.Parse([command]);
        Assert.True(parsed.IsValid);
        Assert.Equal(CliCommandKind.Restart, parsed.Kind);
        Assert.True(CliParser.IsKnownCommand(command));
        Assert.False(CliParser.Parse([command, "--yes"]).IsValid);
        Assert.False(CliParser.Parse([command, "--json"]).IsValid);
        Assert.False(CliParser.Parse([command, "extra"]).IsValid);
    }

    [Fact]
    public async Task NotRunningStartsExactlyOneDesktopWithoutStatusCheck()
    {
        using var fixture = new Fixture();
        var result = await fixture.Create().RestartAsync();
        Assert.True(result.Started, result.Message);
        Assert.Equal(1, fixture.Launcher.LaunchCount);
        Assert.Equal(0, fixture.Processes.ExitRequests);
        Assert.Equal(0, fixture.StatusChecks);
    }

    [Fact]
    public async Task GracefullyExitsThenLaunchesWithoutClearingEmergencyStop()
    {
        using var fixture = new Fixture();
        fixture.AddRunning();
        var result = await fixture.Create().RestartAsync();

        Assert.True(result.Started, result.Message);
        Assert.Equal(1, fixture.Processes.ExitRequests);
        Assert.Equal(1, fixture.Launcher.LaunchCount);
        Assert.True(fixture.Status!.Stopped);
    }

    [Fact]
    public async Task ActiveTaskBlocksAllChanges()
    {
        using var fixture = new Fixture();
        fixture.AddRunning();
        fixture.Status = NewStatus(Guid.NewGuid(), false);
        var result = await fixture.Create().RestartAsync();

        Assert.False(result.Started);
        Assert.Contains("task", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Processes.ExitRequests);
        Assert.Equal(0, fixture.Launcher.LaunchCount);
    }

    [Fact]
    public async Task MissingDesktopStatusBlocksShutdown()
    {
        using var fixture = new Fixture();
        fixture.AddRunning();
        fixture.Status = null;

        var result = await fixture.Create().RestartAsync();

        Assert.False(result.Started);
        Assert.Equal(0, fixture.Processes.ExitRequests);
        Assert.Equal(0, fixture.Launcher.LaunchCount);
    }

    [Fact]
    public async Task WrongExecutableAndMultipleDesktopProcessesAreNotTouched()
    {
        using var fixture = new Fixture();
        fixture.Processes.Active.Add(new(31, @"C:\Unexpected\Capri.Desktop.exe",
            Process.GetCurrentProcess().SessionId));

        var wrong = await fixture.Create().RestartAsync();
        Assert.False(wrong.Started);
        Assert.Equal(0, fixture.Processes.ExitRequests);

        fixture.AddRunning();
        var multiple = await fixture.Create().RestartAsync();
        Assert.False(multiple.Started);
        Assert.Equal(0, fixture.Processes.ExitRequests);
        Assert.Equal(0, fixture.Launcher.LaunchCount);
    }

    [Fact]
    public async Task FailureToExitOrConcurrentLaunchNeverSpawnsDuplicate()
    {
        using var fixture = new Fixture();
        fixture.AddRunning();
        fixture.Processes.ExitSucceeds = false;
        var failed = await fixture.Create().RestartAsync();
        Assert.False(failed.Started);
        Assert.Equal(0, fixture.Launcher.LaunchCount);

        fixture.Processes.ExitSucceeds = true;
        fixture.Processes.NewProcessAppears = true;
        var raced = await fixture.Create().RestartAsync();
        Assert.False(raced.Started);
        Assert.Equal(0, fixture.Launcher.LaunchCount);
    }

    [Fact]
    public async Task CancellationHappensBeforeAnyChanges()
    {
        using var fixture = new Fixture();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Create().RestartAsync(cancel.Token));
        Assert.Equal(0, fixture.Launcher.LaunchCount);
    }

    private static TaskDesktopStatus NewStatus(Guid? activeTask, bool stopped) =>
        new(1, DateTimeOffset.UtcNow, stopped, 3, activeTask, null);

    private sealed class Fixture : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Fixture()
        {
            Root = Path.Combine(_temp.Path, "repo");
            var exe = Path.Combine(Root, "src", "Capri.Desktop", "bin", "Release",
                "net10.0-windows", "Capri.Desktop.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "fixture");
            Executable = exe;
        }

        public string Root { get; }
        public string Executable { get; }
        public StubDesktopLauncher Launcher { get; } = new();
        public FakeController Processes { get; } = new();
        public TaskDesktopStatus? Status { get; set; } = NewStatus(null, true);
        public int StatusChecks { get; private set; }

        public void AddRunning(int processId = 31) =>
            Processes.Active.Add(new(processId, Executable,
                Process.GetCurrentProcess().SessionId));

        public DesktopRestarter Create() => new(Launcher, () => Root, Processes, _ =>
        {
            StatusChecks++;
            return Task.FromResult(Status);
        });

        public void Dispose() => _temp.Dispose();
    }

    private sealed class FakeController : IDesktopProcessController
    {
        public List<DesktopProcessSnapshot> Active { get; } = [];
        public bool ExitSucceeds { get; set; } = true;
        public bool NewProcessAppears { get; set; }
        public int ExitRequests { get; private set; }

        public IReadOnlyList<DesktopProcessSnapshot> GetRunningDesktops() => Active.ToArray();

        public Task<bool> RequestGracefulExitAsync(int processId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExitRequests++;
            if (ExitSucceeds)
            {
                Active.RemoveAll(x => x.ProcessId == processId);
                if (NewProcessAppears)
                    Active.Add(new(99, @"C:\OtherBuild\Capri.Desktop.exe",
                        Process.GetCurrentProcess().SessionId));
            }
            return Task.FromResult(ExitSucceeds);
        }
    }
}
