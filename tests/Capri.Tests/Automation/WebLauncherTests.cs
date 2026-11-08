using Capri.Automation.Web;

namespace Capri.Tests.Automation;

public sealed class WebLauncherTests
{
    [Fact]
    public async Task OpenAsyncUsesShellExecuteWithValidatedAbsoluteUrl()
    {
        var starter = new RecordingProcessStarter();
        var launcher = new WebLauncher(starter);

        var result = await launcher.OpenAsync(new Uri("https://example.com/path?q=1"));

        Assert.True(result.Succeeded, result.Message);
        var startInfo = Assert.Single(starter.Requests);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("https://example.com/path?q=1", startInfo.FileName);
        Assert.Empty(startInfo.ArgumentList);
    }

    [Fact]
    public async Task OpenAsyncRejectsNonHttpUriWithoutStartingAProcess()
    {
        var starter = new RecordingProcessStarter();
        var launcher = new WebLauncher(starter);

        var result = await launcher.OpenAsync(new Uri("file:///C:/Windows/System32/calc.exe"));

        Assert.False(result.Succeeded);
        Assert.Contains("file", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(starter.Requests);
    }

    [Fact]
    public async Task SearchAsyncPercentEncodesTheQuery()
    {
        var starter = new RecordingProcessStarter();
        var launcher = new WebLauncher(starter);

        var result = await launcher.SearchAsync("hello world & more");

        Assert.True(result.Succeeded, result.Message);
        var startInfo = Assert.Single(starter.Requests);
        Assert.True(startInfo.UseShellExecute);
        Assert.StartsWith(WebLauncher.DefaultSearchBaseUri.AbsoluteUri, startInfo.FileName, StringComparison.Ordinal);
        Assert.Contains("q=hello%20world%20%26%20more", startInfo.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsyncRejectsBlankQuery()
    {
        var starter = new RecordingProcessStarter();
        var launcher = new WebLauncher(starter);

        var result = await launcher.SearchAsync("   ");

        Assert.False(result.Succeeded);
        Assert.Empty(starter.Requests);
    }

    [Fact]
    public async Task OpenAsyncReturnsStarterFailure()
    {
        var starter = new RecordingProcessStarter
        {
            Result = false,
            FailureReason = "No browser is registered.",
        };
        var launcher = new WebLauncher(starter);

        var result = await launcher.OpenAsync(new Uri("https://example.com"));

        Assert.False(result.Succeeded);
        Assert.Equal("No browser is registered.", result.Message);
    }

    [Fact]
    public async Task CancelledRequestDoesNotStartAProcess()
    {
        var starter = new RecordingProcessStarter();
        var launcher = new WebLauncher(starter);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await launcher.OpenAsync(new Uri("https://example.com"), cts.Token);

        Assert.False(result.Succeeded);
        Assert.Empty(starter.Requests);
    }
}
