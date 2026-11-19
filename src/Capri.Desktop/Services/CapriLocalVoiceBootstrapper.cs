using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace Capri.Desktop.Services;

/// <summary>
/// Starts the specifically installed Capri Kokoro voice service only when the
/// user has enabled local neural speech. No downloads, arbitrary executable
/// paths, cloud requests or microphone access. Existing user-configured Piper
/// services are never replaced.
/// </summary>
internal static class CapriLocalVoiceBootstrapper
{
    private static readonly SemaphoreSlim StartGate = new(1, 1);

    // The built-in voice has exactly one known address; never use user-supplied
    // endpoints to launch arbitrary local processes.
    internal static bool IsCapriEndpoint(Uri endpoint) =>
        endpoint.Scheme == Uri.UriSchemeHttp &&
        endpoint.Host == "127.0.0.1" &&
        endpoint.Port == 5000 &&
        endpoint.AbsolutePath == "/synthesize" &&
        string.IsNullOrEmpty(endpoint.Query) &&
        string.IsNullOrEmpty(endpoint.Fragment);

    public static async Task EnsureAvailableAsync(Uri synthesisEndpoint, CancellationToken ct)
    {
        if (!IsCapriEndpoint(synthesisEndpoint)) return;

        if (await IsPortOpenAsync(ct).ConfigureAwait(false)) return;

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Capri", "Voice");
        var python = Path.Combine(root, "venv", "Scripts", "python.exe");
        var server = Path.Combine(root, "server.py");
        var model = Path.Combine(root, "models", "kokoro-v1.0.int8.onnx");
        var voices = Path.Combine(root, "models", "voices-v1.0.bin");
        if (!File.Exists(python) || !File.Exists(server) ||
            !File.Exists(model) || !File.Exists(voices))
            return;

        await StartGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await IsPortOpenAsync(ct).ConfigureAwait(false)) return;
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = python,
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = "-u \"server.py\""
            });
            if (process is null) return;

            // First start imports the ONNX runtime and loads the voice model.
            // The stop token prevents waiting after the user cancels speaking.
            for (var attempt = 0; attempt < 48; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(250, ct).ConfigureAwait(false);
                if (await IsPortOpenAsync(ct).ConfigureAwait(false)) return;
                if (process.HasExited) return;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or
                                   InvalidOperationException)
        {
            // Speech still falls back to the built-in Windows voice.
        }
        finally
        {
            StartGate.Release();
        }
    }

    private static async Task<bool> IsPortOpenAsync(CancellationToken ct)
    {
        try
        {
            using var connection = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
            await connection.ConnectAsync("127.0.0.1", 5000, timeout.Token)
                .ConfigureAwait(false);
            return connection.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
            return false;
        }
    }
}
