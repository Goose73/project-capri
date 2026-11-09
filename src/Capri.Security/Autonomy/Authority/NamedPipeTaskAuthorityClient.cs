using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading.Channels;
using Capri.Core.Autonomy.Authority;

namespace Capri.Security.Autonomy.Authority;

public sealed class NamedPipeTaskAuthorityClient : ITaskAuthorityClient
{
    private readonly string _pipeName;
    private readonly string _clientName;
    private readonly bool _canApprove;
    private readonly TimeSpan _connectTimeout;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AuthorityResponse>> _pending = new();
    private readonly Channel<AuthorityEvent> _events = Channel.CreateUnbounded<AuthorityEvent>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private NamedPipeClientStream? _pipe;
    private Task? _reader;
    private CancellationTokenSource? _linkCts;
    private int _requestSeq;

    public NamedPipeTaskAuthorityClient(string pipeName, string clientName, bool canApprove, TimeSpan? connectTimeout = null)
    {
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        _clientName = clientName ?? "client";
        _canApprove = canApprove;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);
    }

    public static string CurrentUserPipeName() => TaskAuthorityProtocol.PipeNameForUserIdentifier(WindowsIdentity.GetCurrent().User?.Value ?? "nouser");

    public bool IsConnected => _pipe?.IsConnected == true;

    public async ValueTask EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return;
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected) return;
            await DisconnectCoreAsync().ConfigureAwait(false);
            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.WriteThrough);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_connectTimeout);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            _pipe = pipe;
            _linkCts = new CancellationTokenSource();
            _reader = Task.Run(() => ReadLoopAsync(pipe, _linkCts.Token), CancellationToken.None);
            var hello = await SendCoreAsync(new AuthorityRequest(NextId(), AuthorityRequestKind.Hello, ClientName: _clientName, CanApprove: _canApprove), cancellationToken).ConfigureAwait(false);
            if (!hello.Ok) throw new InvalidOperationException(hello.Error ?? "Authority hello failed.");
            var sub = await SendCoreAsync(new AuthorityRequest(NextId(), AuthorityRequestKind.Subscribe), cancellationToken).ConfigureAwait(false);
            if (!sub.Ok) throw new InvalidOperationException(sub.Error ?? "Authority subscribe failed.");
        }
        finally { _connectLock.Release(); }
    }

    public async Task<AuthorityResponse> SendAsync(AuthorityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AuthorityResponse> SendCoreAsync(AuthorityRequest request, CancellationToken ct)
    {
        var pipe = _pipe ?? throw new InvalidOperationException("Not connected.");
        var tcs = new TaskCompletionSource<AuthorityResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.RequestId] = tcs;
        using var reg = ct.Register(() => { if (_pending.TryRemove(request.RequestId, out var p)) p.TrySetCanceled(ct); });
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try { await JsonPipeFraming.WriteAsync(pipe, request, TaskAuthorityProtocol.Json, TaskAuthorityProtocol.MaxFrameBytes, ct).ConfigureAwait(false); }
        finally { _writeLock.Release(); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var treg = timeout.Token.Register(() => { if (_pending.TryRemove(request.RequestId, out var p) && !ct.IsCancellationRequested) p.TrySetException(new TimeoutException("Authority response timeout.")); });
        try { return await tcs.Task.ConfigureAwait(false); }
        finally { _pending.TryRemove(request.RequestId, out _); }
    }

    public IAsyncEnumerable<AuthorityEvent> Events(CancellationToken cancellationToken = default) => _events.Reader.ReadAllAsync(cancellationToken);

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var env = await JsonPipeFraming.ReadAsync<AuthorityEnvelope>(pipe, TaskAuthorityProtocol.Json, TaskAuthorityProtocol.MaxFrameBytes, ct).ConfigureAwait(false);
                if (env is null) break;
                if (env.Response is { } resp)
                {
                    if (_pending.TryRemove(resp.RequestId, out var tcs)) tcs.TrySetResult(resp);
                }
                else if (env.Event is { } evt)
                {
                    await _events.Writer.WriteAsync(evt, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { }
        finally
        {
            foreach (var kv in _pending) kv.Value.TrySetException(new IOException("Authority connection closed."));
            _pending.Clear();
            _events.Writer.TryComplete();
        }
    }

    public string NextId() => $"r{Environment.ProcessId}-{Interlocked.Increment(ref _requestSeq)}";

    private async Task DisconnectCoreAsync()
    {
        try { _linkCts?.Cancel(); } catch { }
        if (_reader is not null) try { await _reader.ConfigureAwait(false); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _linkCts?.Dispose();
        _linkCts = null; _reader = null; _pipe = null;
        foreach (var kv in _pending) kv.Value.TrySetException(new IOException("Disconnected."));
        _pending.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectCoreAsync().ConfigureAwait(false);
        _connectLock.Dispose();
        _writeLock.Dispose();
    }
}
