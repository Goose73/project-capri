using System.Diagnostics;
using System.IO;
using Capri.Security;
using Capri.Security.Autonomy;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Autonomy;

public sealed class CrossProcessControlTests
{
    private static string Probe => Path.Combine(AppContext.BaseDirectory, "CoordinationProbe", "Capri.CoordinationProbe.dll");

    private static Process Start(string operation, string root)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Probe);
        start.ArgumentList.Add(operation);
        start.ArgumentList.Add(root);
        return Process.Start(start)!;
    }

    [Fact]
    public async Task SeparateProcessesContendAndCrashReleasesWithoutExpiringLiveOwner()
    {
        using var temp = new TempDirectory();
        using var holder = Start("claim", temp.Path);
        var id = Guid.Parse((await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!);
        var control = new FileTaskControl(new CapriPaths(temp.Path), TimeSpan.FromMilliseconds(1));
        Assert.Equal(id, control.GetActive()?.TaskId);
        using (var contender = Start("claim", temp.Path))
        {
            await contender.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(3, contender.ExitCode);
        }
        holder.Kill();
        await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(control.GetActive());
        using var successor = Start("claim", temp.Path);
        Assert.NotNull(await successor.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await successor.StandardInput.WriteLineAsync(string.Empty);
        await successor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, successor.ExitCode);
    }

    [Fact]
    public async Task ConcurrentExternalStopsAndResetsPreserveGenerationAndCorruption()
    {
        using var temp = new TempDirectory();
        using var a = Start("stop", temp.Path);
        using var b = Start("stop", temp.Path);
        await Task.WhenAll(a.WaitForExitAsync(), b.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, a.ExitCode);
        Assert.Equal(0, b.ExitCode);
        var control = new FileTaskControl(new CapriPaths(temp.Path));
        Assert.Equal(2, control.GetStopState().Generation);
        using var reset = Start("reset", temp.Path);
        using var stop = Start("stop", temp.Path);
        await Task.WhenAll(reset.WaitForExitAsync(), stop.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, reset.ExitCode);
        Assert.Equal(0, stop.ExitCode);
        Assert.Equal(3, control.GetStopState().Generation);
        File.WriteAllText(Path.Combine(temp.Path, "tasks", "stop.json"), "{");
        using var corrupt = Start("reset", temp.Path);
        await corrupt.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(4, corrupt.ExitCode);
    }
}
