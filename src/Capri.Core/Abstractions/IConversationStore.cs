using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Holds the messages of the active conversation.
/// </summary>
public interface IConversationStore
{
    /// <summary>
    /// Current messages in chronological order.
    /// </summary>
    IReadOnlyList<ChatMessage> Messages { get; }

    /// <summary>
    /// Appends a message to the conversation.
    /// </summary>
    void Append(ChatMessage message);

    /// <summary>
    /// Removes all messages from the conversation.
    /// </summary>
    void Clear();
}
