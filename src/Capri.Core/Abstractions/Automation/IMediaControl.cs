using Capri.Core.Models;

namespace Capri.Core.Abstractions.Automation;

/// <summary>
/// Sends media transport commands to the operating system.
/// </summary>
public interface IMediaControl
{
    /// <summary>
    /// Executes a media transport command.
    /// </summary>
    Task<AutomationResult> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default);
}
