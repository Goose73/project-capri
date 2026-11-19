using System.IO;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Capri.Core.Models;
using Capri.Core.Voice;

namespace Capri.Desktop.Services;

/// <summary>
/// Optional local neural TTS via the loopback-only /synthesize protocol used by
/// Piper and Capri's installed Kokoro service. An existing local Piper server
/// is left untouched. The separately installed Capri service may be launched
/// on demand when explicitly enabled; no models are downloaded and no mic,
/// remote server or cloud API is started. No user speech is logged.
/// </summary>
public sealed class PiperNeuralVoiceReply : IDisposable
{
    private const int MaxWavBytes = 8 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly object _sync = new();
    private SoundPlayer? _active;

    public PiperNeuralVoiceReply()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false
        };
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
    }

    public async Task<bool> TrySpeakAsync(
        string text, AppSettings.VoiceSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.UseLocalNeuralVoice ||
            !LocalNeuralVoiceSettings.TryGetSynthesisUri(settings.NeuralVoiceEndpoint, out var endpoint))
            return false;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await CapriLocalVoiceBootstrapper.EnsureAvailableAsync(endpoint!, cancellationToken)
                .ConfigureAwait(false);

            // Optional voice aliases work in the Capri Kokoro service and in
            // multi-voice Piper servers. Omit the field to preserve the
            // existing Piper default, including custom server configurations.
            var payload = new Dictionary<string, object>
            {
                ["text"] = text,
                ["length_scale"] = Math.Clamp(1d / settings.SpeechRate, 0.8d, 1.25d),
                ["noise_scale"] = 0.6d,
                ["noise_w_scale"] = 0.8d,
                ["volume"] = Math.Clamp(settings.Volume, 0, 1)
            };
            if (!string.IsNullOrWhiteSpace(settings.NeuralVoiceName))
                payload["voice"] = settings.NeuralVoiceName.Trim();

            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };

            using var response = await _client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentLength is > MaxWavBytes)
                return false;
            await response.Content.LoadIntoBufferAsync(MaxWavBytes, cancellationToken)
                .ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (bytes.Length is < 44 or > MaxWavBytes ||
                !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
                !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
                return false;

            using var stream = new MemoryStream(bytes, writable: false);
            using var player = new SoundPlayer(stream);
            lock (_sync) _active = player;
            try
            {
                using var registration = cancellationToken.Register(Stop);
                await Task.Run(player.PlaySync, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return true;
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_active, player)) _active = null;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or
                                       InvalidOperationException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    public void Stop()
    {
        SoundPlayer? current;
        lock (_sync) current = _active;
        current?.Stop();
    }

    public void Dispose()
    {
        Stop();
        _client.Dispose();
    }
}
