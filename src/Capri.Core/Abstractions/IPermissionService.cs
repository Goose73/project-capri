using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Obtains user consent before user-visible automation actions.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Requests a decision for an action, prompting the user when required.
    /// </summary>
    Task<PermissionDecision> RequestAsync(PermissionRequest request, CancellationToken cancellationToken = default);
}
