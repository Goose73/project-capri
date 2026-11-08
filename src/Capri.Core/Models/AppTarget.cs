namespace Capri.Core.Models;

/// <summary>
/// A desktop application that can be launched.
/// </summary>
/// <param name="Name">Friendly display name for the application.</param>
/// <param name="ExecutablePath">Optional absolute path to the executable.</param>
/// <param name="ProcessName">Optional process name used to detect a running instance.</param>
public sealed record AppTarget(
    string Name,
    string? ExecutablePath = null,
    string? ProcessName = null);
