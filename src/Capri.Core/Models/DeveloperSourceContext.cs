namespace Capri.Core.Models;

/// <summary>
/// A single file snapshot approved for ONE developer-model request. The caller
/// must explicitly confirm both local reading and sharing before constructing
/// this value; it is never loaded from saved session bookmarks or chat history.
/// </summary>
public sealed record DeveloperSourceContext(
    Guid SessionId, string RelativePath, string Content);
