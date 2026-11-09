using System.Diagnostics;
using Capri.Core.Autonomy.Authority;
using Capri.Security.Autonomy.Authority;

namespace Capri.Cli.Services;

internal static class TaskAuthorityConnector
{
    public static async Task<NamedPipeTaskAuthorityClient?> TryConnectAsync(string clientName, bool canApprove, TimeSpan timeout, CancellationToken ct)
    {
        var pipeName = NamedPipeTaskAuthorityClient.CurrentUserPipeName();
        var client = new NamedPipeTaskAuthorityClient(pipeName, clientName, canApprove, connectTimeout: TimeSpan.FromMilliseconds(750));
        try
        {
            await client.EnsureConnectedAsync(ct).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        // Try to start authority host process.
        try
        {
            StartAuthorityHost();
        }
        catch { return null; }

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout && !ct.IsCancellationRequested)
        {
            var retry = new NamedPipeTaskAuthorityClient(pipeName, clientName, canApprove, connectTimeout: TimeSpan.FromMilliseconds(750));
            try
            {
                await retry.EnsureConnectedAsync(ct).ConfigureAwait(false);
                return retry;
            }
            catch
            {
                await retry.DisposeAsync().ConfigureAwait(false);
            }
            try { await Task.Delay(200, ct).ConfigureAwait(false); } catch { break; }
        }
        return null;
    }

    private static void StartAuthorityHost()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe)) return;
        // For `dotnet run` scenarios, ProcessPath may be dotnet; prefer the apphost next to current assembly.
        var appHost = Path.ChangeExtension(typeof(TaskAuthorityConnector).Assembly.Location, ".exe");
        var file = File.Exists(appHost) ? appHost : exe;
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = Environment.CurrentDirectory
        };
        if (string.Equals(Path.GetFileName(file), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Unusual fallback; avoid if possible.
            return;
        }
        psi.ArgumentList.Add("authority");
        psi.ArgumentList.Add("serve");
        Process.Start(psi);
    }
}
