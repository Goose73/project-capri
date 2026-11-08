using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using Capri.Cli;
using Capri.Cli.Services;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

/// <summary>Deterministic console double that records output and replays input.</summary>
public sealed class FakeCliConsole : ICliConsole
{
    private readonly Queue<string?> _input;
    private readonly StringBuilder _out = new();
    private readonly StringBuilder _error = new();

    public FakeCliConsole(IEnumerable<string?>? input = null, bool interactive = false)
    {
        _input = new Queue<string?>(input ?? Array.Empty<string?>());
        IsInputRedirected = !interactive;
        IsOutputRedirected = !interactive;
    }

    public bool IsInputRedirected { get; }

    public bool IsOutputRedirected { get; }

    public bool IsInteractive => !IsInputRedirected && !IsOutputRedirected;

    public string Out => _out.ToString();

    public string Error => _error.ToString();

    public void Write(string text) => _out.Append(text);

    public void WriteLine(string text) => _out.AppendLine(text);

    public void WriteErrorLine(string text) => _error.AppendLine(text);

    public string? ReadLine() => _input.Count == 0 ? null : _input.Dequeue();

    public void Clear()
    {
        // Clear is intentionally a no-op so assertions can inspect the transcript.
    }
}

/// <summary>Fake CLI service composition backed by in-memory stores.</summary>
public sealed class StubCliServices : ICliServices
{
    public StubCliServices(
        ICapriPaths paths,
        IAppSettingsStore? settings = null,
        ISecureCredentialStore? credentials = null,
        IChatProvider? chatProvider = null,
        IModelRouter? modelRouter = null,
        IOllamaClient? ollamaClient = null)
    {
        Paths = paths;
        Settings = settings ?? new FakeAppSettingsStore();
        Credentials = credentials ?? new FakeCredentialStore();
        ChatProvider = chatProvider;
        ModelRouter = modelRouter;
        OllamaClient = ollamaClient;
    }

    public ICapriPaths Paths { get; }

    public IAppSettingsStore Settings { get; }

    public ISecureCredentialStore Credentials { get; }

    public IChatProvider? ChatProvider { get; set; }

    public IModelRouter? ModelRouter { get; set; }

    public IOllamaClient? OllamaClient { get; set; }

    public IHttpClientFactory? HttpClientFactory { get; set; }

    public static StubCliServices CreateInTemp(
        string tempPath,
        AppSettings? settings = null,
        ISecureCredentialStore? credentials = null)
    {
        return new StubCliServices(
            new CapriPaths(tempPath),
            new FakeAppSettingsStore(settings),
            credentials);
    }

    public void Dispose()
    {
    }
}

/// <summary>Deterministic chat provider double.</summary>
public sealed class StubChatProvider : IChatProvider
{
    public string ProviderId { get; init; } = "stub";
    public ChatCompletion? CompletionResult { get; set; }
    public Exception? ThrowOnComplete { get; set; }
    public Exception? ThrowOnStream { get; set; }
    public IReadOnlyList<ChatDelta>? StreamDeltas { get; set; }
    public ChatRequest? LastRequest { get; private set; }
    public int CompleteCalls { get; private set; }
    public int StreamCalls { get; private set; }

    public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequest = request;
        CompleteCalls++;
        if (ThrowOnComplete is not null)
        {
            throw ThrowOnComplete;
        }

        return Task.FromResult(CompletionResult
            ?? new ChatCompletion(new ChatMessage(ChatRole.Assistant, "stub response")));
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequest = request;
        StreamCalls++;
        if (ThrowOnStream is not null)
        {
            throw ThrowOnStream;
        }

        var deltas = StreamDeltas ?? [new ChatDelta("stub response")];
        foreach (var delta in deltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return delta;
        }
    }

    public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ProviderHealth(true, "ok"));
    }
}

/// <summary>Deterministic model router double.</summary>
public sealed class StubModelRouter : IModelRouter
{
    public StubModelRouter(string providerId = "ollama", string modelId = "qwen3.5:4b", bool supportsStreaming = true)
    {
        Route = new ModelRoute(providerId, modelId, supportsStreaming);
    }

    public ModelRoute Route { get; set; }

    public ValueTask<ModelRoute> ResolveAsync(ModelRouteRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Route);
    }
}

/// <summary>Deterministic Ollama API double.</summary>
public sealed class StubOllamaClient : IOllamaClient
{
    public IReadOnlyList<OllamaModelInfo> Models { get; set; } = [];
    public Exception? ThrowOnList { get; set; }
    public Exception? ThrowOnExists { get; set; }
    public Exception? ThrowOnPull { get; set; }
    public IReadOnlyList<OllamaPullProgress>? PullProgress { get; set; }
    public Func<string, bool>? ExistsPredicate { get; set; }
    public string? LastPullModel { get; private set; }
    public int PullCalls { get; private set; }

    public Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ThrowOnList is not null)
        {
            throw ThrowOnList;
        }

        return Task.FromResult(Models);
    }

    public Task<bool> ModelExistsAsync(string baseUrl, string modelName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ThrowOnExists is not null)
        {
            throw ThrowOnExists;
        }

        if (ExistsPredicate is not null)
        {
            return Task.FromResult(ExistsPredicate(modelName));
        }

        return Task.FromResult(Models.Any(m => string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase)));
    }

    public async IAsyncEnumerable<OllamaPullProgress> PullModelAsync(
        string baseUrl,
        string modelName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastPullModel = modelName;
        PullCalls++;
        if (ThrowOnPull is not null)
        {
            throw ThrowOnPull;
        }

        foreach (var progress in PullProgress ?? [new OllamaPullProgress("success")])
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return progress;
        }
    }
}

/// <summary>Endpoint probe double.</summary>
public sealed class StubEndpointProbe : IEndpointProbe
{
    private readonly Func<Uri, EndpointProbeResult> _handler;

    public StubEndpointProbe(Func<Uri, EndpointProbeResult> handler)
    {
        _handler = handler;
    }

    public StubEndpointProbe(bool reachable)
        : this(_ => new EndpointProbeResult(
            reachable,
            reachable ? "HTTP 200" : "connection failed",
            TimeSpan.FromMilliseconds(5)))
    {
    }

    public List<Uri> ProbedUris { get; } = [];

    public Task<EndpointProbeResult> ProbeAsync(
        Uri baseUri,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProbedUris.Add(baseUri);
        return Task.FromResult(_handler(baseUri));
    }
}

/// <summary>Desktop launcher double.</summary>
public sealed class StubDesktopLauncher : IDesktopLauncher
{
    private readonly DesktopLaunchResult _result;

    public StubDesktopLauncher(DesktopLaunchResult? result = null)
    {
        _result = result ?? new DesktopLaunchResult(true, "Launching the built desktop app.");
    }

    public int LaunchCount { get; private set; }

    public DesktopLaunchResult Launch()
    {
        LaunchCount++;
        return _result;
    }
}

/// <summary>DPAPI probe double.</summary>
public sealed class StubDpapiProbe : IDpapiAvailabilityProbe
{
    private readonly DpapiAvailability _result;

    public StubDpapiProbe(bool available = true)
    {
        _result = available
            ? new DpapiAvailability(true, "DPAPI is available.")
            : new DpapiAvailability(false, "DPAPI is unavailable.");
    }

    public DpapiAvailability Check() => _result;
}
