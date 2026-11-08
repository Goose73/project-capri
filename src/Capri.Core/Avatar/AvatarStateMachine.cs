using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Core.Avatar;

/// <summary>
/// Pure, synchronous avatar state machine. Dwell timers and visual effects belong
/// to the desktop layer; this type only enforces legal transitions and reports
/// state changes.
/// </summary>
public sealed class AvatarStateMachine : IAvatarStateMachine
{
    private static readonly Dictionary<AvatarState, AvatarState[]> AllowedTransitions =
        new()
        {
            [AvatarState.Idle] = [AvatarState.Listening, AvatarState.Thinking, AvatarState.Sleep, AvatarState.Surprised],
            [AvatarState.Listening] = [AvatarState.Idle, AvatarState.Thinking, AvatarState.Error, AvatarState.Annoyed, AvatarState.Surprised],
            [AvatarState.Thinking] = [AvatarState.Idle, AvatarState.Talking, AvatarState.Success, AvatarState.Happy, AvatarState.Error, AvatarState.Annoyed, AvatarState.Surprised],
            [AvatarState.Talking] = [AvatarState.Idle, AvatarState.Success, AvatarState.Happy, AvatarState.Error, AvatarState.Annoyed, AvatarState.Surprised],
            [AvatarState.Success] = [AvatarState.Idle, AvatarState.Listening, AvatarState.Thinking, AvatarState.Happy],
            [AvatarState.Error] = [AvatarState.Idle, AvatarState.Listening, AvatarState.Thinking, AvatarState.Annoyed],
            [AvatarState.Happy] = [AvatarState.Idle, AvatarState.Listening, AvatarState.Thinking, AvatarState.Surprised],
            [AvatarState.Annoyed] = [AvatarState.Idle, AvatarState.Listening, AvatarState.Thinking],
            [AvatarState.Surprised] = [AvatarState.Idle, AvatarState.Listening, AvatarState.Thinking, AvatarState.Talking, AvatarState.Happy],
            [AvatarState.Sleep] = [AvatarState.Idle, AvatarState.Listening, AvatarState.Surprised],
        };

    private readonly object _sync = new();
    private AvatarState _current = AvatarState.Idle;

    /// <inheritdoc />
    public AvatarState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<AvatarStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public bool TryTransition(AvatarState nextState, AvatarTransitionReason reason)
    {
        AvatarState previous;

        lock (_sync)
        {
            previous = _current;

            if (nextState == previous)
            {
                // Re-entering the current state is a no-op and stays silent.
                return true;
            }

            if (!AllowedTransitions.TryGetValue(previous, out var allowed)
                || !allowed.Contains(nextState))
            {
                return false;
            }

            _current = nextState;
        }

        RaiseStateChanged(previous, nextState, reason);
        return true;
    }

    /// <inheritdoc />
    public void Reset()
    {
        AvatarState previous;

        lock (_sync)
        {
            previous = _current;
            if (previous == AvatarState.Idle)
            {
                return;
            }

            _current = AvatarState.Idle;
        }

        RaiseStateChanged(previous, AvatarState.Idle, AvatarTransitionReason.Reset);
    }

    private void RaiseStateChanged(
        AvatarState previous,
        AvatarState current,
        AvatarTransitionReason reason)
    {
        StateChanged?.Invoke(this, new AvatarStateChangedEventArgs(previous, current, reason));
    }
}
