namespace Capri.Core.Avatar;

/// <summary>
/// Recognizes only explicit user-authored TEXT commands for harmless visual
/// gestures. This is NOT a microphone hotword engine and does not confer screen
/// capture rights. A future voice detector can emit the same visual cue after
/// real local speech recognition and microphone consent.
/// </summary>
public static class AvatarGestureTextCueParser
{
    public static AvatarGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 120)
            return null;

        var phrase = text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
        return phrase switch
        {
            "hey capri" or "hi capri" or "hello capri" => AvatarGesture.Appear,
            "look at my screen" or "look at the screen" or
            "capri look at my screen" or "can you look at my screen" =>
                AvatarGesture.TurnToScreen,
            "look left" or "capri look left" => AvatarGesture.LookLeft,
            "look right" or "capri look right" => AvatarGesture.LookRight,
            "look around" or "capri look around" => AvatarGesture.LookAround,
            "nod" or "capri nod" => AvatarGesture.Nod,
            "shake your head" or "capri shake your head" => AvatarGesture.Shake,
            "jump" or "capri jump" => AvatarGesture.Jump,
            "hop" or "capri hop" => AvatarGesture.Hop,
            "walk left" or "capri walk left" => AvatarGesture.WalkLeft,
            "walk right" or "capri walk right" => AvatarGesture.WalkRight,
            "wave" or "capri wave" => AvatarGesture.Wave,
            "celebrate" or "capri celebrate" => AvatarGesture.Celebrate,
            _ => null
        };
    }
}
