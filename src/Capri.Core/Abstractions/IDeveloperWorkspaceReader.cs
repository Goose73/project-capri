using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Bounded read-only project discovery. Every operation requires independent
/// caller authorization; creating a developer session grants none.
/// </summary>
public interface IDeveloperWorkspaceReader
{
    IReadOnlyList<DeveloperWorkspaceItem> List(Guid sessionId, string? relativeDirectory, bool authorized);
    string ReadText(Guid sessionId, string relativeFile, bool authorized);
}
