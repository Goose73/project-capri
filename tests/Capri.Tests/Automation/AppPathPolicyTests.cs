using System.IO;
using Capri.Automation.Apps;

namespace Capri.Tests.Automation;

public sealed class AppPathPolicyTests
{
    private static readonly string WindowsDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Fact]
    public void AllowsAnExecutableUnderTheWindowsDirectory()
    {
        var path = Path.Combine(WindowsDirectory, "System32", "notepad.exe");

        var accepted = AppPathPolicy.TryValidatePath(path, out var normalizedPath, out var reason);

        Assert.True(accepted, reason);
        Assert.Equal(Path.GetFullPath(path), normalizedPath);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("pwsh.exe")]
    [InlineData("wscript.exe")]
    [InlineData("cscript.exe")]
    [InlineData("mshta.exe")]
    [InlineData("rundll32.exe")]
    [InlineData("regsvr32.exe")]
    [InlineData("reg.exe")]
    [InlineData("regedit.exe")]
    [InlineData("msiexec.exe")]
    public void RejectsLivingOffTheLandBinaries(string executableName)
    {
        var path = Path.Combine(WindowsDirectory, "System32", executableName);

        var accepted = AppPathPolicy.TryValidatePath(path, out _, out var reason);

        Assert.False(accepted);
        Assert.Contains("restricted", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"C:\Program Files\SomeApp\SomeApp.exe")]
    public void ForbiddenExecutableCheckIgnoresSafeFiles(string path)
    {
        Assert.False(AppPathPolicy.IsForbiddenExecutable(path));
    }

    [Fact]
    public void RejectsParentDirectoryTraversal()
    {
        var path = Path.Combine(WindowsDirectory, "System32", "..", "explorer.exe");

        var accepted = AppPathPolicy.TryValidatePath(path, out _, out var reason);

        Assert.False(accepted);
        Assert.Contains("traversal", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUncPaths()
    {
        var accepted = AppPathPolicy.TryValidatePath(
            @"\\server\share\app.exe",
            out _,
            out var reason);

        Assert.False(accepted);
        Assert.Contains("UNC", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsRelativePaths()
    {
        var accepted = AppPathPolicy.TryValidatePath("notepad.exe", out _, out var reason);

        Assert.False(accepted);
        Assert.Contains("absolute", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsNonExeExtensions()
    {
        var path = Path.Combine(WindowsDirectory, "System32", "notepad.txt");

        var accepted = AppPathPolicy.TryValidatePath(path, out _, out var reason);

        Assert.False(accepted);
        Assert.Contains(".exe", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsEnvironmentVariablePaths()
    {
        var accepted = AppPathPolicy.TryValidatePath(
            @"%TEMP%\evil.exe",
            out _,
            out var reason);

        Assert.False(accepted);
        Assert.Contains("Environment-variable", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsTemporaryDirectoryPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), "evil.exe");

        var accepted = AppPathPolicy.TryValidatePath(path, out _, out var reason);

        Assert.False(accepted);
        Assert.Contains("temporary", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsPathsOutsideAllowedRoots()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "evil.exe");

        var accepted = AppPathPolicy.TryValidatePath(path, out _, out var reason);

        Assert.False(accepted);
        Assert.Contains("outside", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsProgramFilesPrefixConfusion()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var path = Path.Combine(programFiles + "Evil", "app.exe");

        var accepted = AppPathPolicy.TryValidatePath(path, out _, out var reason);

        Assert.False(accepted);
        Assert.Contains("outside", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAlternateDataStreams()
    {
        var path = Path.Combine(WindowsDirectory, "System32", "notepad.exe") + ":evil";

        var accepted = AppPathPolicy.TryValidatePath(path, out _, out var reason);

        Assert.False(accepted);
        Assert.Contains("data stream", reason, StringComparison.OrdinalIgnoreCase);
    }
}
