using Capri.Core.Abstractions.Voice;

namespace Capri.Voice;

/// <summary>
/// No-op speech synthesizer used until a real voice backend is implemented.
/// </summary>
public sealed class NullSpeechSynthesizer : ISpeechSynthesizer
{
    /// <summary>
    /// False because this scaffold does not produce audio.
    /// </summary>
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public bool IsSpeaking => false;

    /// <inheritdoc />
    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
