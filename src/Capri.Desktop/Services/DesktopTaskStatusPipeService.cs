using Capri.Core.Autonomy;
using Capri.Security.Autonomy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Capri.Desktop.Services;

/// <summary>
/// Desktop-owned, read-only per-user named-pipe status endpoint. This service
/// does not execute work and cannot grant or forward task approvals.
/// </summary>
public sealed class DesktopTaskStatusPipeService : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogUnavailable =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(5101, "TaskStatusIpcUnavailable"),
            "Read-only task status IPC is unavailable; desktop task control remains local.");

    private readonly ITaskControl _control;
    private readonly ILogger<DesktopTaskStatusPipeService> _logger;

    public DesktopTaskStatusPipeService(
        ITaskControl control,
        ILogger<DesktopTaskStatusPipeService> logger)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await TaskStatusPipe.ServeAsync(_control, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal desktop shutdown: stop listening without modifying tasks.
        }
        catch (Exception exception)
        {
            // Status IPC is optional. Fail closed and leave the desktop, existing
            // task coordinator, and approval workflow independent of the listener.
            LogUnavailable(_logger, exception);
        }
    }
}
