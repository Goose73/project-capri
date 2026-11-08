using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Capri.Tests.TestDoubles;

/// <summary>
/// Test double that records requests and returns canned responses.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _handler;
    private readonly object _lock = new();
    private readonly List<RecordedRequest> _requests = [];
    private int _callCount;

    public FakeHttpMessageHandler(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : this((request, _, cancellationToken) => handler(request, cancellationToken))
    {
    }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return _requests.ToArray();
            }
        }
    }

    public int RequestCount => Requests.Count;

    public static FakeHttpMessageHandler FromFactory(Func<HttpResponseMessage> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);
        return new FakeHttpMessageHandler((_, _, _) => Task.FromResult(responseFactory()));
    }

    public static FakeHttpMessageHandler Json(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return FromFactory(() => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    public static FakeHttpMessageHandler ServerSentEvents(string ssePayload)
    {
        return FromFactory(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream"),
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var callIndex = Interlocked.Increment(ref _callCount) - 1;

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var authorization = request.Headers.Authorization?.ToString();
        var accept = request.Headers.Accept.ToString();
        var uri = request.RequestUri;

        lock (_lock)
        {
            _requests.Add(new RecordedRequest(
                request.Method,
                uri,
                body,
                authorization,
                accept));
        }

        return await _handler(request, callIndex, cancellationToken);
    }
}

/// <summary>
/// Snapshot of a request captured before the request is disposed.
/// </summary>
public sealed record RecordedRequest(
    HttpMethod Method,
    Uri? RequestUri,
    string? Body,
    string? Authorization,
    string? Accept);
