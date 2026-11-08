namespace Capri.Core.Abstractions.Voice;

/// <summary>
/// Captures a single spoken utterance and returns its transcript.
/// </summary>
public interface ISpeechRecognizer
{
    /// <summary>
    /// Listens for one utterance and returns the recognized text, or null when none was recognized.
    /// </summary>
    Task<string?> RecognizeOnceAsync(CancellationToken cancellationToken = default);
}
