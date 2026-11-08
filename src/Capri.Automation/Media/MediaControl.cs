using Capri.Core.Abstractions.Automation;
using Capri.Core.Models;

namespace Capri.Automation.Media;

/// <summary>
/// Maps media transport commands to Windows media virtual keys and sends them.
/// </summary>
public sealed class MediaControl : IMediaControl
{
    private readonly IMediaKeySender _keySender;

    /// <summary>
    /// Creates the media controller.
    /// </summary>
    public MediaControl(IMediaKeySender keySender)
    {
        _keySender = keySender ?? throw new ArgumentNullException(nameof(keySender));
    }

    /// <inheritdoc />
    public Task<AutomationResult> ExecuteAsync(
        MediaCommand command,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new AutomationResult(false, "The media command was cancelled."));
        }

        if (!TryMap(command, out var virtualKey))
        {
            return Task.FromResult(new AutomationResult(false, "The media command is not supported."));
        }

        return Task.FromResult(_keySender.TrySend(virtualKey, out var failureReason)
            ? new AutomationResult(true, command == MediaCommand.PlayPause
                ? "Sent play/pause toggle; playback state is unknown, so pause is not verified."
                : $"Sent media command {command}; player state not verified.")
            : new AutomationResult(false, failureReason ?? "The media key could not be sent."));
    }

    /// <summary>
    /// Maps a <see cref="MediaCommand"/> to its Windows virtual-key code.
    /// </summary>
    public static bool TryMap(MediaCommand command, out int virtualKey)
    {
        virtualKey = command switch
        {
            MediaCommand.PlayPause => MediaVirtualKeys.PlayPause,
            MediaCommand.Next => MediaVirtualKeys.NextTrack,
            MediaCommand.Previous => MediaVirtualKeys.PreviousTrack,
            MediaCommand.Stop => MediaVirtualKeys.Stop,
            _ => 0,
        };

        return virtualKey != 0;
    }
}
