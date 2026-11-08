namespace Capri.Cli.Services;

/// <summary>Result of a best-effort endpoint reachability probe.</summary>
public sealed record EndpointProbeResult(
    bool Reachable,
    string Detail,
    TimeSpan Elapsed);

/// <summary>
/// Performs a credential-free GET against a local provider's <c>/models</c>
/// endpoint. Implementations must never attach API keys or other secrets.
/// </summary>
public interface IEndpointProbe
{
    /// <summary>Probes the supplied base URL with a short timeout.</summary>
    Task<EndpointProbeResult> ProbeAsync(
        Uri baseUri,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
