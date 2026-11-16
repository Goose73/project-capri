using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Capri.Cli.Commands;

/// <summary>One physical adapter's GPU memory. Free memory changes during runtime.</summary>
public sealed record GpuMemoryProbeResult(
    string? Name,
    double? DedicatedVramGb,
    double? FreeVramGb,
    string? Source,
    string? Note);

/// <summary>
/// Use the NVIDIA driver to report real 64-bit dedicated memory rather than
/// the 32-bit Win32_VideoController.AdapterRAM value, which can truncate.
/// Read-only and invoked only after the user's hardware-inspection consent.
/// </summary>
public static class GpuHardwareProbe
{
    public static GpuMemoryProbeResult Detect()
    {
        if (!OperatingSystem.IsWindows())
            return new(null, null, null, null, "Windows GPU inspection is unavailable.");

        foreach (var path in KnownNvidiaSmiPaths())
        {
            if (!File.Exists(path)) continue;
            var result = ParseNvidiaSmi(RunProcess(
                path, "--query-gpu=name,memory.total,memory.free --format=csv,noheader,nounits",
                TimeSpan.FromSeconds(4)));
            if (result is not null) return result;
        }

        // Never select just the first WMI adapter: remote/virtual monitors
        // often appear before the actual physical GPU.
        var output = RunProcess("powershell",
            "-NoProfile -NonInteractive -Command \"Get-CimInstance Win32_VideoController | Select-Object Name,AdapterRAM,PNPDeviceID | ConvertTo-Json -Compress\"",
            TimeSpan.FromSeconds(5));
        return ParseWindowsVideoControllers(output);
    }

    public static GpuMemoryProbeResult? ParseNvidiaSmi(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        var devices = new List<GpuMemoryProbeResult>();
        foreach (var line in data.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0]) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var total) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var free) ||
                !double.IsFinite(total) || !double.IsFinite(free) ||
                total < 128 || total > 1_048_576 || free < 0 || free > total)
                continue;

            // NVIDIA reports MiB. Divide by 1024 to return GiB.
            devices.Add(new(parts[0], total / 1024d, free / 1024d,
                "NVIDIA driver (nvidia-smi)", null));
        }

        // A single model uses one adapter's VRAM, not the sum of all GPUs.
        return devices.OrderByDescending(d => d.DedicatedVramGb).FirstOrDefault();
    }

    public static GpuMemoryProbeResult ParseWindowsVideoControllers(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return new(null, null, null, null, "GPU information could not be measured.");
        try
        {
            using var json = JsonDocument.Parse(data);
            var root = json.RootElement;
            var elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToArray()
                : root.ValueKind == JsonValueKind.Object ? [root] : [];

            var adapters = new List<(string Name, ulong? VramBytes)>();
            foreach (var entry in elements)
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("Name", out var nameProperty) ||
                    nameProperty.ValueKind != JsonValueKind.String) continue;

                var name = nameProperty.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(name) ||
                    name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Indirect", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("DisplayLink", StringComparison.OrdinalIgnoreCase)) continue;

                var pnp = entry.TryGetProperty("PNPDeviceID", out var pnpProperty) &&
                    pnpProperty.ValueKind == JsonValueKind.String ? pnpProperty.GetString() : null;
                if (!string.IsNullOrWhiteSpace(pnp) &&
                    pnp.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase)) continue;

                ulong? bytes = null;
                if (entry.TryGetProperty("AdapterRAM", out var ram) &&
                    ram.ValueKind == JsonValueKind.Number &&
                    ram.TryGetUInt64(out var measured) && measured > 0)
                    bytes = measured;
                adapters.Add((name, bytes));
            }

            if (adapters.Count == 0)
                return new(null, null, null, null, "Only virtual/unidentifiable adapters were found.");

            var best = adapters.OrderByDescending(a => a.VramBytes ?? 0).First();
            // RTX 3050 6 GB, for example, can report 4,293,918,720 bytes
            // through this 32-bit property. Do NOT call that 4 GB of VRAM.
            const ulong nearFourGiB = 4UL * 1024 * 1024 * 1024 - 16UL * 1024 * 1024;
            if (best.VramBytes is not { } memory || memory >= nearFourGiB)
                return new(best.Name, null, null, "Windows CIM (GPU identity)",
                    "WMI VRAM may be truncated. Dedicated VRAM cannot be verified without a vendor or 64-bit GPU memory API.");

            if (best.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
                (best.Name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
                 best.Name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
                 best.Name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase)))
                return new(best.Name, null, null, "Windows CIM (integrated GPU)",
                    "Integrated graphics may share system RAM; dedicated VRAM is unverified.");

            return new(best.Name, memory / (1024d * 1024d * 1024d), null,
                "Windows CIM (unverified VRAM)",
                "Windows WMI AdapterRAM can be truncated at 4 GiB. Verify with your GPU driver.");
        }
        catch (JsonException)
        {
            return new(null, null, null, null, "GPU information could not be parsed.");
        }
    }

    private static IEnumerable<string> KnownNvidiaSmiPaths()
    {
        // Restrict execution to established driver directories, not user input.
        yield return Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
    }

    private static string? RunProcess(string executable, string arguments, TimeSpan timeout)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable, Arguments = arguments,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };
            process.Start();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
