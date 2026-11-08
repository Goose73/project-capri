using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Capri.Automation.Processes;

/// <summary>
/// Default <see cref="IProcessStarter"/> that delegates to <see cref="Process.Start(ProcessStartInfo)"/>.
/// </summary>
public sealed class ShellProcessStarter : IProcessStarter
{
    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Process startup failures must be surfaced as a result instead of crashing the assistant.")]
    public bool TryStart(ProcessStartInfo startInfo, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        try
        {
            using var process = Process.Start(startInfo);
            failureReason = null;
            return true;
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException
                or PlatformNotSupportedException)
        {
            failureReason = exception.Message;
            return false;
        }
    }
}
