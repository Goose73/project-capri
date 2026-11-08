using System.Diagnostics;
using System.Net;
using System.Net.Http;

namespace Capri.Cli.Services;

/// <summary>
/// Default local endpoint probe. Proxy use is disabled so a system proxy cannot
/// make a loopback provider look unreachable. No credentials are sent.
/// </summary>
public sealed class HttpEndpointProbe : IEndpointProbe
{
    /// <inheritdoc />
    public async Task<EndpointProbeResult> ProbeAsync(
        Uri baseUri,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        cancellationToken.ThrowIfCancellationRequested();

        var modelsUri = BuildModelsUri(baseUri);
        var effectiveTimeout = timeout <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(1000)
            : timeout;
        var stopwatch = Stopwatch.StartNew();

        using var handler = new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
        };
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(effectiveTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);
            stopwatch.Stop();

            var status = (int)response.StatusCode;
            var reason = SanitizeReason(response.ReasonPhrase);
            var detail = response.IsSuccessStatusCode
                ? $"HTTP {status}"
                : string.IsNullOrEmpty(reason)
                    ? $"HTTP {status}"
                    : $"HTTP {status} {reason}";

            return new EndpointProbeResult(
                Reachable: true,
                Detail: detail,
                Elapsed: stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new EndpointProbeResult(
                Reachable: false,
                Detail: $"timed out after {effectiveTimeout.TotalMilliseconds:0} ms",
                Elapsed: stopwatch.Elapsed);
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();
            var detail = exception.StatusCode is HttpStatusCode status
                ? $"connection failed (HTTP {(int)status})"
                : "connection failed";
            return new EndpointProbeResult(
                Reachable: false,
                Detail: detail,
                Elapsed: stopwatch.Elapsed);
        }
        catch (IOException)
        {
            stopwatch.Stop();
            return new EndpointProbeResult(
                Reachable: false,
                Detail: "connection failed",
                Elapsed: stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Builds the models endpoint for an OpenAI-compatible base URL. Handles
    /// base URLs that already end in <c>/models</c> or
    /// <c>/chat/completions</c>.
    /// </summary>
    public static Uri BuildModelsUri(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        var path = baseUri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/chat/completions".Length];
        }

        if (!path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            path = string.Concat(path, "/models");
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = path.Length == 0 ? "/models" : path,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri;
    }

    private static string SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        var characters = reason
            .Where(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_')
            .Take(40)
            .ToArray();
        return new string(characters).Trim();
    }
}
