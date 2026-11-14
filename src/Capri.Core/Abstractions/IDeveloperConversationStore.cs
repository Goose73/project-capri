using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>Private developer transcript journal, shared by WPF and CLI clients.</summary>
public interface IDeveloperConversationStore
{
    IReadOnlyList<DeveloperConversationEntry> Read(Guid sessionId, int maxCount = 40);
    DeveloperConversationEntry Append(Guid sessionId, ChatRole role, string content);
}
