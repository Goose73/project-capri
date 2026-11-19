using System.Speech.Recognition;

namespace Capri.Tests.Desktop;

/// <summary>
/// Tests the installed Windows recognizer with synthetic/emulated text.
/// No microphone input is opened, captured, recorded or uploaded.
/// </summary>
public sealed class OfflineWakeGrammarTests
{
    [Fact]
    public void WakeAndOneBreathQuestionUseDifferentExplicitGrammars()
    {
        if (!OperatingSystem.IsWindows()) return;
        var recognizer = SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(x => x.Culture.Name.Equals("en-GB", StringComparison.OrdinalIgnoreCase));
        if (recognizer is null) return; // Clean CI image may not have an offline UK language.

        using var engine = new SpeechRecognitionEngine(recognizer);
        var wake = new GrammarBuilder { Culture = recognizer.Culture };
        wake.Append(new Choices("Hey Capri", "Capri"));
        engine.LoadGrammar(new Grammar(wake) { Name = "capri-wake" });
        engine.LoadGrammar(new DictationGrammar { Name = "capri-question", Enabled = false });

        var inline = new GrammarBuilder { Culture = recognizer.Culture };
        inline.Append(new Choices("Hey Capri", "Capri"));
        inline.AppendDictation();
        engine.LoadGrammar(new Grammar(inline) { Name = "capri-command" });

        engine.SetInputToNull();
        Assert.Equal("capri-wake", engine.EmulateRecognize("Hey Capri")?.Grammar.Name);
        Assert.Equal("capri-wake", engine.EmulateRecognize("Capri")?.Grammar.Name);
        Assert.Equal("capri-command",
            engine.EmulateRecognize("Hey Capri what time is it")?.Grammar.Name);
        Assert.Equal("capri-command",
            engine.EmulateRecognize("Capri how are you")?.Grammar.Name);
    }
}
