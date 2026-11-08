using Capri.Core.Abstractions.Voice;
using Capri.Voice;
using Capri.Voice.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Capri.Tests.Voice;

public sealed class NullVoiceImplementationTests
{
    [Fact]
    public void SynthesizerIsUnavailableAndSilent()
    {
        var synthesizer = new NullSpeechSynthesizer();

        Assert.False(synthesizer.IsAvailable);
        Assert.False(synthesizer.IsSpeaking);
    }

    [Fact]
    public async Task SynthesizerOperationsComplete()
    {
        var synthesizer = new NullSpeechSynthesizer();

        await synthesizer.SpeakAsync("hello");
        await synthesizer.StopAsync();

        Assert.False(synthesizer.IsSpeaking);
    }

    [Fact]
    public async Task SynthesizerHonorsCancellation()
    {
        var synthesizer = new NullSpeechSynthesizer();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => synthesizer.SpeakAsync("hello", cts.Token));
    }

    [Fact]
    public void RecognizerIsUnavailable()
    {
        var recognizer = new NullSpeechRecognizer();

        Assert.False(recognizer.IsAvailable);
    }

    [Fact]
    public async Task RecognizerReturnsNoTranscript()
    {
        var recognizer = new NullSpeechRecognizer();

        var transcript = await recognizer.RecognizeOnceAsync();

        Assert.Null(transcript);
    }

    [Fact]
    public async Task RecognizerHonorsCancellation()
    {
        var recognizer = new NullSpeechRecognizer();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => recognizer.RecognizeOnceAsync(cts.Token));
    }

    [Fact]
    public void VoiceServicesResolveToNullImplementations()
    {
        var services = new ServiceCollection();
        services.AddCapriVoice();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<NullSpeechSynthesizer>(provider.GetRequiredService<ISpeechSynthesizer>());
        Assert.IsType<NullSpeechRecognizer>(provider.GetRequiredService<ISpeechRecognizer>());
    }
}
