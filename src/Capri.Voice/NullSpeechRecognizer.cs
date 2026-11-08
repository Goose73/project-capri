using Capri.Core.Abstractions.Voice;

namespace Capri.Voice;

/// <summary>
/// No-op speech recognizer used until a real voice backend is implemented.
/// </summary>
public sealed class NullSpeechRecognizer : ISpeechRecognizer
{
    /// <summary>
    /// False because this scaffold does not capture audio.
    /// </summary>
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public Task<string?> RecognizeOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
