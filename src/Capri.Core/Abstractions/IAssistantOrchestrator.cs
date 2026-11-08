using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Coordinates intent detection, permissions, automation, and chat streaming.
/// </summary>
public interface IAssistantOrchestrator
{
    /// <summary>
    /// Processes user text and yields progress updates as they occur.
    /// </summary>
    IAsyncEnumerable<AssistantUpdate> StreamAsync(string userText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the active conversation and derived state.
    /// </summary>
    void ResetConversation();
}
