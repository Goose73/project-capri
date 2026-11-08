using System.Runtime.CompilerServices;
using Capri.Core.Abstractions;
using Capri.Core.Abstractions.Automation;
using Capri.Core.Avatar;
using Capri.Core.Conversation;
using Capri.Core.Intents;
using Capri.Core.Models;
using Capri.Core.Orchestration;

namespace Capri.Tests.Core;

public sealed class AssistantOrchestratorTests
{
    [Fact]
    public async Task AutomationIntentDoesNotCallProviderAndExecutesAutomation()
    {
        var provider = new FakeChatProvider();
        var automation = new FakeAutomationService
        {
            Result = new AutomationResult(true, "Opened Brave."),
        };
        var orchestrator = CreateOrchestrator(provider, automation);

        var updates = await CollectAsync(orchestrator.StreamAsync("open brave"));

        Assert.Equal(0, provider.StreamCalls);
        Assert.Equal(1, automation.ExecuteCalls);
        Assert.NotNull(automation.LastIntent);
        Assert.Equal(IntentKind.LaunchApp, automation.LastIntent!.Kind);
        Assert.Equal("brave", automation.LastIntent.Target);

        Assert.Contains(updates, u => u.Kind == AssistantUpdateKind.Started);
        Assert.Contains(updates, u => u.Kind == AssistantUpdateKind.IntentDetected && u.DetectedIntent?.Kind == IntentKind.LaunchApp);
        Assert.Contains(updates, u => u.Kind == AssistantUpdateKind.ToolExecuted && u.ToolResult?.Succeeded == true);
        Assert.Contains(updates, u => u.Kind == AssistantUpdateKind.Completed);
    }

    [Fact]
    public async Task WebAutomationIntentCallsAutomationService()
    {
        var provider = new FakeChatProvider();
        var automation = new FakeAutomationService
        {
            Result = new AutomationResult(true, "Opened page."),
        };
        var orchestrator = CreateOrchestrator(provider, automation);

        await CollectAsync(orchestrator.StreamAsync("open https://example.com"));

        Assert.Equal(0, provider.StreamCalls);
        Assert.Equal(1, automation.ExecuteCalls);
        Assert.Equal(IntentKind.OpenWeb, automation.LastIntent?.Kind);
    }

    [Fact]
    public async Task ChatPathStreamsDeltasStoresAssistantMessageAndDoesNotCallAutomation()
    {
        var provider = new FakeChatProvider(
            new ChatDelta("Hello", ChatRole.Assistant),
            new ChatDelta(" world", FinishReason: "stop"));
        var automation = new FakeAutomationService();
        var store = new InMemoryConversationStore();
        var orchestrator = CreateOrchestrator(provider, automation, conversationStore: store);

        var updates = await CollectAsync(orchestrator.StreamAsync("tell me a joke"));

        Assert.Equal(0, automation.ExecuteCalls);
        Assert.Equal(1, provider.StreamCalls);

        var deltas = updates.Where(u => u.Kind == AssistantUpdateKind.TextDelta).Select(u => u.Text).ToArray();
        string?[] expected = ["Hello", " world"];
        Assert.Equal(expected, deltas);
        Assert.Contains(updates, u => u.Kind == AssistantUpdateKind.Completed);

        var assistantMessages = store.Messages.Where(m => m.Role == ChatRole.Assistant).ToArray();
        var assistant = Assert.Single(assistantMessages);
        Assert.Equal("Hello world", assistant.Content);
    }

    [Fact]
    public async Task ProviderErrorYieldsFailedUpdateAndErrorAvatarState()
    {
        var provider = new FakeChatProvider(
            new ChatDelta(string.Empty, Error: new ProviderError(ProviderErrorKind.Authentication, "bad key")));
        var automation = new FakeAutomationService();
        var avatar = new AvatarStateMachine();
        var orchestrator = CreateOrchestrator(provider, automation, avatarStateMachine: avatar);

        var updates = await CollectAsync(orchestrator.StreamAsync("hello"));

        var failed = Assert.Single(updates, u => u.Kind == AssistantUpdateKind.Failed);
        Assert.NotNull(failed.Error);
        Assert.DoesNotContain("bad key", failed.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Turn ", failed.Error.Message, StringComparison.Ordinal);
        Assert.Equal(AvatarState.Error, avatar.Current);
    }

    [Fact]
    public async Task CancellationStopsTurn()
    {
        var provider = new BlockingChatProvider();
        var automation = new FakeAutomationService();
        var orchestrator = CreateOrchestrator(provider, automation);
        using var cts = new CancellationTokenSource();

        var updates = new List<AssistantUpdate>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in orchestrator.StreamAsync("hello", cts.Token))
            {
                updates.Add(update);
                if (update.Kind == AssistantUpdateKind.IntentDetected)
                {
                    await cts.CancelAsync();
                }
            }
        });

        Assert.Equal(0, automation.ExecuteCalls);
    }

    [Fact]
    public async Task ModelOutputCannotTriggerAutomationByDesign()
    {
        // Even if the model outputs text that looks like a command, the orchestrator never parses it.
        var provider = new FakeChatProvider(
            new ChatDelta("open brave", ChatRole.Assistant, FinishReason: "stop"));
        var automation = new FakeAutomationService();
        var orchestrator = CreateOrchestrator(provider, automation);

        var updates = await CollectAsync(orchestrator.StreamAsync("what should I do?"));

        Assert.Equal(0, automation.ExecuteCalls);
        Assert.Contains(updates, u => u.Kind == AssistantUpdateKind.TextDelta && u.Text == "open brave");
        Assert.DoesNotContain(updates, u => u.Kind == AssistantUpdateKind.ToolExecuted);
    }

    [Fact]
    public async Task ResetConversationClearsStoreAndResetsAvatar()
    {
        var provider = new FakeChatProvider(new ChatDelta("ok", FinishReason: "stop"));
        var automation = new FakeAutomationService();
        var store = new InMemoryConversationStore();
        var avatar = new AvatarStateMachine();
        var orchestrator = CreateOrchestrator(provider, automation, conversationStore: store, avatarStateMachine: avatar);

        await CollectAsync(orchestrator.StreamAsync("hello"));
        Assert.NotEmpty(store.Messages);
        Assert.NotEqual(AvatarState.Idle, avatar.Current);

        orchestrator.ResetConversation();

        Assert.Empty(store.Messages);
        Assert.Equal(AvatarState.Idle, avatar.Current);
    }

    [Fact]
    public async Task UserTextIsParsedAndStoredAsUserMessage()
    {
        var provider = new FakeChatProvider(new ChatDelta("ok", FinishReason: "stop"));
        var automation = new FakeAutomationService();
        var store = new InMemoryConversationStore();
        var parser = new RecordingIntentParser(new RuleBasedIntentParser());
        var orchestrator = CreateOrchestrator(
            provider,
            automation,
            conversationStore: store,
            intentParser: parser);

        const string input = "tell me something";
        await CollectAsync(orchestrator.StreamAsync(input));

        Assert.Equal([input], parser.ParsedInputs);
        Assert.Contains(store.Messages, m => m.Role == ChatRole.User && m.Content == input);
    }

    private static AssistantOrchestrator CreateOrchestrator(
        IChatProvider chatProvider,
        IAutomationService automationService,
        IConversationStore? conversationStore = null,
        IAvatarStateMachine? avatarStateMachine = null,
        IIntentParser? intentParser = null,
        IModelRouter? modelRouter = null)
    {
        return new AssistantOrchestrator(
            intentParser ?? new RuleBasedIntentParser(),
            automationService,
            modelRouter ?? new FakeModelRouter(),
            chatProvider,
            conversationStore ?? new InMemoryConversationStore(),
            avatarStateMachine ?? new AvatarStateMachine());
    }

    private static async Task<List<AssistantUpdate>> CollectAsync(IAsyncEnumerable<AssistantUpdate> updates)
    {
        var list = new List<AssistantUpdate>();
        await foreach (var update in updates)
        {
            list.Add(update);
        }

        return list;
    }

    private sealed class FakeModelRouter : IModelRouter
    {
        public int ResolveCalls { get; private set; }

        public ValueTask<ModelRoute> ResolveAsync(ModelRouteRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCalls++;
            return ValueTask.FromResult(new ModelRoute("test-provider", request.PreferredModelId ?? "test-model"));
        }
    }

    private sealed class FakeChatProvider : IChatProvider
    {
        private readonly IReadOnlyList<ChatDelta> _deltas;

        public FakeChatProvider(params ChatDelta[] deltas)
        {
            _deltas = deltas;
        }

        public int StreamCalls { get; private set; }

        public string ProviderId => "test-provider";

        public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Streaming path is used by these tests.");
        }

        public async IAsyncEnumerable<ChatDelta> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamCalls++;
            foreach (var delta in _deltas)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return delta;
                await Task.Yield();
            }
        }

        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ProviderHealth(true));
    }

    private sealed class BlockingChatProvider : IChatProvider
    {
        public string ProviderId => "blocking";

        public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatDelta> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            yield break;
        }

        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ProviderHealth(true));
    }

    private sealed class FakeAutomationService : IAutomationService
    {
        public int ExecuteCalls { get; private set; }

        public Intent? LastIntent { get; private set; }

        public AutomationResult Result { get; set; } = new(true, "ok");

        public Task<AutomationResult> ExecuteAsync(Intent intent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCalls++;
            LastIntent = intent;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingIntentParser : IIntentParser
    {
        private readonly IIntentParser _inner;

        public RecordingIntentParser(IIntentParser inner)
        {
            _inner = inner;
        }

        public List<string> ParsedInputs { get; } = [];

        public Intent Parse(string input)
        {
            ParsedInputs.Add(input);
            return _inner.Parse(input);
        }
    }
}
