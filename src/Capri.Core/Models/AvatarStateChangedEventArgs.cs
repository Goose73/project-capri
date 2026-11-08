namespace Capri.Core.Models;

/// <summary>
/// Event data raised when the avatar changes state.
/// </summary>
public sealed class AvatarStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Creates event data for an avatar state change.
    /// </summary>
    /// <param name="previous">State before the transition.</param>
    /// <param name="current">State after the transition.</param>
    /// <param name="reason">Why the transition occurred.</param>
    public AvatarStateChangedEventArgs(AvatarState previous, AvatarState current, AvatarTransitionReason reason)
    {
        Previous = previous;
        Current = current;
        Reason = reason;
    }

    /// <summary>
    /// State before the transition.
    /// </summary>
    public AvatarState Previous { get; }

    /// <summary>
    /// State after the transition.
    /// </summary>
    public AvatarState Current { get; }

    /// <summary>
    /// Why the transition occurred.
    /// </summary>
    public AvatarTransitionReason Reason { get; }
}
