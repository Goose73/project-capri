using System.Speech.Synthesis;

namespace Capri.Desktop.Services;

public interface IAmbientCommentSpeech
{
    Task SayAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// Local Windows SAPI speech synthesis, never a cloud voice provider.
/// The user must separately opt into spoken spontaneous remarks.
/// </summary>
public sealed class WindowsAmbientCommentSpeech : IAmbientCommentSpeech
{
    public async Task SayAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        cancellationToken.ThrowIfCancellationRequested();
        using var synthesizer = new SpeechSynthesizer();
        synthesizer.SetOutputToDefaultAudioDevice();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, SpeakCompletedEventArgs e)
        {
            if (e.Cancelled) finished.TrySetCanceled(cancellationToken);
            else if (e.Error is not null) finished.TrySetException(e.Error);
            else finished.TrySetResult();
        }

        synthesizer.SpeakCompleted += OnCompleted;
        using var registration = cancellationToken.Register(() =>
        {
            try { synthesizer.SpeakAsyncCancelAll(); }
            catch (ObjectDisposedException) { }
        });
        try
        {
            synthesizer.SpeakAsync(text);
            await finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            synthesizer.SpeakCompleted -= OnCompleted;
            try { synthesizer.SpeakAsyncCancelAll(); }
            catch (ObjectDisposedException) { }
        }
    }
}
