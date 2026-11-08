using System.Diagnostics.CodeAnalysis;
using Capri.Automation.Web;
using Capri.Core.Abstractions;
using Capri.Core.Abstractions.Automation;
using Capri.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Automation;

/// <summary>
/// Coordinates permission checks and the concrete automation launchers.
/// The service converts every failure, including unexpected exceptions, into an
/// <see cref="AutomationResult"/> so callers never need to handle exceptions.
/// </summary>
public sealed partial class AutomationService : IAutomationService
{
    private readonly IPermissionService _permissionService;
    private readonly IWebLauncher _webLauncher;
    private readonly IAppAliasResolver _appAliasResolver;
    private readonly IAppLauncher _appLauncher;
    private readonly IMediaControl _mediaControl;
    private readonly ILogger<AutomationService> _logger;

    /// <summary>
    /// Creates the automation service.
    /// </summary>
    public AutomationService(
        IPermissionService permissionService,
        IWebLauncher webLauncher,
        IAppAliasResolver appAliasResolver,
        IAppLauncher appLauncher,
        IMediaControl mediaControl,
        ILogger<AutomationService>? logger = null)
    {
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _webLauncher = webLauncher ?? throw new ArgumentNullException(nameof(webLauncher));
        _appAliasResolver = appAliasResolver ?? throw new ArgumentNullException(nameof(appAliasResolver));
        _appLauncher = appLauncher ?? throw new ArgumentNullException(nameof(appLauncher));
        _mediaControl = mediaControl ?? throw new ArgumentNullException(nameof(mediaControl));
        _logger = logger ?? NullLogger<AutomationService>.Instance;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Automation must be non-throwing so a failed action cannot crash the assistant.")]
    public async Task<AutomationResult> ExecuteAsync(
        Intent intent,
        CancellationToken cancellationToken = default)
    {
        if (intent is null)
        {
            return new AutomationResult(false, "An automation intent is required.");
        }

        try
        {
            return intent.Kind switch
            {
                IntentKind.OpenWeb => await ExecuteWebAsync(intent, cancellationToken).ConfigureAwait(false),
                IntentKind.LaunchApp => await ExecuteAppAsync(intent, cancellationToken).ConfigureAwait(false),
                IntentKind.MediaControl => await ExecuteMediaAsync(intent, cancellationToken).ConfigureAwait(false),
                _ => new AutomationResult(false, $"Intent kind '{intent.Kind}' is not an automation action."),
            };
        }
        catch (OperationCanceledException)
        {
            return new AutomationResult(false, "The automation action was cancelled.");
        }
        catch (Exception exception)
        {
            LogAutomationFailure(_logger, exception);
            return new AutomationResult(false, "The automation action failed unexpectedly.");
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Automation action failed.")]
    private static partial void LogAutomationFailure(ILogger logger, Exception exception);

    private async Task<AutomationResult> ExecuteWebAsync(Intent intent, CancellationToken cancellationToken)
    {
        if (!UrlPolicy.TryValidate(intent.Target, out var uri, out var reason))
        {
            return new AutomationResult(false, reason);
        }

        var decision = await _permissionService.RequestAsync(
            new PermissionRequest(
                PermissionKind.OpenWeb,
                $"Open {uri}.",
                uri.AbsoluteUri),
            cancellationToken).ConfigureAwait(false);

        if (!decision.Allowed)
        {
            return Denied(decision);
        }

        return await _webLauncher.OpenAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AutomationResult> ExecuteAppAsync(Intent intent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(intent.Target))
        {
            return new AutomationResult(false, "An application alias is required.");
        }

        var target = await _appAliasResolver.ResolveAsync(intent.Target, cancellationToken)
            .ConfigureAwait(false);

        if (target is null)
        {
            return new AutomationResult(false, $"The application '{intent.Target}' is not available.");
        }

        var decision = await _permissionService.RequestAsync(
            new PermissionRequest(
                PermissionKind.LaunchApplication,
                $"Launch {target.Name}.",
                target.ExecutablePath),
            cancellationToken).ConfigureAwait(false);

        if (!decision.Allowed)
        {
            return Denied(decision);
        }

        return await _appLauncher.LaunchAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AutomationResult> ExecuteMediaAsync(Intent intent, CancellationToken cancellationToken)
    {
        if (intent.Media is null)
        {
            return new AutomationResult(false, "A media command is required.");
        }

        var command = intent.Media.Value;
        var decision = await _permissionService.RequestAsync(
            new PermissionRequest(
                PermissionKind.MediaControl,
                $"Send media command {command}.",
                command.ToString()),
            cancellationToken).ConfigureAwait(false);

        if (!decision.Allowed)
        {
            return Denied(decision);
        }

        return await _mediaControl.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static AutomationResult Denied(PermissionDecision decision)
    {
        return new AutomationResult(false, decision.Reason ?? "Permission was denied.");
    }
}
