namespace Capri.Core.Models;

/// <summary>
/// Visual state of the assistant avatar.
/// </summary>
public enum AvatarState
{
    Idle = 0,
    Listening,
    Thinking,
    Talking,
    Success,
    Error,
    Happy,
    Annoyed,
    Surprised,
    Sleep
}
