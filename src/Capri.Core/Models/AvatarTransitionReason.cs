namespace Capri.Core.Models;

/// <summary>
/// Reason an avatar state transition was requested.
/// </summary>
public enum AvatarTransitionReason
{
    Unknown = 0,
    UserStartedListening,
    UserStoppedListening,
    SpeechRecognized,
    IntentDetected,
    ResponseStarted,
    ResponseCompleted,
    UserInterrupted,
    ErrorOccurred,
    Timeout,
    Reset
}
