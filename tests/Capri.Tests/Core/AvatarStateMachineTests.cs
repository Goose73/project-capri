using Capri.Core.Avatar;
using Capri.Core.Models;

namespace Capri.Tests.Core;

public sealed class AvatarStateMachineTests
{
    [Fact]
    public void InitialStateIsIdle()
    {
        var machine = new AvatarStateMachine();

        Assert.Equal(AvatarState.Idle, machine.Current);
    }

    [Theory]
    [InlineData(AvatarState.Idle, AvatarState.Listening)]
    [InlineData(AvatarState.Idle, AvatarState.Thinking)]
    [InlineData(AvatarState.Listening, AvatarState.Idle)]
    [InlineData(AvatarState.Listening, AvatarState.Thinking)]
    [InlineData(AvatarState.Listening, AvatarState.Error)]
    [InlineData(AvatarState.Thinking, AvatarState.Idle)]
    [InlineData(AvatarState.Thinking, AvatarState.Talking)]
    [InlineData(AvatarState.Thinking, AvatarState.Success)]
    [InlineData(AvatarState.Thinking, AvatarState.Error)]
    [InlineData(AvatarState.Talking, AvatarState.Idle)]
    [InlineData(AvatarState.Talking, AvatarState.Success)]
    [InlineData(AvatarState.Talking, AvatarState.Error)]
    [InlineData(AvatarState.Success, AvatarState.Idle)]
    [InlineData(AvatarState.Success, AvatarState.Listening)]
    [InlineData(AvatarState.Success, AvatarState.Thinking)]
    [InlineData(AvatarState.Error, AvatarState.Idle)]
    [InlineData(AvatarState.Error, AvatarState.Listening)]
    [InlineData(AvatarState.Error, AvatarState.Thinking)]
    public void TryTransitionAllowsExpectedTransitions(AvatarState from, AvatarState to)
    {
        var machine = CreateInState(from);

        var changed = machine.TryTransition(to, AvatarTransitionReason.Unknown);

        Assert.True(changed);
        Assert.Equal(to, machine.Current);
    }

    [Theory]
    [InlineData(AvatarState.Idle, AvatarState.Talking)]
    [InlineData(AvatarState.Idle, AvatarState.Success)]
    [InlineData(AvatarState.Idle, AvatarState.Error)]
    [InlineData(AvatarState.Listening, AvatarState.Talking)]
    [InlineData(AvatarState.Listening, AvatarState.Success)]
    [InlineData(AvatarState.Thinking, AvatarState.Listening)]
    [InlineData(AvatarState.Talking, AvatarState.Listening)]
    [InlineData(AvatarState.Talking, AvatarState.Thinking)]
    [InlineData(AvatarState.Success, AvatarState.Talking)]
    [InlineData(AvatarState.Success, AvatarState.Error)]
    [InlineData(AvatarState.Error, AvatarState.Talking)]
    [InlineData(AvatarState.Error, AvatarState.Success)]
    public void TryTransitionRejectsIllegalTransitions(AvatarState from, AvatarState to)
    {
        var machine = CreateInState(from);
        var events = 0;
        machine.StateChanged += (_, _) => events++;

        var changed = machine.TryTransition(to, AvatarTransitionReason.Unknown);

        Assert.False(changed);
        Assert.Equal(from, machine.Current);
        Assert.Equal(0, events);
    }

    [Fact]
    public void TryTransitionIsIdempotentForCurrentStateAndSilent()
    {
        var machine = new AvatarStateMachine();
        var events = new List<AvatarStateChangedEventArgs>();
        machine.StateChanged += (_, e) => events.Add(e);

        var changed = machine.TryTransition(AvatarState.Idle, AvatarTransitionReason.Unknown);

        Assert.True(changed);
        Assert.Equal(AvatarState.Idle, machine.Current);
        Assert.Empty(events);
    }

    [Fact]
    public void TryTransitionRaisesEventWithPreviousCurrentAndReason()
    {
        var machine = new AvatarStateMachine();
        var events = new List<AvatarStateChangedEventArgs>();
        machine.StateChanged += (_, e) => events.Add(e);

        machine.TryTransition(AvatarState.Thinking, AvatarTransitionReason.IntentDetected);

        var evt = Assert.Single(events);
        Assert.Equal(AvatarState.Idle, evt.Previous);
        Assert.Equal(AvatarState.Thinking, evt.Current);
        Assert.Equal(AvatarTransitionReason.IntentDetected, evt.Reason);
    }

    [Fact]
    public void ResetReturnsToIdleAndRaisesEvent()
    {
        var machine = CreateInState(AvatarState.Error);
        var events = new List<AvatarStateChangedEventArgs>();
        machine.StateChanged += (_, e) => events.Add(e);

        machine.Reset();

        Assert.Equal(AvatarState.Idle, machine.Current);
        var evt = Assert.Single(events);
        Assert.Equal(AvatarState.Error, evt.Previous);
        Assert.Equal(AvatarState.Idle, evt.Current);
        Assert.Equal(AvatarTransitionReason.Reset, evt.Reason);
    }

    [Fact]
    public void ResetIsIdempotentWhenAlreadyIdle()
    {
        var machine = new AvatarStateMachine();
        var events = 0;
        machine.StateChanged += (_, _) => events++;

        machine.Reset();

        Assert.Equal(0, events);
    }

    private static AvatarStateMachine CreateInState(AvatarState state)
    {
        var machine = new AvatarStateMachine();
        if (state == AvatarState.Idle)
        {
            return machine;
        }

        // Drive through legal paths to reach the requested state.
        var path = state switch
        {
            AvatarState.Listening => new[] { AvatarState.Listening },
            AvatarState.Thinking => new[] { AvatarState.Thinking },
            AvatarState.Talking => new[] { AvatarState.Thinking, AvatarState.Talking },
            AvatarState.Success => new[] { AvatarState.Thinking, AvatarState.Success },
            AvatarState.Error => new[] { AvatarState.Thinking, AvatarState.Error },
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        foreach (var step in path)
        {
            Assert.True(machine.TryTransition(step, AvatarTransitionReason.Unknown), $"Failed to reach {step}.");
        }

        Assert.Equal(state, machine.Current);
        return machine;
    }
}
