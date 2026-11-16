using Capri.Cli.Commands;

namespace Capri.Tests.Cli;

public sealed class GpuHardwareProbeTests
{
    [Fact]
    public void ReadsNvidiaDriverMemoryInMiBInsteadOfWrappedWmiBytes()
    {
        var result = GpuHardwareProbe.ParseNvidiaSmi("NVIDIA GeForce RTX 3050, 6144, 1707\r\n");

        Assert.NotNull(result);
        Assert.Equal("NVIDIA GeForce RTX 3050", result.Name);
        Assert.Equal(6d, result.DedicatedVramGb);
        Assert.Equal(1707d / 1024, result.FreeVramGb);
        Assert.Equal("NVIDIA driver (nvidia-smi)", result.Source);
    }

    [Fact]
    public void ChoosesLargestSingleAdapterWithoutSummingGpuMemory()
    {
        var result = GpuHardwareProbe.ParseNvidiaSmi(
            "NVIDIA RTX A1000, 4096, 3000\nNVIDIA RTX 3050, 6144, 2100\n");

        Assert.NotNull(result);
        Assert.Equal("NVIDIA RTX 3050", result.Name);
        Assert.Equal(6d, result.DedicatedVramGb);
    }

    [Theory]
    [InlineData("NVIDIA RTX, N/A, N/A")]
    [InlineData("NVIDIA RTX, 6144, 9000")]
    [InlineData("NVIDIA RTX, -1, 0")]
    [InlineData("")]
    public void RejectsInvalidVendorMeasurements(string input)
    {
        Assert.Null(GpuHardwareProbe.ParseNvidiaSmi(input));
    }

    [Fact]
    public void WmiFallbackPrefersRealAdapterOverFirstVirtualMonitor()
    {
        const string raw = """
            [
                {"Name":"Meta Virtual Monitor","AdapterRAM":null,"PNPDeviceID":"ROOT\\DISPLAY\\0000"},
                {"Name":"NVIDIA GeForce RTX 3050","AdapterRAM":4293918720,"PNPDeviceID":"PCI\\VEN_10DE"}
            ]
            """;

        var result = GpuHardwareProbe.ParseWindowsVideoControllers(raw);

        Assert.Equal("NVIDIA GeForce RTX 3050", result.Name);
        Assert.Null(result.DedicatedVramGb);
        Assert.Contains("truncated", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WmiFallbackCanShowOnlyVerifiedAdapterIdentityWhenMemoryUnknown()
    {
        const string raw = """
            {"Name":"AMD Radeon RX 6800","AdapterRAM":null,"PNPDeviceID":"PCI\\VEN_1002"}
            """;

        var result = GpuHardwareProbe.ParseWindowsVideoControllers(raw);

        Assert.Equal("AMD Radeon RX 6800", result.Name);
        Assert.Null(result.DedicatedVramGb);
        Assert.NotNull(result.Note);
    }

    [Fact]
    public void RejectsVirtualOnlyAndMalformedWmiData()
    {
        Assert.Null(GpuHardwareProbe.ParseWindowsVideoControllers(
            """{"Name":"Meta Virtual Monitor","PNPDeviceID":"ROOT\\DISPLAY"}""").Name);
        Assert.Null(GpuHardwareProbe.ParseWindowsVideoControllers("{not-json").DedicatedVramGb);
    }

    [Fact]
    public void PreservesPlausibleSmallDiscreteWmiEstimateButLabelsItUnverified()
    {
        const string raw = """
            {"Name":"AMD Radeon RX 570","AdapterRAM":2147483648,"PNPDeviceID":"PCI\\VEN_1002"}
            """;

        var result = GpuHardwareProbe.ParseWindowsVideoControllers(raw);

        Assert.Equal(2d, result.DedicatedVramGb);
        Assert.Contains("unverified", result.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.FreeVramGb);
    }
}
