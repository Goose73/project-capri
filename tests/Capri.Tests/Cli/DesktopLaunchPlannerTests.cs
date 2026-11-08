using System.IO;
using Capri.Cli.Services;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class DesktopLaunchPlannerTests
{
    [Fact]
    public void PrefersDebugExecutable()
    {
        using var temp = new TempDirectory();
        var debug = CreateExecutable(temp.Path, "Debug");
        CreateExecutable(temp.Path, "Release");

        var plan = DesktopLaunchPlanner.Create(temp.Path);

        Assert.Equal(DesktopLaunchKind.BuiltExecutable, plan.Kind);
        Assert.Equal(debug, plan.FileName);
        Assert.Equal(Path.GetDirectoryName(debug), plan.WorkingDirectory);
    }

    [Fact]
    public void FallsBackToReleaseExecutable()
    {
        using var temp = new TempDirectory();
        var release = CreateExecutable(temp.Path, "Release");

        var plan = DesktopLaunchPlanner.Create(temp.Path);

        Assert.Equal(DesktopLaunchKind.BuiltExecutable, plan.Kind);
        Assert.Equal(release, plan.FileName);
    }

    [Fact]
    public void FallsBackToDotNetRunWhenNoExecutableExists()
    {
        using var temp = new TempDirectory();
        var projectDirectory = Path.Combine(temp.Path, "src", "Capri.Desktop");
        Directory.CreateDirectory(projectDirectory);
        var project = Path.Combine(projectDirectory, "Capri.Desktop.csproj");
        File.WriteAllText(project, "<Project />");

        var plan = DesktopLaunchPlanner.Create(temp.Path);

        Assert.Equal(DesktopLaunchKind.DotNetRun, plan.Kind);
        Assert.Equal("dotnet", plan.FileName);
        Assert.Contains("run", plan.Arguments);
        Assert.Contains(project, plan.Arguments);
        Assert.Equal(temp.Path, plan.WorkingDirectory);
    }

    [Fact]
    public void MissingRepositoryIsUnavailable()
    {
        using var temp = new TempDirectory();

        var plan = DesktopLaunchPlanner.Create(temp.Path);

        Assert.Equal(DesktopLaunchKind.Unavailable, plan.Kind);
        Assert.NotEmpty(plan.Description);
    }

    [Fact]
    public void NullRepositoryIsUnavailable()
    {
        var plan = DesktopLaunchPlanner.Create(null);

        Assert.Equal(DesktopLaunchKind.Unavailable, plan.Kind);
    }

    private static string CreateExecutable(string root, string configuration)
    {
        var directory = Path.Combine(
            root,
            "src",
            "Capri.Desktop",
            "bin",
            configuration,
            "net10.0-windows");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "Capri.Desktop.exe");
        File.WriteAllBytes(executable, []);
        return executable;
    }
}
