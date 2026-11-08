using Capri.Core.Intents;
using Capri.Core.Models;

namespace Capri.Tests.Core;

public sealed class RuleBasedIntentParserTests
{
    private readonly RuleBasedIntentParser _parser = new();

    [Theory]
    [InlineData("play", MediaCommand.PlayPause)]
    [InlineData("pause", MediaCommand.PlayPause)]
    [InlineData("toggle", MediaCommand.PlayPause)]
    [InlineData("resume", MediaCommand.PlayPause)]
    [InlineData("next", MediaCommand.Next)]
    [InlineData("skip", MediaCommand.Next)]
    [InlineData("previous", MediaCommand.Previous)]
    [InlineData("back", MediaCommand.Previous)]
    [InlineData("stop", MediaCommand.Stop)]
    [InlineData("play music", MediaCommand.PlayPause)]
    [InlineData("next track", MediaCommand.Next)]
    [InlineData("previous song", MediaCommand.Previous)]
    [InlineData("STOP MUSIC", MediaCommand.Stop)]
    public void ParseRecognizesMediaCommands(string input, MediaCommand expected)
    {
        var intent = _parser.Parse(input);

        Assert.Equal(IntentKind.MediaControl, intent.Kind);
        Assert.Equal(expected, intent.Media);
        Assert.Equal(input, intent.RawText);
    }

    [Theory]
    [InlineData("open https://example.com", "https://example.com/")]
    [InlineData("go to https://youtube.com/watch?v=1", "https://youtube.com/watch?v=1")]
    [InlineData("open youtube.com", "https://youtube.com/")]
    [InlineData("open netflix.com", "https://netflix.com/")]
    [InlineData("visit example.org/path?q=1", "https://example.org/path?q=1")]
    public void ParseRecognizesSafeWebTargets(string input, string expectedUrl)
    {
        var intent = _parser.Parse(input);

        Assert.Equal(IntentKind.OpenWeb, intent.Kind);
        Assert.Equal(expectedUrl, intent.Target);
        Assert.Equal(input, intent.RawText);
    }

    [Fact]
    public void ParseBuildsSafeYouTubeSearchUrl()
    {
        var intent = _parser.Parse("search youtube for lofi beats");

        Assert.Equal(IntentKind.OpenWeb, intent.Kind);
        Assert.Equal("https://www.youtube.com/results?search_query=lofi%20beats", intent.Target);
    }

    [Theory]
    [InlineData("search for dotnet docs", "https://duckduckgo.com/?q=dotnet%20docs")]
    [InlineData("search google for weather today", "https://www.google.com/search?q=weather%20today")]
    public void ParseBuildsSafeSearchUrls(string input, string expected)
    {
        var intent = _parser.Parse(input);

        Assert.Equal(IntentKind.OpenWeb, intent.Kind);
        Assert.Equal(expected, intent.Target);
    }

    [Theory]
    [InlineData("open brave", "brave")]
    [InlineData("open chrome", "chrome")]
    [InlineData("open edge", "edge")]
    [InlineData("open firefox", "firefox")]
    [InlineData("open notepad", "notepad")]
    [InlineData("open calculator", "calculator")]
    [InlineData("open calc", "calc")]
    [InlineData("open explorer", "explorer")]
    [InlineData("open files", "files")]
    [InlineData("open paint", "paint")]
    [InlineData("launch Notepad", "notepad")]
    [InlineData("start Chrome", "chrome")]
    public void ParseRecognizesAppLaunchAliases(string input, string expectedAlias)
    {
        var intent = _parser.Parse(input);

        Assert.Equal(IntentKind.LaunchApp, intent.Kind);
        Assert.Equal(expectedAlias, intent.Target);
    }

    [Theory]
    [InlineData("hey capri, play")]
    [InlineData("please open brave")]
    [InlineData("could you open https://example.com")]
    [InlineData("Hey Capri please search youtube for cats")]
    [InlineData("ok capri, pause please")]
    public void ParseToleratesPolitePhrasing(string input)
    {
        var intent = _parser.Parse(input);

        Assert.NotEqual(IntentKind.Unknown, intent.Kind);
        Assert.Equal(input, intent.RawText);
    }

    [Theory]
    [InlineData("what's the weather?")]
    [InlineData("tell me a joke")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("open my Downloads folder")]
    [InlineData("open C:\\Windows\\System32\\notepad.exe")]
    [InlineData("launch ..\\secret.exe")]
    [InlineData("open %TEMP%\\app.exe")]
    [InlineData("open file:///C:/Windows/System32/cmd.exe")]
    [InlineData("go to javascript:alert(1)")]
    [InlineData("open cmd.exe")]
    [InlineData("open powershell")]
    [InlineData("run regedit")]
    [InlineData("open https://user:pass@example.com")]
    public void ParseDoesNotCreateUnsafeAutomationIntents(string input)
    {
        var intent = _parser.Parse(input);

        Assert.True(
            intent.Kind is IntentKind.Chat or IntentKind.Unknown,
            $"Expected non-automation intent for '{input}', got {intent.Kind} target '{intent.Target}'.");
        Assert.DoesNotContain("\\", intent.Target ?? string.Empty);
        Assert.DoesNotContain("..", intent.Target ?? string.Empty);
        Assert.DoesNotContain("%", intent.Target ?? string.Empty);
        if (intent.Target is { Length: > 0 } target)
        {
            Assert.True(
                target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || intent.Kind == IntentKind.LaunchApp,
                $"Unexpected non-http target: {target}");
        }
    }

    [Fact]
    public void ParseNeverTreatsSearchQueryAsArbitraryPath()
    {
        var intent = _parser.Parse("search for C:\\Windows\\System32");

        Assert.Equal(IntentKind.OpenWeb, intent.Kind);
        Assert.NotNull(intent.Target);
        Assert.StartsWith("https://duckduckgo.com/?q=", intent.Target);
        Assert.DoesNotContain("file:", intent.Target, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\", intent.Target);
    }

    [Fact]
    public void ParsePreservesRawText()
    {
        const string input = "Hey Capri, please open brave!";
        var intent = _parser.Parse(input);

        Assert.Equal(input, intent.RawText);
    }
}
