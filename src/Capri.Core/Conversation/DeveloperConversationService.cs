using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Core.Conversation;

/// <summary>
/// Explicit developer conversation requests using the existing configured
/// provider and its cloud/free-model consent guards. No workspace file is read
/// or attached to the model. Tool execution and edits are not supported.
/// </summary>
public sealed class DeveloperConversationService
{
    private readonly IDeveloperSessionStore _sessions;
    private readonly IDeveloperConversationStore _history;
    private readonly IChatProvider _provider;
    private readonly IModelRouter _router;

    public DeveloperConversationService(
        IDeveloperSessionStore sessions, IDeveloperConversationStore history,
        IChatProvider provider, IModelRouter router)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    public async Task<DeveloperConversationEntry> SendAsync(
        Guid sessionId, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sessionId == Guid.Empty || _sessions.Find(sessionId) is null)
            throw new InvalidOperationException("The developer session no longer exists.");
        if (string.IsNullOrWhiteSpace(message) || message.Length > 8000)
            throw new ArgumentException("Enter a message between 1 and 8000 characters.", nameof(message));

        // Route selection occurs BEFORE any transcript mutation; provider policy
        // still enforces cloud consent and local/free model restrictions.
        var route = await _router.ResolveAsync(new ModelRouteRequest(), cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(route.ModelId))
            throw new InvalidOperationException("Configure an AI provider and model before chatting.");

        var previous = _history.Read(sessionId, maxCount: 24);
        var messages = previous.Select(x => new ChatMessage(x.Role, x.Content))
            .ToList();
        messages.Add(new ChatMessage(ChatRole.User, message));

        // Persist the user's request so a network failure does not erase the
        // conversation. Only a completed nonempty response is persisted as AI.
        _history.Append(sessionId, ChatRole.User, message);
        var completion = await _provider.CompleteAsync(
            new ChatRequest(messages, ModelId: route.ModelId),
            cancellationToken).ConfigureAwait(false);
        if (completion.Error is not null)
            throw new InvalidOperationException(
                "The provider could not answer. Check provider configuration, consent, and connectivity.");
        var response = completion.Message.Content;
        if (string.IsNullOrWhiteSpace(response))
            throw new InvalidOperationException("The provider returned an empty response.");
        if (response.Length > 8000)
            throw new InvalidOperationException("The model response exceeded the safe transcript limit.");

        cancellationToken.ThrowIfCancellationRequested();
        return _history.Append(sessionId, ChatRole.Assistant, response);
    }
}
