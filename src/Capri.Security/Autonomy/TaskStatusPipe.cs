using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Capri.Core.Autonomy;

namespace Capri.Security.Autonomy;

/// <summary>
/// Version-one local desktop status IPC. The only accepted request is a fixed
/// single-byte read-only status query; it cannot approve, stop, reset, or run tasks.
/// CurrentUserOnly restricts access to the matching Windows user account.
/// This is not a boundary against untrusted code already running as that user.
/// </summary>
public static class TaskStatusPipe
{
    private const byte StatusQuery = 1;
    private const int ProtocolVersion = 1;
    private const int MaxResponseBytes = 2048;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string CurrentUserPipeName()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Capri desktop IPC only runs on Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new UnauthorizedAccessException("Windows user identity unavailable.");
        return "capri-task-status-v1-" + sid;
    }

    public static async Task ServeAsync(
        ITaskControl control,
        CancellationToken cancellationToken,
        string? pipeName = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        var name = pipeName ?? CurrentUserPipeName();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Capri desktop IPC only runs on Windows.");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Never listen on TCP, HTTP, Tailscale, or a cross-user pipe.
            using var server = new NamedPipeServerStream(
                name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            request.CancelAfter(RequestTimeout);
            try
            {
                await ProcessOneAsync(server, control, request.Token).ConfigureAwait(false);
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                // A client disconnected or supplied a truncated request. No dispatch.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // An idle or stalled client exceeded its short request deadline.
            }
        }
    }

    private static async Task ProcessOneAsync(
        PipeStream server,
        ITaskControl control,
        CancellationToken cancellationToken)
    {
        var request = new byte[1];
        await server.ReadExactlyAsync(request, cancellationToken).ConfigureAwait(false);
        if (request[0] != StatusQuery)
        {
            // Unknown operations are rejected without any response or state mutation.
            return;
        }

        var active = control.GetActive();
        var stop = control.GetStopState();
        var status = new TaskDesktopStatus(
            ProtocolVersion,
            DateTimeOffset.UtcNow,
            stop.Stopped,
            stop.Generation,
            active?.TaskId,
            active?.ProcessId);
        var response = JsonSerializer.SerializeToUtf8Bytes(status, JsonOptions);
        if (response.Length is < 1 or > MaxResponseBytes)
            throw new InvalidDataException("Desktop IPC status exceeds the response limit.");

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, response.Length);
        await server.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await server.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        await server.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns null when no desktop listener is available. A connected server
    /// with an invalid response causes an error rather than fabricated status.
    /// </summary>
    public static async Task<TaskDesktopStatus?> QueryAsync(
        CancellationToken cancellationToken,
        string? pipeName = null)
    {
        var name = pipeName ?? CurrentUserPipeName();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Capri desktop IPC only runs on Windows.");
        using var client = new NamedPipeClientStream(
            ".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(750, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        request.CancelAfter(RequestTimeout);
        await client.WriteAsync(new byte[] { StatusQuery }, request.Token).ConfigureAwait(false);
        await client.FlushAsync(request.Token).ConfigureAwait(false);

        var length = new byte[sizeof(int)];
        await client.ReadExactlyAsync(length, request.Token).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count is < 1 or > MaxResponseBytes)
            throw new InvalidDataException("Desktop IPC returned an invalid response length.");
        var payload = new byte[count];
        await client.ReadExactlyAsync(payload, request.Token).ConfigureAwait(false);

        var result = JsonSerializer.Deserialize<TaskDesktopStatus>(payload, JsonOptions);
        if (result is null || result.Version != ProtocolVersion ||
            result.StopGeneration < 0 || result.CheckedUtc == default)
            throw new InvalidDataException("Desktop IPC returned invalid status metadata.");
        return result;
    }
}

/// <summary>No prompts, plans, targets, credentials, or task actions are transferred.</summary>
public sealed record TaskDesktopStatus(
    int Version,
    DateTimeOffset CheckedUtc,
    bool Stopped,
    long StopGeneration,
    Guid? ActiveTaskId,
    int? ActiveProcessId);
