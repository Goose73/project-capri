using Capri.Core.Models;

namespace Capri.Core.Abstractions.Automation;

/// <summary>
/// Launches desktop applications.
/// </summary>
public interface IAppLauncher
{
    /// <summary>
    /// Starts the supplied application target.
    /// </summary>
    Task<AutomationResult> LaunchAsync(AppTarget target, CancellationToken cancellationToken = default);
}
