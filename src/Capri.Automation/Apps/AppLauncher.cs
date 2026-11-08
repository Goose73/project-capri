using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Capri.Automation.Processes;
using Capri.Core.Abstractions.Automation;
using Capri.Core.Models;

namespace Capri.Automation.Apps;

/// <summary>
/// Launches validated desktop applications without accepting arguments from
/// untrusted input.
/// </summary>
public sealed class AppLauncher : IAppLauncher
{
    private readonly IProcessStarter _processStarter;
    private readonly IAppPathValidator _pathValidator;

    /// <summary>
    /// Creates the launcher.
    /// </summary>
    public AppLauncher(IProcessStarter processStarter, IAppPathValidator pathValidator)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Application launch failures must be surfaced as an automation result.")]
    public Task<AutomationResult> LaunchAsync(
        AppTarget target,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new AutomationResult(false, "The launch request was cancelled."));
        }

        if (target is null)
        {
            return Task.FromResult(new AutomationResult(false, "An application target is required."));
        }

        try
        {
            var validation = _pathValidator.Validate(target.ExecutablePath);
            if (!validation.IsValid)
            {
                return Task.FromResult(new AutomationResult(
                    false,
                    validation.Reason ?? "The application path is not permitted."));
            }

            // No arguments, working directory, or other caller-supplied data are
            // forwarded to the process. Shell execution handles GUI applications.
            var startInfo = new ProcessStartInfo
            {
                FileName = validation.NormalizedPath!,
                UseShellExecute = true,
            };

            return Task.FromResult(_processStarter.TryStart(startInfo, out var failureReason)
                ? new AutomationResult(true, $"Launched {target.Name}.")
                : new AutomationResult(false, failureReason ?? "The application could not be launched."));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or PlatformNotSupportedException)
        {
            return Task.FromResult(new AutomationResult(false, exception.Message));
        }
    }
}
