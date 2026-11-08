using Capri.Core.Abstractions;
using Capri.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Security;

/// <summary>
/// Phase 1 permission policy. Actions are allowed when automation is enabled
/// and the matching category is enabled in <see cref="AppSettings.AutomationSettings"/>.
/// No interactive confirmation UI exists yet, so decisions are immediate.
/// </summary>
public sealed partial class DefaultPermissionService : IPermissionService
{
    private readonly IAppSettingsStore _settings;
    private readonly ILogger<DefaultPermissionService> _logger;

    /// <summary>
    /// Creates the policy service.
    /// </summary>
    public DefaultPermissionService(
        IAppSettingsStore settings,
        ILogger<DefaultPermissionService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullLogger<DefaultPermissionService>.Instance;
    }

    /// <inheritdoc />
    public Task<PermissionDecision> RequestAsync(
        PermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var automation = _settings.Current.Automation;
        var (allowed, reason) = Evaluate(request.Kind, automation);

        if (!allowed)
        {
            // Log the category only; descriptions and targets may contain user data.
            LogPermissionDenied(_logger, request.Kind);
            return Task.FromResult(new PermissionDecision(Allowed: false, Remember: false, Reason: reason));
        }

        return Task.FromResult(new PermissionDecision(Allowed: true));
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Permission denied for action category {PermissionKind}.")]
    private static partial void LogPermissionDenied(ILogger logger, PermissionKind permissionKind);

    private static (bool Allowed, string? Reason) Evaluate(
        PermissionKind kind,
        AppSettings.AutomationSettings automation)
    {
        if (!automation.Enabled)
        {
            return (false, "Automation is disabled in settings.");
        }

        return kind switch
        {
            PermissionKind.LaunchApplication when !automation.AllowAppLaunch
                => (false, "Application launching is disabled in settings."),
            PermissionKind.LaunchApplication
                => (true, null),

            PermissionKind.OpenWeb when !automation.AllowWebLaunch
                => (false, "Opening web pages is disabled in settings."),
            PermissionKind.OpenWeb
                => (true, null),

            PermissionKind.MediaControl when !automation.AllowMediaControl
                => (false, "Media control is disabled in settings."),
            PermissionKind.MediaControl
                => (true, null),

            PermissionKind.FileAccess or PermissionKind.RunCommand
                => (false, "This action category is not supported in Phase 1."),

            _ => (false, "Unknown action category."),
        };
    }
}
