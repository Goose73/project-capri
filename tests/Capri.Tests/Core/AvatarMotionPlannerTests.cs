using Capri.Core.Avatar;
using Capri.Core.Models;

namespace Capri.Tests.Core;

public sealed class AvatarMotionPlannerTests
{
    [Theory]
    [InlineData(AvatarState.Idle, AvatarMotionKind.Breathe)]
    [InlineData(AvatarState.Listening, AvatarMotionKind.Attend)]
    [InlineData(AvatarState.Thinking, AvatarMotionKind.Scan)]
    [InlineData(AvatarState.Talking, AvatarMotionKind.Speak)]
    [InlineData(AvatarState.Success, AvatarMotionKind.Celebrate)]
    [InlineData(AvatarState.Happy, AvatarMotionKind.Celebrate)]
    [InlineData(AvatarState.Annoyed, AvatarMotionKind.Disagree)]
    [InlineData(AvatarState.Error, AvatarMotionKind.Disagree)]
    [InlineData(AvatarState.Surprised, AvatarMotionKind.React)]
    public void StatesChooseDistinctBoundedVisualLanguage(AvatarState state, AvatarMotionKind expected)
    {
        var motion = AvatarMotionPlanner.ForState(state, reduceMotion: false);

        Assert.True(motion.Animated);
        Assert.Equal(expected, motion.Kind);
        Assert.InRange(motion.BobPixels, -8, 0);
        Assert.InRange(motion.SwayPixels, 0, 4);
        Assert.InRange(motion.TiltDegrees, 0, 5);
        Assert.InRange(motion.StretchY, 1, 1.05);
        Assert.InRange(motion.HalfCycleMilliseconds, 200, 1600);
    }

    [Theory]
    [InlineData(AvatarState.Idle)]
    [InlineData(AvatarState.Listening)]
    [InlineData(AvatarState.Thinking)]
    [InlineData(AvatarState.Talking)]
    [InlineData(AvatarState.Success)]
    [InlineData(AvatarState.Error)]
    [InlineData(AvatarState.Happy)]
    [InlineData(AvatarState.Annoyed)]
    [InlineData(AvatarState.Surprised)]
    [InlineData(AvatarState.Sleep)]
    public void ReduceMotionDisablesEveryAnimatedPose(AvatarState state)
    {
        var motion = AvatarMotionPlanner.ForState(state, reduceMotion: true);

        Assert.False(motion.Animated);
        Assert.Equal(AvatarMotionKind.Still, motion.Kind);
        Assert.Equal(0, motion.SwayPixels);
        Assert.Equal(0, motion.BobPixels);
        Assert.Equal(0, motion.TiltDegrees);
        Assert.Equal(1, motion.StretchY);
    }

    [Fact]
    public void SleepDoesNotAnimateEvenWithoutReducedMotion()
    {
        var motion = AvatarMotionPlanner.ForState(AvatarState.Sleep, reduceMotion: false);
        Assert.False(motion.Animated);
        Assert.Equal(AvatarMotionKind.Still, motion.Kind);
    }

    [Fact]
    public void RespondingMotionIsQuickerThanIdleAndInvestigation()
    {
        var speaking = AvatarMotionPlanner.ForState(AvatarState.Talking, false);
        var idle = AvatarMotionPlanner.ForState(AvatarState.Idle, false);
        var thinking = AvatarMotionPlanner.ForState(AvatarState.Thinking, false);

        Assert.True(speaking.HalfCycleMilliseconds < idle.HalfCycleMilliseconds);
        Assert.True(speaking.HalfCycleMilliseconds < thinking.HalfCycleMilliseconds);
        Assert.NotEqual(idle.Kind, thinking.Kind);
    }
}
