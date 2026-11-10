using Capri.Core.Models;

namespace Capri.Core.Avatar;

/// <summary>
/// Presentation-only reactions. A cue is never evidence of microphone access,
/// screenshot capture, task completion, or permission to perform an action.
/// </summary>
public enum AvatarCueKind
{
    None,
    Summon,
    Dismiss,
    ScreenRequested,
    ReturnToUser,
    GlanceLeft,
    GlanceRight,
    LookAround,
    Nod,
    Shake,
    Hop,
    LeanIn,
    StepLeft,
    StepRight,
    Wink,
    Blink,
    SpeakEmphasis
}

public readonly record struct AvatarGaze(double X, double Y);

/// <summary>
/// Deterministic, testable motion selection. No input device is read and no
/// request is interpreted as proof that Capri actually saw the screen.
/// </summary>
public static class AvatarCuePlanner
{
    private static readonly string[] ScreenReferences =
    [
        "my screen", "the screen", "this screen", "on screen", "my monitor",
        "this monitor", "my display", "this display", "my desktop",
        "this window", "my window", "what's on my screen", "screenshot"
    ];

    private static readonly string[] RequestVerbs =
    [
        "look", "see", "watch", "read", "check", "inspect",
        "analyse", "analyze", "describe", "what", "tell", "explain",
        "help", "can you", "show", "take", "capture"
    ];

    /// <summary>
    /// Recognises a user request for attention toward a display. It requests
    /// an animation ONLY, not screenshot, screen permission or inspection.
    /// </summary>
    public static bool RequestsScreenAttention(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.Trim().ToLowerInvariant();
        if (lower.Length > 4096)
            return false;

        if (lower.Contains("don't look", StringComparison.Ordinal) ||
            lower.Contains("do not look", StringComparison.Ordinal) ||
            lower.Contains("stop looking", StringComparison.Ordinal) ||
            lower.Contains("without looking", StringComparison.Ordinal))
            return false;

        return ScreenReferences.Any(lower.Contains) &&
               RequestVerbs.Any(lower.Contains);
    }

    /// <summary>Maps actual visual states, not speculative AI text, to a short reaction.</summary>
    public static AvatarCueKind ForState(AvatarState state) => state switch
    {
        AvatarState.Listening => AvatarCueKind.LeanIn,
        AvatarState.Thinking => AvatarCueKind.LookAround,
        AvatarState.Talking => AvatarCueKind.SpeakEmphasis,
        AvatarState.Success or AvatarState.Happy => AvatarCueKind.Hop,
        AvatarState.Error or AvatarState.Annoyed => AvatarCueKind.Shake,
        AvatarState.Surprised => AvatarCueKind.Hop,
        _ => AvatarCueKind.None
    };

    /// <summary>
    /// Device-independent gaze vector for eyes; fallback head tilt only when
    /// the optional independent-eye layer is not supplied.
    /// </summary>
    public static AvatarGaze LimitGaze(double x, double y, bool reduceMotion)
    {
        if (reduceMotion || !double.IsFinite(x) || !double.IsFinite(y))
            return default;

        return new AvatarGaze(Math.Clamp(x, -1, 1), Math.Clamp(y, -1, 1));
    }

    public static bool ShouldAnimate(AvatarCueKind kind, bool reduceMotion, bool hasArtwork) =>
        kind != AvatarCueKind.None && !reduceMotion && hasArtwork;
}
