using Capri.Automation.Media;
using Capri.Core.Models;

namespace Capri.Tests.Automation;

public sealed class MediaControlTests
{
    [Theory]
    [InlineData(MediaCommand.PlayPause, 0xB3)]
    [InlineData(MediaCommand.Next, 0xB0)]
    [InlineData(MediaCommand.Previous, 0xB1)]
    [InlineData(MediaCommand.Stop, 0xB2)]
    public void MapsMediaCommandsToWindowsVirtualKeys(MediaCommand command, int expectedVirtualKey)
    {
        var mapped = MediaControl.TryMap(command, out var virtualKey);

        Assert.True(mapped);
        Assert.Equal(expectedVirtualKey, virtualKey);
    }

    [Fact]
    public void RejectsUnknownMediaCommand()
    {
        var mapped = MediaControl.TryMap((MediaCommand)99, out var virtualKey);

        Assert.False(mapped);
        Assert.Equal(0, virtualKey);
    }

    [Theory]
    [InlineData(MediaCommand.PlayPause, 0xB3)]
    [InlineData(MediaCommand.Next, 0xB0)]
    [InlineData(MediaCommand.Previous, 0xB1)]
    [InlineData(MediaCommand.Stop, 0xB2)]
    public async Task ExecuteAsyncSendsMappedVirtualKey(MediaCommand command, int expectedVirtualKey)
    {
        var sender = new RecordingMediaKeySender();
        var mediaControl = new MediaControl(sender);

        var result = await mediaControl.ExecuteAsync(command);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal([expectedVirtualKey], sender.SentVirtualKeys);
    }

    [Fact]
    public async Task ExecuteAsyncReturnsSenderFailure()
    {
        var sender = new RecordingMediaKeySender
        {
            Result = false,
            FailureReason = "Input was blocked.",
        };
        var mediaControl = new MediaControl(sender);

        var result = await mediaControl.ExecuteAsync(MediaCommand.Next);

        Assert.False(result.Succeeded);
        Assert.Equal("Input was blocked.", result.Message);
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotSendWhenCancelled()
    {
        var sender = new RecordingMediaKeySender();
        var mediaControl = new MediaControl(sender);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await mediaControl.ExecuteAsync(MediaCommand.PlayPause, cts.Token);

        Assert.False(result.Succeeded);
        Assert.Empty(sender.SentVirtualKeys);
    }

    [Fact]
    public void WindowsSenderRejectsOutOfRangeVirtualKey()
    {
        var sender = new WindowsMediaKeySender();

        var sent = sender.TrySend(0x100, out var failureReason);

        Assert.False(sent);
        Assert.False(string.IsNullOrWhiteSpace(failureReason));
    }
}
