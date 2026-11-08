namespace Capri.Core.Models;

/// <summary>
/// A request for permission to perform a user-visible action.
/// </summary>
/// <param name="Kind">Category of the action.</param>
/// <param name="Description">Human-readable explanation shown to the user.</param>
/// <param name="Target">Optional application, URL, or other action target.</param>
/// <param name="IsDestructive">True when the action cannot easily be undone.</param>
public sealed record PermissionRequest(
    PermissionKind Kind,
    string Description,
    string? Target = null,
    bool IsDestructive = false);
