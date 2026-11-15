namespace Capri.Core.Avatar;

/// <summary>
/// Short, interruptible performance cues. All movement is contained inside the
/// companion window; the character does not move the user's applications.
/// Eye and mouth cues require real approved sprite frames rather than guessing
/// where painted facial features are on the user's art.
/// </summary>
public enum AvatarGesture
{
    Appear,
    Disappear,
    LookLeft,
    LookRight,
    LookAround,
    Nod,
    Shake,
    Hop,
    Jump,
    WalkLeft,
    WalkRight,
    TurnToScreen,
    LeanIn,
    Wave,
    Celebrate,
    Blink,
    MouthSpeak,
    SideEye
}

/// <param name="AtMs">Milliseconds into the clip.</param>
/// <param name="X">Horizontal translation inside the companion (DIP).</param>
/// <param name="Y">Vertical translation inside the companion (DIP).</param>
/// <param name="Angle">Sprite rotation in degrees.</param>
/// <param name="ScaleX">Horizontal scale; never mirror asymmetric artwork.</param>
/// <param name="ScaleY">Vertical scale.</param>
/// <param name="Opacity">Character alpha; no strobing.</param>
/// <param name="Echo">Soft ghost opacity on a second copy of the SAME approved artwork.</param>
public sealed record AvatarGestureFrame(
    int AtMs, double X = 0, double Y = 0, double Angle = 0,
    double ScaleX = 1, double ScaleY = 1, double Opacity = 1, double Echo = 0);

public sealed record AvatarGestureClip(
    AvatarGesture Gesture,
    int DurationMs,
    IReadOnlyList<AvatarGestureFrame> Frames,
    bool RequiresArtworkFrames = false);

/// <summary>
/// Distinct held poses and snappy transitions, with bounded displacement and
/// modest ghosting; reusable in WPF or a future animation compositor.
/// </summary>
public static class AvatarGesturePlanner
{
    private static AvatarGestureFrame F(
        int ms, double x = 0, double y = 0, double angle = 0,
        double sx = 1, double sy = 1, double opacity = 1, double echo = 0)
        => new(ms, x, y, angle, sx, sy, opacity, echo);

    public static AvatarGestureClip? ForGesture(AvatarGesture gesture, bool reduceMotion)
    {
        if (reduceMotion || !Enum.IsDefined(gesture)) return null;

        return gesture switch
        {
            AvatarGesture.Appear => new(gesture, 490, [
                F(0, y: 18, sx: .86, sy: .89, opacity: 0, echo: .20),
                F(105, y: -7, sx: 1.05, sy: 1.025, opacity: .88, echo: .24),
                F(175, y: -7, sx: 1.05, sy: 1.025, opacity: 1, echo: .14),
                F(275, y: 2, sx: .99, sy: .99, echo: .06),
                F(490)
            ]),
            AvatarGesture.Disappear => new(gesture, 360, [
                F(0), F(100, y: -5, sx: 1.04, sy: 1.03, echo: .20),
                F(170, y: -5, sx: 1.04, sy: 1.03, echo: .2),
                F(360, y: 18, sx: .85, sy: .9, opacity: 0)
            ]),
            AvatarGesture.LookLeft => new(gesture, 670, [
                F(0), F(130, x: -6, angle: -8, echo: .10),
                F(420, x: -6, angle: -8), F(670)
            ]),
            AvatarGesture.LookRight => new(gesture, 670, [
                F(0), F(130, x: 6, angle: 8, echo: .10),
                F(420, x: 6, angle: 8), F(670)
            ]),
            AvatarGesture.LookAround => new(gesture, 1240, [
                F(0), F(150, x: -7, angle: -8, echo: .12),
                F(380, x: -7, angle: -8), F(550, x: 6, angle: 7, echo: .13),
                F(820, x: 6, angle: 7), F(1020, x: -2, angle: -3), F(1240)
            ]),
            AvatarGesture.Nod => new(gesture, 700, [
                F(0), F(115, y: 4, angle: 4, sy: .985),
                F(245, y: -1, angle: -2), F(355, y: 4, angle: 4),
                F(505, y: -1, angle: -2), F(700)
            ]),
            AvatarGesture.Shake => new(gesture, 770, [
                F(0), F(125, x: -5, angle: -8, echo: .12),
                F(285, x: 5, angle: 8, echo: .12),
                F(440, x: -4, angle: -6), F(590, x: 3, angle: 4),
                F(770)
            ]),
            AvatarGesture.Hop => new(gesture, 720, [
                F(0), F(90, y: 3, sy: .96), F(250, y: -18, sy: 1.045, echo: .13),
                F(360, y: -18, sy: 1.045), F(520, y: 2, sy: .965),
                F(720)
            ]),
            AvatarGesture.Jump => new(gesture, 900, [
                F(0), F(135, y: 4, sy: .95), F(320, y: -22, sx: .98, sy: 1.05, echo: .20),
                F(510, y: -22, sx: .98, sy: 1.05), F(710, y: 4, sy: .94, echo: .12),
                F(900)
            ]),
            AvatarGesture.WalkLeft => Walk(gesture, -1),
            AvatarGesture.WalkRight => Walk(gesture, 1),
            AvatarGesture.TurnToScreen => new(gesture, 1250, [
                F(0), F(160, x: -5, y: -1, angle: -8, sx: .97, echo: .16),
                F(335, x: -9, y: 1, angle: -10, sx: .96),
                F(770, x: -9, y: 1, angle: -10, sx: .96),
                F(1030, x: -4, angle: -5), F(1250)
            ]),
            AvatarGesture.LeanIn => new(gesture, 850, [
                F(0), F(135, y: -4, sy: 1.04, sx: 1.035, echo: .10),
                F(520, y: -4, sy: 1.04, sx: 1.035), F(850)
            ]),
            AvatarGesture.Wave => new(gesture, 980, [
                F(0), F(130, x: 3, angle: 5, echo: .10),
                F(285, x: -2, angle: -4), F(430, x: 3, angle: 5),
                F(580, x: -2, angle: -4), F(740, x: 2, angle: 3),
                F(980)
            ]),
            AvatarGesture.Celebrate => new(gesture, 1070, [
                F(0), F(140, y: 3, angle: -4, sy: .95),
                F(330, y: -22, angle: 7, sy: 1.055, echo: .25),
                F(540, y: -18, angle: 4, sy: 1.025),
                F(740, y: 3, angle: -4, sy: .955, echo: .12),
                F(1070)
            ]),
            AvatarGesture.Blink or AvatarGesture.MouthSpeak or AvatarGesture.SideEye
                => new(gesture, gesture == AvatarGesture.MouthSpeak ? 600 : 460,
                    [F(0), F(gesture == AvatarGesture.MouthSpeak ? 600 : 460)],
                    RequiresArtworkFrames: true),
            _ => null
        };
    }

    private static AvatarGestureClip Walk(AvatarGesture gesture, int direction)
        => new(gesture, 1320, [
            F(0),
            F(160, x: 4 * direction, y: -5, angle: 4 * direction, echo: .12),
            F(315, x: 7 * direction, y: 1, angle: -3 * direction),
            F(480, x: 10 * direction, y: -5, angle: 4 * direction, echo: .10),
            F(680, x: 14 * direction, y: 1, angle: -3 * direction),
            F(840, x: 14 * direction, y: -4, angle: 3 * direction),
            F(1100, x: 6 * direction, angle: -2 * direction),
            F(1320)
        ]);
}
