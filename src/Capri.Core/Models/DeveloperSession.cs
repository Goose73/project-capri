namespace Capri.Core.Models;

/// <summary>
/// Metadata for a user-selected development workspace. Creating a session does not
/// read source files, run commands, or grant an agent access to the workspace.
/// </summary>
public sealed record DeveloperSession(
    Guid Id,
    string DisplayName,
    string WorkspaceDirectory,
    DateTimeOffset CreatedUtc);
