using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Capri.Hub;

/// <summary>
/// Deliberately local-only operator CLI. No server, listener, task dispatch,
/// remote approval, AI provider, or user-account credential handling.
/// </summary>
public static class HubCommands
{
    public static async Task<int> RunAsync(
        string[] args,
        Func<string, string?> environment,
        IPcProbe probe,
        IWakeSender wakeSender,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(wakeSender);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            output.WriteLine("Capri Hub (experimental): doctor|status|watch [--json], wake --confirm");
            output.WriteLine("Configure CAPRI_PC_IP; optionally CAPRI_WOL_MAC and CAPRI_WOL_BROADCAST.");
            output.WriteLine("No network listener is started. Wake requires explicit --confirm.");
            return 0;
        }

        var command = args[0];
        var flags = args.Skip(1).ToArray();
        var json = flags.Contains("--json", StringComparer.Ordinal);
        var confirm = flags.Contains("--confirm", StringComparer.Ordinal);
        if (command is not ("doctor" or "status" or "watch" or "wake") ||
            flags.Length != flags.Distinct(StringComparer.Ordinal).Count() ||
            flags.Any(f => f is not ("--json" or "--confirm")) ||
            (confirm && command != "wake") ||
            (json && command is not ("doctor" or "status")))
        {
            error.WriteLine("Unsupported command or option. Run capri-hub help.");
            return 2;
        }

        HubSettings settings;
        try
        {
            settings = HubSettings.Load(environment);
        }
        catch (ArgumentException ex)
        {
            error.WriteLine("Capri Hub configuration: " + ex.Message);
            return 2;
        }

        switch (command)
        {
            case "doctor":
                if (json)
                {
                    output.WriteLine(JsonSerializer.Serialize(new
                    {
                        ok = true,
                        pcIp = settings.PcAddress.ToString(),
                        wakeConfigured = settings.CanWake,
                        pollSeconds = settings.PollSeconds,
                        listenerEnabled = false,
                    }));
                }
                else
                {
                    output.WriteLine("PC LAN target: " + settings.PcAddress);
                    output.WriteLine("Wake-on-LAN configured: " + (settings.CanWake ? "yes" : "no"));
                    output.WriteLine("Poll interval: " + settings.PollSeconds.ToString(CultureInfo.InvariantCulture) + "s");
                    output.WriteLine("Network listener: disabled");
                }
                return 0;

            case "status":
                await WriteStatusAsync(settings.PcAddress, probe, output, json, cancellationToken)
                    .ConfigureAwait(false);
                return 0;

            case "watch":
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await WriteStatusAsync(settings.PcAddress, probe, output, json: true, cancellationToken)
                        .ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(settings.PollSeconds), cancellationToken)
                        .ConfigureAwait(false);
                }

            case "wake":
                if (!confirm)
                {
                    error.WriteLine("No packet sent. Explicitly run: capri-hub wake --confirm");
                    return 2;
                }
                if (!settings.CanWake)
                {
                    error.WriteLine("No packet sent. Configure both CAPRI_WOL_MAC and CAPRI_WOL_BROADCAST.");
                    return 2;
                }
                await wakeSender.SendAsync(settings.WakeMac!, settings.WakeBroadcast!, cancellationToken)
                    .ConfigureAwait(false);
                output.WriteLine("Wake packet dispatched; PC power-on has not been verified.");
                return 0;
        }

        return 2;
    }

    private static async Task WriteStatusAsync(
        IPAddress pc,
        IPcProbe probe,
        TextWriter output,
        bool json,
        CancellationToken cancellationToken)
    {
        var result = await probe.CheckAsync(pc, cancellationToken).ConfigureAwait(false);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                checkedUtc = DateTimeOffset.UtcNow,
                pcIp = pc.ToString(),
                online = result.Online,
                warning = result.Warning,
            }));
        }
        else
        {
            var state = result.Online switch { true => "online", false => "offline", null => "unknown" };
            output.WriteLine("PC " + pc + ": " + state +
                             (result.Warning is null ? string.Empty : " (" + result.Warning + ")"));
        }
    }
}
