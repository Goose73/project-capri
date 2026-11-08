using System.IO;
using Capri.Automation.Apps;
using Capri.Core.Models;

namespace Capri.Tests.Automation;

public sealed class AppLauncherTests
{
    private static readonly string SafePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");

    [Fact]
    public async Task LaunchesValidatedTargetWithoutArguments()
    {
        var starter = new RecordingProcessStarter();
        var validator = new StubAppPathValidator
        {
            Result = new AppPathValidationResult(true, SafePath, null),
        };
        var launcher = new AppLauncher(starter, validator);

        var result = await launcher.LaunchAsync(new AppTarget("Notepad", SafePath, "notepad"));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(SafePath, validator.LastPath);
        var startInfo = Assert.Single(starter.Requests);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(SafePath, startInfo.FileName);
        Assert.Empty(startInfo.ArgumentList);
    }

    [Fact]
    public async Task RejectsTargetWhenPathValidatorRejectsIt()
    {
        var starter = new RecordingProcessStarter();
        var validator = new StubAppPathValidator
        {
            Result = new AppPathValidationResult(false, null, "The executable is a restricted system tool."),
        };
        var launcher = new AppLauncher(starter, validator);

        var result = await launcher.LaunchAsync(
            new AppTarget("Command Prompt", @"C:\Windows\System32\cmd.exe", "cmd"));

        Assert.False(result.Succeeded);
        Assert.Contains("restricted", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(starter.Requests);
    }

    [Fact]
    public async Task RejectsTargetWithoutExecutablePath()
    {
        var starter = new RecordingProcessStarter();
        var validator = new StubAppPathValidator
        {
            Result = new AppPathValidationResult(false, null, "An application executable path is required."),
        };
        var launcher = new AppLauncher(starter, validator);

        var result = await launcher.LaunchAsync(new AppTarget("Unknown"));

        Assert.False(result.Succeeded);
        Assert.Null(validator.LastPath);
        Assert.Empty(starter.Requests);
    }

    [Fact]
    public async Task ReturnsStarterFailure()
    {
        var starter = new RecordingProcessStarter
        {
            Result = false,
            FailureReason = "The application could not be started.",
        };
        var validator = new StubAppPathValidator
        {
            Result = new AppPathValidationResult(true, SafePath, null),
        };
        var launcher = new AppLauncher(starter, validator);

        var result = await launcher.LaunchAsync(new AppTarget("Notepad", SafePath));

        Assert.False(result.Succeeded);
        Assert.Equal("The application could not be started.", result.Message);
    }

    [Fact]
    public async Task CancelledLaunchDoesNotStartAProcess()
    {
        var starter = new RecordingProcessStarter();
        var validator = new StubAppPathValidator
        {
            Result = new AppPathValidationResult(true, SafePath, null),
        };
        var launcher = new AppLauncher(starter, validator);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await launcher.LaunchAsync(
            new AppTarget("Notepad", SafePath),
            cts.Token);

        Assert.False(result.Succeeded);
        Assert.Empty(starter.Requests);
    }
}
