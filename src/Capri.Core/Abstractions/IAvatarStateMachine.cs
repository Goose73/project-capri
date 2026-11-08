using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Guards legal transitions of the avatar's visual state.
/// </summary>
public interface IAvatarStateMachine
{
    /// <summary>
    /// Current avatar state.
    /// </summary>
    AvatarState Current { get; }

    /// <summary>
    /// Raised after the avatar state changes.
    /// </summary>
    event EventHandler<AvatarStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Attempts to move to <paramref name="nextState"/>, returning false when the transition is not allowed.
    /// </summary>
    bool TryTransition(AvatarState nextState, AvatarTransitionReason reason);

    /// <summary>
    /// Returns the avatar to its initial state.
    /// </summary>
    void Reset();
}
