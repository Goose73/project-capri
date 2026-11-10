using Capri.Core.Avatar;
using Capri.Core.Models;

namespace Capri.Tests.Core;

public sealed class AvatarCuePlannerTests
{
    [Theory]
    [InlineData("Hey Capri, look at my screen")]
    [InlineData("Can you see what's on my monitor?")]
    [InlineData("Please inspect this window")]
    [InlineData("What is on the screen right now?")]
    [InlineData("analyse my desktop")]
    [InlineData("look at me screen")]
    [InlineData("Take a screenshot of my display")]
    public void ExplicitDisplayRequestsProduceAttentionCue(string prompt)
    {
        Assert.True(AvatarCuePlanner.RequestsScreenAttention(prompt));
    }

    [Theory]
    [InlineData("How do computer screens work?")]
    [InlineData("Explain screen resolution")]
    [InlineData("Don't look at my screen")]
    [InlineData("Do not look at this window")]
    [InlineData("Stop looking at my desktop")]
    [InlineData("hi Capri")]
    [InlineData("")]
    public void GeneralScreenDiscussionAndNegativeRequestsAreNotAttentionCues(string prompt)
    {
        Assert.False(AvatarCuePlanner.RequestsScreenAttention(prompt));
    }

    [Theory]
    [InlineData(AvatarState.Listening, AvatarCueKind.LeanIn)]
    [InlineData(AvatarState.Thinking, AvatarCueKind.LookAround)]
    [InlineData(AvatarState.Talking, AvatarCueKind.SpeakEmphasis)]
    [InlineData(AvatarState.Happy, AvatarCueKind.Hop)]
    [InlineData(AvatarState.Error, AvatarCueKind.Shake)]
    [InlineData(AvatarState.Idle, AvatarCueKind.None)]
    [InlineData(AvatarState.Sleep, AvatarCueKind.None)]
    public void StateCuesAreDistinctAndBoundToRealState(AvatarState state, AvatarCueKind expected)
    {
        Assert.Equal(expected, AvatarCuePlanner.ForState(state));
    }

    [Fact]
    public void GazeIsBoundedEvenForInvalidCoordinates()
    {
        var limited = AvatarCuePlanner.LimitGaze(90, -34, reduceMotion: false);
        Assert.Equal(new AvatarGaze(1, -1), limited);
        Assert.Equal(default, AvatarCuePlanner.LimitGaze(double.NaN, 1, false));
        Assert.Equal(default, AvatarCuePlanner.LimitGaze(0.8, 0.8, true));
    }

    [Theory]
    [InlineData(AvatarCueKind.Summon)]
    [InlineData(AvatarCueKind.ScreenRequested)]
    [InlineData(AvatarCueKind.LookAround)]
    [InlineData(AvatarCueKind.Nod)]
    [InlineData(AvatarCueKind.Shake)]
    [InlineData(AvatarCueKind.Hop)]
    [InlineData(AvatarCueKind.Blink)]
    public void CuesNeverAnimateWithoutArtworkOrWithReducedMotion(AvatarCueKind cue)
    {
        Assert.True(AvatarCuePlanner.ShouldAnimate(cue, reduceMotion: false, hasArtwork: true));
        Assert.False(AvatarCuePlanner.ShouldAnimate(cue, reduceMotion: true, hasArtwork: true));
        Assert.False(AvatarCuePlanner.ShouldAnimate(cue, reduceMotion: false, hasArtwork: false));
    }
}
