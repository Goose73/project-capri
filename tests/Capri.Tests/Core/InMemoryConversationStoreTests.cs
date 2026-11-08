using Capri.Core.Conversation;
using Capri.Core.Models;

namespace Capri.Tests.Core;

public sealed class InMemoryConversationStoreTests
{
    [Fact]
    public void StartsEmpty()
    {
        var store = new InMemoryConversationStore();

        Assert.Empty(store.Messages);
    }

    [Fact]
    public void AppendAddsMessagesInOrder()
    {
        var store = new InMemoryConversationStore();
        var first = new ChatMessage(ChatRole.User, "hi");
        var second = new ChatMessage(ChatRole.Assistant, "hello");

        store.Append(first);
        store.Append(second);

        Assert.Equal([first, second], store.Messages);
    }

    [Fact]
    public void MessagesReturnsSnapshot()
    {
        var store = new InMemoryConversationStore();
        store.Append(new ChatMessage(ChatRole.User, "one"));

        var snapshot = store.Messages;
        store.Append(new ChatMessage(ChatRole.User, "two"));

        Assert.Single(snapshot);
        Assert.Equal(2, store.Messages.Count);
    }

    [Fact]
    public void ClearRemovesAllMessages()
    {
        var store = new InMemoryConversationStore();
        store.Append(new ChatMessage(ChatRole.User, "hi"));
        store.Append(new ChatMessage(ChatRole.Assistant, "hello"));

        store.Clear();

        Assert.Empty(store.Messages);
    }
}
