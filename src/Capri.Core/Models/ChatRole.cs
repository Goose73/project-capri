namespace Capri.Core.Models;

/// <summary>
/// Role of a message in a chat conversation.
/// </summary>
public enum ChatRole
{
    System = 0,
    User = 1,
    Assistant = 2,
    Tool = 3
}
