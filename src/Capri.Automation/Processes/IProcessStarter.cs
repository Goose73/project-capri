using System.Diagnostics;

namespace Capri.Automation.Processes;

/// <summary>
/// Starts a process described by a <see cref="ProcessStartInfo"/>.
/// Abstracted so automations can be unit tested without launching real programs.
/// </summary>
public interface IProcessStarter
{
    /// <summary>
    /// Attempts to start the process.
    /// </summary>
    /// <param name="startInfo">Fully prepared start information.</param>
    /// <param name="failureReason">Failure detail when <see langword="false"/> is returned.</param>
    /// <returns>True when the process was started.</returns>
    bool TryStart(ProcessStartInfo startInfo, out string? failureReason);
}
