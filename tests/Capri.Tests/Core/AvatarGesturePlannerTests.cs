using Capri.Core.Avatar;

namespace Capri.Tests.Core;

/// <summary>
/// Deterministic motion plans are verified independently of WPF; manual
/// sprite/visual acceptance is still required for illustrated frame quality.
/// </summary>
public sealed class AvatarGesturePlannerTests
{
    [Theory]
    [InlineData(AvatarGesture.Appear)]
    [InlineData(AvatarGesture.Disappear)]
    [InlineData(AvatarGesture.LookLeft)]
    [InlineData(AvatarGesture.LookRight)]
    [InlineData(AvatarGesture.LookAround)]
    [InlineData(AvatarGesture.Nod)]
    [InlineData(AvatarGesture.Shake)]
    [InlineData(AvatarGesture.Hop)]
    [InlineData(AvatarGesture.Jump)]
    [InlineData(AvatarGesture.WalkLeft)]
    [InlineData(AvatarGesture.WalkRight)]
    [InlineData(AvatarGesture.TurnToScreen)]
    [InlineData(AvatarGesture.LeanIn)]
    [InlineData(AvatarGesture.Wave)]
    [InlineData(AvatarGesture.Celebrate)]
    [InlineData(AvatarGesture.Blink)]
    [InlineData(AvatarGesture.MouthSpeak)]
    [InlineData(AvatarGesture.SideEye)]
    public void EveryGestureHasBoundedNonFlashingDeterministicSequence(AvatarGesture gesture)
    {
        var clip = AvatarGesturePlanner.ForGesture(gesture, reduceMotion: false);
        Assert.NotNull(clip);
        Assert.Equal(gesture, clip.Gesture);
        Assert.InRange(clip.DurationMs, 200, 2000);
        Assert.InRange(clip.Frames.Count, 2, 16);
        Assert.Equal(0, clip.Frames[0].AtMs);
        Assert.Equal(clip.DurationMs, clip.Frames[^1].AtMs);

        var lastAt = -1;
        foreach (var frame in clip.Frames)
        {
            Assert.True(frame.AtMs > lastAt);
            lastAt = frame.AtMs;
            Assert.InRange(frame.X, -24, 24);
            Assert.InRange(frame.Y, -26, 24);
            Assert.InRange(frame.Angle, -12, 12);
            Assert.InRange(frame.ScaleX, .8, 1.08);
            Assert.InRange(frame.ScaleY, .8, 1.08);
            Assert.InRange(frame.Opacity, 0, 1);
            Assert.InRange(frame.Echo, 0, .3);
        }
    }

    [Theory]
    [InlineData(AvatarGesture.Appear)]
    [InlineData(AvatarGesture.LookAround)]
    [InlineData(AvatarGesture.Jump)]
    [InlineData(AvatarGesture.Blink)]
    [InlineData(AvatarGesture.MouthSpeak)]
    public void ReducedMotionDisablesAllCues(AvatarGesture gesture)
    {
        Assert.Null(AvatarGesturePlanner.ForGesture(gesture, reduceMotion: true));
    }

    [Fact]
    public void InvalidGestureFailsClosed()
    {
        Assert.Null(AvatarGesturePlanner.ForGesture((AvatarGesture)100_000, false));
    }

    [Fact]
    public void MouthEyeAndSideEyeNeedRealArtworkFrames()
    {
        foreach (var gesture in new[]
        {
            AvatarGesture.Blink, AvatarGesture.MouthSpeak, AvatarGesture.SideEye
        })
        {
            Assert.True(AvatarGesturePlanner.ForGesture(gesture, false)!.RequiresArtworkFrames);
        }
        Assert.False(AvatarGesturePlanner.ForGesture(AvatarGesture.Nod, false)!.RequiresArtworkFrames);
        Assert.False(AvatarGesturePlanner.ForGesture(AvatarGesture.TurnToScreen, false)!.RequiresArtworkFrames);
    }

    [Fact]
    public void ScreenTurnIsPresentationOnlyAndNeverIdentifiesAnObservedScreen()
    {
        var cue = AvatarGesturePlanner.ForGesture(AvatarGesture.TurnToScreen, false)!;
        Assert.Contains(cue.Frames, f => f.X < 0 && f.Angle < 0);
        Assert.True(cue.Frames[^2].AtMs - cue.Frames[2].AtMs >= 400);
        Assert.Equal(1, cue.Frames[^1].Opacity);
    }

    [Fact]
    public void WalkingMovesOppositeDirectionsWithoutMirroringTheOriginalSprite()
    {
        var left = AvatarGesturePlanner.ForGesture(AvatarGesture.WalkLeft, false)!;
        var right = AvatarGesturePlanner.ForGesture(AvatarGesture.WalkRight, false)!;
        Assert.Equal(left.DurationMs, right.DurationMs);
        Assert.Contains(left.Frames, f => f.X < -10);
        Assert.Contains(right.Frames, f => f.X > 10);
        Assert.All(left.Frames.Concat(right.Frames), f => Assert.True(f.ScaleX > 0));
    }
}

public sealed class AvatarGestureTextCueParserTests
{
    [Theory]
    [InlineData("Hey Capri!", AvatarGesture.Appear)]
    [InlineData("Hi Capri", AvatarGesture.Appear)]
    [InlineData("look at my screen", AvatarGesture.TurnToScreen)]
    [InlineData("Can you look at my screen?", AvatarGesture.TurnToScreen)]
    [InlineData("capri look left", AvatarGesture.LookLeft)]
    [InlineData("look right", AvatarGesture.LookRight)]
    [InlineData("nod", AvatarGesture.Nod)]
    [InlineData("shake your head", AvatarGesture.Shake)]
    [InlineData("jump", AvatarGesture.Jump)]
    [InlineData("walk right", AvatarGesture.WalkRight)]
    [InlineData("celebrate", AvatarGesture.Celebrate)]
    public void TypedCommandsMapOnlyToVisualGestures(string text, AvatarGesture expected)
    {
        Assert.Equal(expected, AvatarGestureTextCueParser.Parse(text));
    }

    [Theory]
    [InlineData("please delete my files")]
    [InlineData("look at my screen and read my bank details")]
    [InlineData("hey capri; activate the webcam")]
    [InlineData("hey capri do stuff")]
    [InlineData("listen to me")]
    [InlineData("")]
    public void NaturalLanguageThatIsNotAnExactVisualCueIsIgnored(string text)
    {
        Assert.Null(AvatarGestureTextCueParser.Parse(text));
    }

    [Fact]
    public void ExcessivelyLongTextCannotBecomeGesture()
    {
        Assert.Null(AvatarGestureTextCueParser.Parse(new string('a', 121)));
        Assert.Null(AvatarGestureTextCueParser.Parse(null));
    }
}
