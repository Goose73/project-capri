namespace Capri.Automation.Media;

/// <summary>
/// Sends a virtual-key press to the operating system.
/// </summary>
public interface IMediaKeySender
{
    /// <summary>
    /// Sends a key-down and key-up sequence for <paramref name="virtualKey"/>.
    /// </summary>
    /// <param name="virtualKey">Windows virtual-key code.</param>
    /// <param name="failureReason">Failure detail when <see langword="false"/> is returned.</param>
    /// <returns>True when the input was accepted by the OS.</returns>
    bool TrySend(int virtualKey, out string? failureReason);
}
