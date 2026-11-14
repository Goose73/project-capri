namespace Capri.Core.Models;

/// <summary>
/// One persisted message in a specific developer session. No workspace source is
/// included unless the user explicitly types it in a message.
/// </summary>
public sealed record DeveloperConversationEntry(
    Guid Id, Guid SessionId, ChatRole Role, string Content, DateTimeOffset CreatedUtc);
