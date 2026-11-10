namespace Capri.Core.Companion;

/// <summary>
/// Stored preferences for a future webcam-based ambient companion. Persisted
/// preferences never authorize camera use by themselves: a separate, fresh,
/// in-memory user-consent session is required on every launch.
/// No camera capture implementation is registered in this milestone.
/// </summary>
public sealed record AmbientVisionSettings
{
    /// <summary>Opt-in preference; false for every existing installation.</summary>
    public bool Enabled { get; init; }

    /// <summary>Separate opt-in for unprompted remarks; false by default.</summary>
    public bool AllowSpontaneousComments { get; init; }

    /// <summary>Seconds between brief local webcam snapshots, not video frames.</summary>
    public int SampleIntervalSeconds { get; init; } = 20;

    /// <summary>Minimum seconds between unprompted comments.</summary>
    public int MinimumCommentIntervalSeconds { get; init; } = 300;

    /// <summary>Selected local webcam device index (0–4).</summary>
    public int CameraIndex { get; init; }

    /// <summary>Locally installed Ollama vision-capable model.</summary>
    public string VisionModel { get; init; } = "qwen3.5:4b";

    /// <summary>Separately opt into audible spontaneous comments.</summary>
    public bool SpeakRemarks { get; init; }

    /// <summary>Do not send ambient frames to cloud model providers.</summary>
    public bool LocalOnly { get; init; } = true;
}

/// <summary>
/// Runtime conditions verified immediately before each frame or remark.
/// Never construct SessionConsentGiven from persisted settings or a model
/// response. VisibleIndicatorActive must reflect an actual user-facing cue.
/// </summary>
public sealed record AmbientVisionRuntime(
    bool SessionConsentGiven,
    bool WindowsCameraAccessGranted,
    bool CameraAvailable,
    bool VisibleIndicatorActive,
    bool LocalVisionModelAvailable,
    bool Paused,
    bool SessionLocked,
    bool CaptureInProgress,
    DateTimeOffset? LastCapturedUtc,
    DateTimeOffset? LastCommentUtc,
    bool NewMeaningfulObservation,
    bool UserIsBusy);

public sealed record AmbientVisionDecision(bool CaptureFrame, bool MakeSpontaneousRemark, string Reason);

/// <summary>
/// Pure fail-closed decision policy, independent of camera drivers and model
/// adapters. No camera access, recording, network traffic or speech here.
/// </summary>
public static class AmbientVisionPolicy
{
    public static AmbientVisionDecision Evaluate(
        AmbientVisionSettings? settings,
        AmbientVisionRuntime? runtime,
        DateTimeOffset utcNow)
    {
        if (settings is null || runtime is null || !settings.Enabled)
            return Block("Ambient webcam mode is disabled.");

        // The local-only requirement cannot be removed through configuration.
        if (!settings.LocalOnly || !runtime.LocalVisionModelAvailable)
            return Block("A compatible local vision model is required.");
        if (!runtime.SessionConsentGiven)
            return Block("Fresh webcam consent is required for this session.");
        if (!runtime.WindowsCameraAccessGranted || !runtime.CameraAvailable)
            return Block("Webcam access or hardware is unavailable.");
        if (!runtime.VisibleIndicatorActive)
            return Block("The active-camera indicator must be visible.");
        if (runtime.Paused || runtime.SessionLocked)
            return Block("Ambient observation is paused or the session is locked.");
        if (runtime.CaptureInProgress)
            return Block("An observation is already in progress.");

        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.SampleIntervalSeconds, 10, 3600));
        var sinceCapture = runtime.LastCapturedUtc.HasValue
            ? utcNow - runtime.LastCapturedUtc.Value
            : TimeSpan.MaxValue;
        var canCapture = sinceCapture >= interval;

        // A remark is permitted only after meaningful successful vision work.
        // Do not remark from stale data, on every frame, or while the user is busy.
        var cooldown = TimeSpan.FromSeconds(Math.Clamp(settings.MinimumCommentIntervalSeconds, 120, 86400));
        var sinceRemark = runtime.LastCommentUtc.HasValue
            ? utcNow - runtime.LastCommentUtc.Value
            : TimeSpan.MaxValue;
        var canComment = settings.AllowSpontaneousComments
            && runtime.NewMeaningfulObservation
            && runtime.LastCapturedUtc.HasValue
            && runtime.LastCapturedUtc.Value <= utcNow
            && sinceCapture <= TimeSpan.FromMinutes(2)
            && sinceRemark >= cooldown
            && !runtime.UserIsBusy;

        return new AmbientVisionDecision(canCapture, canComment,
            canCapture || canComment ? "Eligible for a local observation or optional remark."
                : "Waiting for the next observation or commentary interval.");
    }

    private static AmbientVisionDecision Block(string reason) => new(false, false, reason);
}
