using Capri.Core.Models;

namespace Capri.Core.Abstractions.Automation;

/// <summary>
/// Executes structured automation intents.
/// </summary>
public interface IAutomationService
{
    /// <summary>
    /// Executes the automation action described by <paramref name="intent"/>.
    /// </summary>
    Task<AutomationResult> ExecuteAsync(Intent intent, CancellationToken cancellationToken = default);
}
