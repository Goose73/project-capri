using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Capri.Hub;

/// <summary>
/// Pi-side configuration. No defaults for device identity or network destinations:
/// operators must configure their own LAN addresses and MAC locally.
/// </summary>
public sealed record HubSettings(
    IPAddress PcAddress,
    PhysicalAddress? WakeMac,
    IPAddress? WakeBroadcast,
    int PollSeconds)
{
    public bool CanWake => WakeMac is not null && WakeBroadcast is not null;

    public static HubSettings Load(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var pc = ParseLocalAddress(environment("CAPRI_PC_IP"), "CAPRI_PC_IP", allowGlobalBroadcast: false);
        var macText = environment("CAPRI_WOL_MAC");
        PhysicalAddress? mac = null;
        if (!string.IsNullOrWhiteSpace(macText))
        {
            if (!PhysicalAddress.TryParse(macText.Trim(), out mac) || mac is null ||
                mac.GetAddressBytes().Length != 6)
                throw new ArgumentException("CAPRI_WOL_MAC must be a six-byte Ethernet MAC address.");
        }

        var broadcastText = environment("CAPRI_WOL_BROADCAST");
        IPAddress? broadcast = null;
        if (!string.IsNullOrWhiteSpace(broadcastText))
            broadcast = ParseLocalAddress(broadcastText, "CAPRI_WOL_BROADCAST", allowGlobalBroadcast: true);

        var intervalText = environment("CAPRI_POLL_SECONDS");
        var interval = 60;
        if (!string.IsNullOrWhiteSpace(intervalText) &&
            (!int.TryParse(intervalText, NumberStyles.None, CultureInfo.InvariantCulture, out interval)
             || interval is < 10 or > 3600))
            throw new ArgumentException("CAPRI_POLL_SECONDS must be between 10 and 3600.");

        return new HubSettings(pc, mac, broadcast, interval);
    }

    private static IPAddress ParseLocalAddress(string? text, string name, bool allowGlobalBroadcast)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !IPAddress.TryParse(text.Trim(), out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException(name + " must be an IPv4 address.");

        var b = address.GetAddressBytes();
        var privateAddress = b[0] == 10 ||
                             (b[0] == 172 && b[1] is >= 16 and <= 31) ||
                             (b[0] == 192 && b[1] == 168) ||
                             (b[0] == 100 && b[1] is >= 64 and <= 127);
        var globalBroadcast = address.Equals(IPAddress.Broadcast);
        if (!privateAddress && !(allowGlobalBroadcast && globalBroadcast))
            throw new ArgumentException(name + " must be a private LAN/CGNAT IPv4 address" +
                                        (allowGlobalBroadcast ? " or 255.255.255.255." : "."));

        return address;
    }
}
