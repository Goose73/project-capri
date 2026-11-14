using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Shared local developer-session metadata used by GUI and future terminal clients.
/// This is not an execution or file-access authorization interface.
/// </summary>
public interface IDeveloperSessionStore
{
    DeveloperSession Create(string workspaceDirectory, string? displayName = null);
    IReadOnlyList<DeveloperSession> List(int maxCount = 30);
    DeveloperSession? Find(Guid sessionId);
}
