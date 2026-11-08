namespace Capri.Core.Abstractions.Voice;

/// <summary>
/// Converts text to spoken audio.
/// </summary>
public interface ISpeechSynthesizer
{
    /// <summary>
    /// True while audio is being produced.
    /// </summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Speaks the supplied text.
    /// </summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops current speech.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
