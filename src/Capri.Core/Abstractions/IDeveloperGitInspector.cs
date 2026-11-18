using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Read-only Git status/diff previews for an explicitly selected developer
/// session. Opening a project does not grant Git inspection permission.
/// </summary>
public interface IDeveloperGitInspector
{
    Task<DeveloperGitStatus> GetStatusAsync(
        Guid sessionId, bool authorized, CancellationToken cancellationToken = default);

    Task<DeveloperGitDiff> GetDiffAsync(
        Guid sessionId, string relativeSourceFile, bool authorized,
        CancellationToken cancellationToken = default);
}
