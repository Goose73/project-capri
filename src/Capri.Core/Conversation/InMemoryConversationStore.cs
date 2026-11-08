using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Core.Conversation;

/// <summary>
/// Thread-safe in-memory conversation store suitable for a single UI session.
/// Readers receive an immutable snapshot so enumeration cannot observe a
/// concurrent mutation.
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly object _sync = new();
    private readonly List<ChatMessage> _messages = [];

    /// <inheritdoc />
    public IReadOnlyList<ChatMessage> Messages
    {
        get
        {
            lock (_sync)
            {
                return _messages.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void Append(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_sync)
        {
            _messages.Add(message);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_sync)
        {
            _messages.Clear();
        }
    }
}
