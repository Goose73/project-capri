namespace Capri.Core.Models;

/// <summary>
/// Kind of progress notification emitted by the assistant pipeline.
/// </summary>
public enum AssistantUpdateKind
{
    Started = 0,
    IntentDetected,
    TextDelta,
    ToolExecuted,
    Completed,
    Failed
}

/// <summary>
/// A single progress notification emitted while handling an assistant turn.
/// </summary>
/// <param name="Kind">The kind of progress being reported.</param>
/// <param name="Text">Optional text payload, such as a streamed fragment.</param>
/// <param name="DetectedIntent">Intent details for <see cref="AssistantUpdateKind.IntentDetected"/>.</param>
/// <param name="ToolResult">Automation outcome for <see cref="AssistantUpdateKind.ToolExecuted"/>.</param>
/// <param name="Error">Failure details for <see cref="AssistantUpdateKind.Failed"/>.</param>
public sealed record AssistantUpdate(
    AssistantUpdateKind Kind,
    string? Text = null,
    Intent? DetectedIntent = null,
    AutomationResult? ToolResult = null,
    ProviderError? Error = null);
