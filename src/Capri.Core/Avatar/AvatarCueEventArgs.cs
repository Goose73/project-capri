namespace Capri.Core.Avatar;

/// <summary>
/// UI-only animation request. Deliberately carries no screen pixels, microphone
/// data, input text or permission state. An event indicates desired presentation,
/// never that an external action succeeded.
/// </summary>
public sealed class AvatarCueEventArgs(AvatarCueKind cue) : EventArgs
{
    public AvatarCueKind Cue { get; } = cue;
}
