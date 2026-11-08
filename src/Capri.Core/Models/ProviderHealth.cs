namespace Capri.Core.Models;

/// <summary>
/// Result of a provider health probe.
/// </summary>
/// <param name="IsHealthy">True when the provider responded successfully.</param>
/// <param name="Message">Optional diagnostic detail.</param>
/// <param name="Latency">Optional round-trip time for the probe.</param>
public sealed record ProviderHealth(
    bool IsHealthy,
    string? Message = null,
    TimeSpan? Latency = null);
