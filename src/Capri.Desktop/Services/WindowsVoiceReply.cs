using System.Speech.Synthesis;
using Capri.Core.Models;
using Capri.Core.Voice;

namespace Capri.Desktop.Services;

/// <summary>
/// Local SAPI speech fallback. Installed Windows voices are functional but
/// are not a reproduction of a performer and are not a high-quality neural
/// voice. This adapter can later be replaced with properly licensed local TTS.
/// </summary>
public sealed class WindowsVoiceReply
{
    private readonly object _sync = new();
    private SpeechSynthesizer? _active;

    public async Task SpeakAsync(
        string response, AppSettings.VoiceSettings voice,
        CancellationToken cancellationToken = default)
    {
        var spoken = SpokenReplyText.Prepare(response);
        if (spoken.Length == 0) return;
        cancellationToken.ThrowIfCancellationRequested();

        using var synthesizer = new SpeechSynthesizer();
        synthesizer.SetOutputToDefaultAudioDevice();
        synthesizer.Volume = Math.Clamp((int)Math.Round(voice.Volume * 100), 0, 100);
        synthesizer.Rate = Math.Clamp((int)Math.Round((voice.SpeechRate - 1) * 5), -3, 3);
        try { synthesizer.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult); }
        catch (ArgumentException) { /* Retain installed default if no female voice exists. */ }

        SpeechSynthesizer? old;
        lock (_sync)
        {
            old = _active;
            _active = synthesizer;
        }
        try { old?.SpeakAsyncCancelAll(); }
        catch (ObjectDisposedException) { }

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnComplete(object? sender, SpeakCompletedEventArgs e)
        {
            if (e.Cancelled) finished.TrySetCanceled(cancellationToken);
            else if (e.Error is { } error) finished.TrySetException(error);
            else finished.TrySetResult();
        }

        synthesizer.SpeakCompleted += OnComplete;
        using var registration = cancellationToken.Register(() =>
        {
            try { synthesizer.SpeakAsyncCancelAll(); }
            catch (ObjectDisposedException) { }
        });
        try
        {
            synthesizer.SpeakAsync(spoken);
            await finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            synthesizer.SpeakCompleted -= OnComplete;
            try { synthesizer.SpeakAsyncCancelAll(); }
            catch (ObjectDisposedException) { }
            lock (_sync)
            {
                if (ReferenceEquals(_active, synthesizer))
                    _active = null;
            }
        }
    }

    public void Stop()
    {
        SpeechSynthesizer? speaker;
        lock (_sync) speaker = _active;
        try { speaker?.SpeakAsyncCancelAll(); }
        catch (ObjectDisposedException) { }
    }
}
