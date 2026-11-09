using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Capri.Hub;

namespace Capri.Hub.Tests;

public sealed class HubTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] items)
    {
        var values = items.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return name => values.GetValueOrDefault(name);
    }

    [Fact]
    public void ConfigurationHasNoImplicitDeviceOrWakeDestination()
    {
        Assert.Throws<ArgumentException>(() => HubSettings.Load(Env()));
        var settings = HubSettings.Load(Env(("CAPRI_PC_IP", "192.168.1.100")));
        Assert.Equal(IPAddress.Parse("192.168.1.100"), settings.PcAddress);
        Assert.Equal(60, settings.PollSeconds);
        Assert.False(settings.CanWake);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("192.168.1.100/24")]
    public void RejectsNonLanAndInvalidAddresses(string address)
    {
        Assert.Throws<ArgumentException>(() =>
            HubSettings.Load(Env(("CAPRI_PC_IP", address))));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("3601")]
    [InlineData("abc")]
    public void RejectsInvalidPollingInterval(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            HubSettings.Load(Env(("CAPRI_PC_IP", "192.168.1.100"), ("CAPRI_POLL_SECONDS", value))));
    }

    [Fact]
    public void WakePacketHasCorrectHeaderAndSixteenMacRepetitions()
    {
        var mac = PhysicalAddress.Parse("D8-43-AE-BE-F1-38");
        var packet = UdpWakeSender.CreatePacket(mac);
        Assert.Equal(102, packet.Length);
        Assert.All(packet.Take(6), b => Assert.Equal((byte)0xFF, b));
        var expected = mac.GetAddressBytes();
        for (var i = 0; i < 16; i++)
            Assert.Equal(expected, packet.Skip(6 + i * 6).Take(6).ToArray());
    }

    [Fact]
    public async Task StatusReportsOfflineWithoutPretendingItIsAnError()
    {
        var output = new StringWriter();
        var code = await HubCommands.RunAsync(
            ["status", "--json"], Env(("CAPRI_PC_IP", "192.168.1.100")),
            new StubProbe(new Reachability(false, null)), new StubWake(),
            output, new StringWriter(), CancellationToken.None);
        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("online").GetBoolean());
        Assert.Equal("192.168.1.100", json.RootElement.GetProperty("pcIp").GetString());
    }

    [Fact]
    public async Task UnavailableProbeReportsUnknownNotOffline()
    {
        var output = new StringWriter();
        var code = await HubCommands.RunAsync(
            ["status", "--json"], Env(("CAPRI_PC_IP", "192.168.1.100")),
            new StubProbe(new Reachability(null, "ICMP unavailable")), new StubWake(),
            output, new StringWriter(), CancellationToken.None);
        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("online").ValueKind);
    }

    [Fact]
    public async Task WakeRequiresExplicitConfirmation()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var sender = new StubWake();
        var config = Env(
            ("CAPRI_PC_IP", "192.168.1.100"),
            ("CAPRI_WOL_MAC", "D8-43-AE-BE-F1-38"),
            ("CAPRI_WOL_BROADCAST", "192.168.1.255"));
        var code = await HubCommands.RunAsync(
            ["wake"], config, new StubProbe(new Reachability(false, null)),
            sender, output, error, CancellationToken.None);
        Assert.Equal(2, code);
        Assert.Equal(0, sender.Sends);

        code = await HubCommands.RunAsync(
            ["wake", "--confirm"], config, new StubProbe(new Reachability(false, null)),
            sender, output, error, CancellationToken.None);
        Assert.Equal(0, code);
        Assert.Equal(1, sender.Sends);
        Assert.Contains("not been verified", output.ToString());
    }

    [Fact]
    public async Task RejectsUnsupportedOptionsBeforeAnyNetworkAction()
    {
        var probe = new CountingProbe();
        var sender = new StubWake();
        var code = await HubCommands.RunAsync(
            ["status", "--confirm"], Env(("CAPRI_PC_IP", "192.168.1.100")),
            probe, sender, new StringWriter(), new StringWriter(), CancellationToken.None);
        Assert.Equal(2, code);
        Assert.Equal(0, probe.Checks);
        Assert.Equal(0, sender.Sends);
    }

    private sealed class StubProbe(Reachability result) : IPcProbe
    {
        public Task<Reachability> CheckAsync(IPAddress address, CancellationToken cancellationToken)
            => Task.FromResult(result);
    }

    private sealed class CountingProbe : IPcProbe
    {
        public int Checks { get; private set; }

        public Task<Reachability> CheckAsync(IPAddress address, CancellationToken cancellationToken)
        {
            Checks++;
            return Task.FromResult(new Reachability(true, null));
        }
    }

    private sealed class StubWake : IWakeSender
    {
        public int Sends { get; private set; }

        public Task SendAsync(PhysicalAddress mac, IPAddress broadcast, CancellationToken cancellationToken)
        {
            Sends++;
            return Task.CompletedTask;
        }
    }
}
