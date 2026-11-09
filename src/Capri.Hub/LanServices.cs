using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Capri.Hub;

public sealed record Reachability(bool? Online, string? Warning);

public interface IPcProbe
{
    Task<Reachability> CheckAsync(IPAddress address, CancellationToken cancellationToken);
}

/// <summary>Single bounded ICMP check; unknown is distinct from offline.</summary>
public sealed class PingPcProbe : IPcProbe
{
    public async Task<Reachability> CheckAsync(IPAddress address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(address, 1500)
                .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);
            return new Reachability(reply.Status == IPStatus.Success, null);
        }
        catch (PingException)
        {
            return new Reachability(null, "ICMP probe unavailable; check network and container permissions.");
        }
        catch (TimeoutException)
        {
            return new Reachability(null, "ICMP probe timed out without a usable result.");
        }
    }
}

public interface IWakeSender
{
    Task SendAsync(PhysicalAddress mac, IPAddress broadcast, CancellationToken cancellationToken);
}

/// <summary>
/// Optional one-shot magic packet sender. No auto-wake or network listener.
/// Dispatching a UDP packet does not establish that the target booted.
/// </summary>
public sealed class UdpWakeSender : IWakeSender
{
    public static byte[] CreatePacket(PhysicalAddress mac)
    {
        ArgumentNullException.ThrowIfNull(mac);
        var address = mac.GetAddressBytes();
        if (address.Length != 6) throw new ArgumentException("Expected six-byte MAC.", nameof(mac));
        var packet = new byte[102];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (var i = 0; i < 16; i++)
            Buffer.BlockCopy(address, 0, packet, 6 + i * 6, 6);
        return packet;
    }

    public async Task SendAsync(PhysicalAddress mac, IPAddress broadcast, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broadcast);
        cancellationToken.ThrowIfCancellationRequested();
        using var socket = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        var payload = CreatePacket(mac);
        _ = await socket.SendAsync(payload, payload.Length, new IPEndPoint(broadcast, 9))
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
