using Capri.Core.Models;

namespace Capri.Core.Avatar;

/// <summary>
/// Lightweight, artwork-independent motion vocabulary for the desktop companion.
/// Whole-sprite transforms are a temporary fallback until separately rigged,
/// authorized character art supports real head, eye, mouth and limb animation.
/// </summary>
public enum AvatarMotionKind
{
    Still,
    Breathe,
    Attend,
    Scan,
    Speak,
    Celebrate,
    Disagree,
    React
}

/// <summary>Bounded whole-sprite motion values in WPF device-independent pixels and degrees.</summary>
public sealed record AvatarMotionProfile(
    AvatarMotionKind Kind,
    double BobPixels,
    double SwayPixels,
    double TiltDegrees,
    double StretchY,
    int HalfCycleMilliseconds,
    bool Animated);

/// <summary>
/// Maps actual assistant state into safe visual expressions. Never claims screen
/// inspection, voice detection or tool success merely because a motion plays.
/// </summary>
public static class AvatarMotionPlanner
{
    private static readonly AvatarMotionProfile Still =
        new(AvatarMotionKind.Still, 0, 0, 0, 1, 1000, false);

    public static AvatarMotionProfile ForState(AvatarState state, bool reduceMotion)
    {
        if (reduceMotion || state == AvatarState.Sleep)
            return Still;

        return state switch
        {
            AvatarState.Listening => new(AvatarMotionKind.Attend, -1, 2.0, 2.0, 1.008, 650, true),
            AvatarState.Thinking => new(AvatarMotionKind.Scan, -1.6, 3.0, 3.0, 1.006, 950, true),
            AvatarState.Talking => new(AvatarMotionKind.Speak, -1.4, 0.7, 0.8, 1.018, 220, true),
            AvatarState.Success or AvatarState.Happy =>
                new(AvatarMotionKind.Celebrate, -5.0, 0.8, 2.5, 1.035, 350, true),
            AvatarState.Error or AvatarState.Annoyed =>
                new(AvatarMotionKind.Disagree, -0.8, 2.0, 3.0, 1.0, 600, true),
            AvatarState.Surprised =>
                new(AvatarMotionKind.React, -6.0, 1.0, 4.0, 1.042, 260, true),
            _ => new(AvatarMotionKind.Breathe, -1.8, 0.5, 0.5, 1.006, 1450, true)
        };
    }
}
