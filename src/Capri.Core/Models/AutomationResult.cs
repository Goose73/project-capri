namespace Capri.Core.Models;

/// <summary>
/// Outcome of an automation action.
/// </summary>
/// <param name="Succeeded">True when the action completed successfully.</param>
/// <param name="Message">Optional diagnostic detail.</param>
public sealed record AutomationResult(bool Succeeded, string? Message = null);
