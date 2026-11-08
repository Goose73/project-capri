namespace Capri.Core.Models;

/// <summary>
/// Outcome of a permission request.
/// </summary>
/// <param name="Allowed">True when the user permitted the action.</param>
/// <param name="Remember">True when the decision should be reused for similar actions.</param>
/// <param name="Reason">Optional explanation for a denial.</param>
public sealed record PermissionDecision(
    bool Allowed,
    bool Remember = false,
    string? Reason = null);
