using Capri.Core.Voice;

namespace Capri.Tests.Core;

public sealed class WakeWordPolicyTests
{
    [Theory]
    [InlineData("Capri")]
    [InlineData("hey Capri")]
    [InlineData("HEY  CAPRI")]
    [InlineData("Hey, Capri!")]
    public void AcceptsOnlyTheTwoExplicitWakePhrases(string phrase)
    {
        Assert.True(WakeWordPolicy.IsWakePhrase(phrase, 0.87f));
    }

    [Theory]
    [InlineData("Capri open my photos")]
    [InlineData("My friend Capri went home")]
    [InlineData("Captain")]
    [InlineData("Capri's")]
    [InlineData("Hi Alexa")]
    [InlineData("")]
    public void DoesNotActivateForNearbyWordsOrSubstrings(string phrase)
    {
        Assert.False(WakeWordPolicy.IsWakePhrase(phrase, 0.99f));
    }

    [Theory]
    [InlineData("Hey Capri, what time is it?", "what time is it?")]
    [InlineData("Capri how are you?", "how are you?")]
    [InlineData("HEY CAPRI: open the chat", "open the chat")]
    [InlineData("Hey, Capri! Give me a summary.", "Give me a summary.")]
    public void SupportsWakeAndQuestionInOneUtterance(string utterance, string expected)
    {
        Assert.Equal(expected, WakeWordPolicy.ExtractInlineQuestion(utterance, 0.85f));
    }

    [Theory]
    [InlineData("I met Capri yesterday")]
    [InlineData("Someone said hey Capri how are you")]
    [InlineData("Captain how are you")]
    [InlineData("capricious little thing")]
    [InlineData("Capri's keyboard is broken")]
    [InlineData("hey Capricorn, what time is it")]
    [InlineData("Capri")]
    [InlineData("")]
    public void RejectsUnanchoredOrWakeOnlySingleUtterances(string utterance)
    {
        Assert.Null(WakeWordPolicy.ExtractInlineQuestion(utterance, 0.99f));
    }

    [Fact]
    public void RejectsLowConfidenceInlineQuestion()
    {
        Assert.Null(WakeWordPolicy.ExtractInlineQuestion("Hey Capri, open notes", 0.3f));
        Assert.Null(WakeWordPolicy.ExtractInlineQuestion(
            "Hey Capri " + new string('x', 900), 0.99f));
    }

    [Fact]
    public void LowConfidenceNeverActivatesMicrophoneQuestion()
    {
        Assert.False(WakeWordPolicy.IsWakePhrase("Capri", 0.35f));
        Assert.Null(WakeWordPolicy.AcceptQuestion(
            "How are you?", 0.4f, true, DateTimeOffset.UtcNow.AddSeconds(5),
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void QuestionMustFollowWakeWithinShortWindow()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("What time is it?", WakeWordPolicy.AcceptQuestion(
            "What time is it?", 0.87f, true, now.AddSeconds(12), now));
        Assert.Null(WakeWordPolicy.AcceptQuestion(
            "What time is it?", 0.87f, false, now.AddSeconds(12), now));
        Assert.Null(WakeWordPolicy.AcceptQuestion(
            "What time is it?", 0.87f, true, now.AddSeconds(-1), now));
        Assert.Null(WakeWordPolicy.AcceptQuestion(
            "Capri", 0.87f, true, now.AddSeconds(12), now));
        Assert.Null(WakeWordPolicy.AcceptQuestion(
            new string('a', 900), 0.87f, true, now.AddSeconds(12), now));
    }
}

public sealed class SpokenReplyTextTests
{
    [Fact]
    public void NeverNarratesSourceCodeFromFencedBlocks()
    {
        const string text = """
            ## Here's an example

            ```csharp
            string secret = "dontread";
            Console.WriteLine("hi");
            ```

            **Done** and ready.
            """;
        var spoken = SpokenReplyText.Prepare(text);
        Assert.Contains("Here's an example", spoken);
        Assert.Contains("included code", spoken);
        Assert.Contains("Done", spoken);
        Assert.DoesNotContain("dontread", spoken);
        Assert.DoesNotContain("Console.WriteLine", spoken);
        Assert.DoesNotContain("**", spoken);
    }

    [Fact]
    public void ReturnsShortBoundedSpeechAndOmitsStandaloneLinks()
    {
        Assert.Equal(string.Empty, SpokenReplyText.Prepare(null));
        var spoken = SpokenReplyText.Prepare("https://example.invalid\n" + new string('x', 3000));
        Assert.True(spoken.Length <= 754);
        Assert.Contains("link in the chat", spoken);
        Assert.DoesNotContain("https://", spoken);
    }
}
