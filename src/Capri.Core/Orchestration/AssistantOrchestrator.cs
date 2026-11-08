using System.Runtime.CompilerServices;
using System.Text;
using Capri.Core.Abstractions;
using Capri.Core.Abstractions.Automation;
using Capri.Core.Autonomy;
using Capri.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Core.Orchestration;

/// <summary>
/// Coordinates intent parsing, automation, and chat streaming for a single user turn.
/// </summary>
/// <remarks>
/// <para>
/// Intent parsing is applied only to <b>user-typed text</b>. Model output is streamed to the UI
/// and stored as conversation history, but it is never parsed as an automation intent.
/// </para>
/// <para>
/// When a deterministic automation intent is detected (launch app, open web, media control),
/// the orchestrator executes it through <see cref="IAutomationService"/> and does not call the
/// language model. This prevents prompt-injection from model output and keeps automation
/// deterministic.
/// </para>
/// </remarks>
public sealed partial class AssistantOrchestrator : IAssistantOrchestrator, IDisposable
{
    private const int MaxConversationMessages = 20;
    private const string SystemPrompt =
        "You are Capri, a helpful Windows desktop assistant. Be concise, friendly, and accurate. " +
        "Do not claim to have performed actions unless they were explicitly executed by the system.";

    private readonly IIntentParser _intentParser;
    private readonly IAutomationService _automationService;
    private readonly IModelRouter _modelRouter;
    private readonly IChatProvider _chatProvider;
    private readonly IConversationStore _conversationStore;
    private readonly IAvatarStateMachine _avatarStateMachine;
    private readonly ILogger<AssistantOrchestrator> _logger;
    private readonly TaskCoordinator? _tasks;
    private readonly SemaphoreSlim _turnGate = new(1, 1);

    /// <summary>
    /// Creates the orchestrator.
    /// </summary>
    public AssistantOrchestrator(
        IIntentParser intentParser,
        IAutomationService automationService,
        IModelRouter modelRouter,
        IChatProvider chatProvider,
        IConversationStore conversationStore,
        IAvatarStateMachine avatarStateMachine,
        ILogger<AssistantOrchestrator>? logger = null,
        TaskCoordinator? tasks = null)
    {
        _intentParser = intentParser ?? throw new ArgumentNullException(nameof(intentParser));
        _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
        _modelRouter = modelRouter ?? throw new ArgumentNullException(nameof(modelRouter));
        _chatProvider = chatProvider ?? throw new ArgumentNullException(nameof(chatProvider));
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _avatarStateMachine = avatarStateMachine ?? throw new ArgumentNullException(nameof(avatarStateMachine));
        _logger = logger ?? NullLogger<AssistantOrchestrator>.Instance;
        _tasks = tasks;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AssistantUpdate> StreamAsync(
        string userText,
        CancellationToken cancellationToken = default)
    {
        return StreamCoreAsync(userText, cancellationToken);
    }

    /// <inheritdoc />
    public void ResetConversation()
    {
        _conversationStore.Clear();
        _avatarStateMachine.Reset();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _turnGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async IAsyncEnumerable<AssistantUpdate> StreamCoreAsync(
        string userText,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var turnId = Guid.NewGuid().ToString("N")[..12];
        var acquired = false;

        try
        {
            await _turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            await foreach (var update in RunTurnAsync(userText, turnId, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return update;
            }
        }
        finally
        {
            if (acquired)
            {
                _turnGate.Release();
            }
        }
    }

    private async IAsyncEnumerable<AssistantUpdate> RunTurnAsync(
        string userText,
        string turnId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new AssistantUpdate(AssistantUpdateKind.Started);

        var input = userText ?? string.Empty;
        if (input.StartsWith("/task", StringComparison.OrdinalIgnoreCase) && (input.Length == 5 || char.IsWhiteSpace(input[5])))
        {
            if (_tasks is null)
            {
                yield return new AssistantUpdate(AssistantUpdateKind.Failed, Text: "Task service unavailable.");
                yield break;
            }
            await foreach (var progress in _tasks.RunAsync(input[5..].Trim(), cancellationToken).ConfigureAwait(false))
            {
                if (progress.Phase is TaskPhase.Failed or TaskPhase.Cancelled)
                    yield return new AssistantUpdate(AssistantUpdateKind.Failed, Text: progress.Message);
                else if (progress.Phase == TaskPhase.Completed)
                    yield return new AssistantUpdate(AssistantUpdateKind.Completed, Text: progress.Message);
                else
                    yield return new AssistantUpdate(AssistantUpdateKind.ToolExecuted, ToolResult: new AutomationResult(true, progress.Message));
            }
            yield break;
        }
        _conversationStore.Append(new ChatMessage(ChatRole.User, input));
        TryTransition(AvatarState.Thinking, AvatarTransitionReason.UserStoppedListening);

        // IMPORTANT: parse only user-typed text. Model output is never parsed.
        var intent = _intentParser.Parse(input);
        yield return new AssistantUpdate(AssistantUpdateKind.IntentDetected, DetectedIntent: intent);

        if (IsAutomationIntent(intent))
        {
            await foreach (var update in ExecuteAutomationAsync(intent, turnId, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return update;
            }

            yield break;
        }

        await foreach (var update in StreamChatAsync(turnId, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private async IAsyncEnumerable<AssistantUpdate> ExecuteAutomationAsync(
        Intent intent,
        string turnId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        AutomationResult result;

        try
        {
            result = await _automationService
                .ExecuteAsync(intent, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogAutomationExecutionFailed(_logger, exception, turnId);
            result = new AutomationResult(false, "The action could not be completed.");
        }

        yield return new AssistantUpdate(AssistantUpdateKind.ToolExecuted, ToolResult: result);

        var completionText = result.Succeeded
            ? (string.IsNullOrWhiteSpace(result.Message) ? "Done." : result.Message)
            : (string.IsNullOrWhiteSpace(result.Message) ? "I couldn't complete that action." : result.Message);

        // Store assistant feedback; this text is conversation history only and is never parsed as intent.
        _conversationStore.Append(new ChatMessage(ChatRole.Assistant, completionText));

        if (result.Succeeded)
        {
            TryTransition(AvatarState.Success, AvatarTransitionReason.ResponseCompleted);
            yield return new AssistantUpdate(
                AssistantUpdateKind.Completed,
                Text: completionText);
        }
        else
        {
            TryTransition(AvatarState.Error, AvatarTransitionReason.ErrorOccurred);
            yield return new AssistantUpdate(
                AssistantUpdateKind.Failed,
                Text: completionText,
                ToolResult: result,
                Error: new ProviderError(ProviderErrorKind.Unknown, $"Turn {turnId}: {completionText}"));
        }
    }

    private async IAsyncEnumerable<AssistantUpdate> StreamChatAsync(
        string turnId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = BuildChatRequest();

        ModelRoute? route = null;
        ProviderError? setupFailure = null;

        try
        {
            route = await _modelRouter
                .ResolveAsync(new ModelRouteRequest(PreferredModelId: request.ModelId), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogRoutingFailed(_logger, exception, turnId);
            setupFailure = CreateFriendlyFailure(turnId, "I couldn't select a model for that request.");
        }

        if (setupFailure is not null)
        {
            TryTransition(AvatarState.Error, AvatarTransitionReason.ErrorOccurred);
            yield return new AssistantUpdate(AssistantUpdateKind.Failed, Text: setupFailure.Message, Error: setupFailure);
            yield break;
        }

        var routedRequest = request with { ModelId = route!.ModelId };
        var assistantText = new StringBuilder();
        var talking = false;
        ProviderError? streamError = null;

        IAsyncEnumerator<ChatDelta>? enumerator = null;
        try
        {
            enumerator = _chatProvider
                .StreamAsync(routedRequest, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogProviderStreamFailed(_logger, exception, turnId);
            setupFailure = CreateFriendlyFailure(turnId, "I couldn't reach the chat provider.");
        }

        if (enumerator is null)
        {
            var failure = setupFailure ?? CreateFriendlyFailure(turnId, "I couldn't reach the chat provider.");
            _conversationStore.Append(new ChatMessage(ChatRole.Assistant, failure.Message));
            TryTransition(AvatarState.Error, AvatarTransitionReason.ErrorOccurred);
            yield return new AssistantUpdate(AssistantUpdateKind.Failed, Text: failure.Message, Error: failure);
            yield break;
        }

        try
        {
            while (true)
            {
                ChatDelta delta;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    delta = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogProviderStreamFailed(_logger, exception, turnId);
                    streamError ??= CreateFriendlyFailure(turnId, "The chat provider stopped responding.");
                    break;
                }

                if (delta.Error is not null)
                {
                    streamError = CreateFriendlyFailure(turnId, "The chat provider reported an error.");
                    break;
                }

                if (!string.IsNullOrEmpty(delta.Content))
                {
                    if (!talking)
                    {
                        talking = true;
                        TryTransition(AvatarState.Talking, AvatarTransitionReason.ResponseStarted);
                    }

                    assistantText.Append(delta.Content);
                    // Model output is surfaced as text only; it is never parsed as an intent.
                    yield return new AssistantUpdate(AssistantUpdateKind.TextDelta, Text: delta.Content);
                }

            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        if (streamError is not null)
        {
            var message = streamError.Message;
            if (assistantText.Length > 0)
            {
                // Preserve any partial response as history; it remains non-executable text.
                _conversationStore.Append(new ChatMessage(ChatRole.Assistant, assistantText.ToString()));
            }
            else
            {
                _conversationStore.Append(new ChatMessage(ChatRole.Assistant, message));
            }

            TryTransition(AvatarState.Error, AvatarTransitionReason.ErrorOccurred);
            yield return new AssistantUpdate(AssistantUpdateKind.Failed, Text: message, Error: streamError);
            yield break;
        }

        var finalText = assistantText.ToString();
        _conversationStore.Append(new ChatMessage(ChatRole.Assistant, finalText));
        TryTransition(AvatarState.Success, AvatarTransitionReason.ResponseCompleted);

        var completedText = finalText.Length == 0 ? null : finalText;
        yield return new AssistantUpdate(AssistantUpdateKind.Completed, Text: completedText);
    }

    private ChatRequest BuildChatRequest()
    {
        var history = _conversationStore.Messages;
        var window = TrimConversationWindow(history);
        var messages = new List<ChatMessage>(window.Count + 1)
        {
            new(ChatRole.System, SystemPrompt),
        };
        messages.AddRange(window);

        return new ChatRequest(messages);
    }

    private static IReadOnlyList<ChatMessage> TrimConversationWindow(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count <= MaxConversationMessages)
        {
            return messages;
        }

        var start = messages.Count - MaxConversationMessages;
        var window = new ChatMessage[MaxConversationMessages];
        for (var i = 0; i < window.Length; i++)
        {
            window[i] = messages[start + i];
        }

        return window;
    }

    private static bool IsAutomationIntent(Intent intent)
    {
        return intent.Kind is IntentKind.LaunchApp or IntentKind.OpenWeb or IntentKind.MediaControl;
    }

    private static ProviderError CreateFriendlyFailure(string turnId, string message)
    {
        // Never include provider payloads, credentials, or raw exception text.
        return new ProviderError(
            ProviderErrorKind.Unknown,
            $"Turn {turnId}: {message}");
    }

    private void TryTransition(AvatarState nextState, AvatarTransitionReason reason)
    {
        try
        {
            _avatarStateMachine.TryTransition(nextState, reason);
        }
        catch (Exception exception)
        {
            LogAvatarTransitionFailed(_logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Automation execution failed for turn {TurnId}.")]
    private static partial void LogAutomationExecutionFailed(ILogger logger, Exception exception, string turnId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Model routing failed for turn {TurnId}.")]
    private static partial void LogRoutingFailed(ILogger logger, Exception exception, string turnId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Chat provider streaming failed for turn {TurnId}.")]
    private static partial void LogProviderStreamFailed(ILogger logger, Exception exception, string turnId);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "Avatar transition threw an exception and was ignored.")]
    private static partial void LogAvatarTransitionFailed(ILogger logger, Exception exception);
}
