using System.IO;
using Capri.Automation.Apps;

namespace Capri.Tests.Automation;

public sealed class AppPathValidatorTests
{
    private static readonly string WindowsDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Fact]
    public void AcceptsAnExistingSafeExecutable()
    {
        var path = Path.Combine(WindowsDirectory, "System32", "notepad.exe");
        var validator = new AppPathValidator(_ => true);

        var result = validator.Validate(path);

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(Path.GetFullPath(path), result.NormalizedPath);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void RejectsAnExecutableThatDoesNotExist()
    {
        var path = Path.Combine(WindowsDirectory, "System32", "notepad.exe");
        var validator = new AppPathValidator(_ => false);

        var result = validator.Validate(path);

        Assert.False(result.IsValid);
        Assert.Contains("not found", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsForbiddenExecutableEvenWhenItExists()
    {
        var path = Path.Combine(WindowsDirectory, "System32", "cmd.exe");
        var validator = new AppPathValidator(_ => true);

        var result = validator.Validate(path);

        Assert.False(result.IsValid);
        Assert.Contains("restricted", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsTemporaryPathEvenWhenItExists()
    {
        var path = Path.Combine(Path.GetTempPath(), "evil.exe");
        var validator = new AppPathValidator(_ => true);

        var result = validator.Validate(path);

        Assert.False(result.IsValid);
        Assert.Contains("temporary", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
