using System.IO;
using Capri.Core.Abstractions;
using Capri.Core.Conversation;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Core;

public sealed class DeveloperConversationServiceTests
{
    [Fact]
    public async Task SendsRealModelRequestsAndRestoresPreviousSessionTurns()
    {
        using var temp = new TempDirectory();
        var workspace = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(workspace);
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var session = sessions.Create(workspace);
        var transcript = new FileDeveloperConversationStore(paths, sessions);
        var provider = new FakeProvider();
        var service = new DeveloperConversationService(sessions, transcript, provider, new FakeRouter());

        var reply1 = await service.SendAsync(session.Id, "Help with a C# project");
        var reply2 = await service.SendAsync(session.Id, "Continue the explanation");

        Assert.Equal(ChatRole.Assistant, reply1.Role);
        Assert.Equal(ChatRole.Assistant, reply2.Role);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal(["Help with a C# project"],
            provider.Requests[0].Messages.Select(x => x.Content));
        Assert.Equal(["Help with a C# project", "Reply 1", "Continue the explanation"],
            provider.Requests[1].Messages.Select(x => x.Content));
        Assert.All(provider.Requests, x => Assert.Equal("local-model", x.ModelId));
        Assert.Equal(4, transcript.Read(session.Id).Count);
        Assert.Equal(4, new FileDeveloperConversationStore(paths, sessions).Read(session.Id).Count);
    }

    [Fact]
    public async Task NoProjectFilesAreAttachedWithoutSeparatePermission()
    {
        using var temp = new TempDirectory();
        var folder = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "source.cs"), "UNIQUE_SECRET_SOURCE_MARKER");
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var session = sessions.Create(folder);
        var store = new FileDeveloperConversationStore(paths, sessions);
        var provider = new FakeProvider();
        var service = new DeveloperConversationService(sessions, store, provider, new FakeRouter());

        await service.SendAsync(session.Id, "Describe unit tests");

        var sent = Assert.Single(provider.Requests);
        Assert.DoesNotContain(sent.Messages, x =>
            x.Content.Contains("UNIQUE_SECRET_SOURCE_MARKER", StringComparison.Ordinal));
        Assert.All(sent.Messages, x => Assert.Equal(ChatRole.User, x.Role));
    }

    [Fact]
    public async Task ProviderFailuresPreserveUserMessageButNeverInventAnAssistantReply()
    {
        using var temp = new TempDirectory();
        var folder = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(folder);
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var session = sessions.Create(folder);
        var store = new FileDeveloperConversationStore(paths, sessions);
        var provider = new FakeProvider { ShouldFail = true };
        var service = new DeveloperConversationService(sessions, store, provider, new FakeRouter());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync(session.Id, "Why is this failing?"));

        var turn = Assert.Single(store.Read(session.Id));
        Assert.Equal(ChatRole.User, turn.Role);
        Assert.Equal("Why is this failing?", turn.Content);
    }

    [Fact]
    public async Task CancelReturnsPromptlyEvenWhenProviderIgnoresCancellation()
    {
        using var temp = new TempDirectory();
        var folder = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(folder);
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var session = sessions.Create(folder);
        var store = new FileDeveloperConversationStore(paths, sessions);
        var provider = new FakeProvider { ShouldBlock = true };
        var service = new DeveloperConversationService(sessions, store, provider, new FakeRouter());
        using var cancellation = new CancellationTokenSource();

        var pending = service.SendAsync(session.Id, "Cancel this provider", cancellation.Token);
        Assert.Single(provider.Requests);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var saved = Assert.Single(store.Read(session.Id));
        Assert.Equal(ChatRole.User, saved.Role);
    }

    [Fact]
    public async Task MissingModelFailsBeforeAnyConversationMutation()
    {
        using var temp = new TempDirectory();
        var folder = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(folder);
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var session = sessions.Create(folder);
        var store = new FileDeveloperConversationStore(paths, sessions);
        var provider = new FakeProvider();
        var service = new DeveloperConversationService(sessions, store, provider, new FakeRouter(""));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync(session.Id, "hello"));
        Assert.Empty(store.Read(session.Id));
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task ApprovedSourceIsSentOnceAndNeverStoredOrCarriedIntoLaterTurns()
    {
        using var temp = new TempDirectory();
        var folder = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(folder);
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var session = sessions.Create(folder);
        var history = new FileDeveloperConversationStore(paths, sessions);
        var provider = new FakeProvider();
        var chat = new DeveloperConversationService(sessions, history, provider, new FakeRouter());

        const string marker = "SOURCE_ONLY_84Z_UNIQUE_DO_NOT_STORE";
        var source = new DeveloperSourceContext(session.Id, "src/Program.cs", marker);
        await chat.SendWithSourceAsync(session.Id, "What does this do?", source);
        await chat.SendAsync(session.Id, "What is a test?");

        Assert.Equal(2, provider.Requests.Count);
        var first = provider.Requests[0].Messages;
        Assert.Equal(2, first.Count);
        Assert.Contains(marker, first[0].Content);
        Assert.Contains("src/Program.cs", first[0].Content);
        Assert.Equal(ChatRole.User, first[0].Role);
        Assert.Equal("What does this do?", first[1].Content);

        Assert.DoesNotContain(provider.Requests[1].Messages,
            message => message.Content.Contains(marker, StringComparison.Ordinal));
        Assert.DoesNotContain(history.Read(session.Id),
            entry => entry.Content.Contains(marker, StringComparison.Ordinal));
        Assert.Equal(4, history.Read(session.Id).Count);
    }

    [Fact]
    public async Task InvalidSourceSessionOrOversizedContentCannotReachProviderOrStorage()
    {
        using var temp = new TempDirectory();
        var folder = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(folder);
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var session = sessions.Create(folder);
        var history = new FileDeveloperConversationStore(paths, sessions);
        var provider = new FakeProvider();
        var chat = new DeveloperConversationService(sessions, history, provider, new FakeRouter());

        var wrongSession = new DeveloperSourceContext(Guid.NewGuid(), "src/Program.cs", "code");
        var traversal = new DeveloperSourceContext(session.Id, "../passwords.txt", "code");
        var huge = new DeveloperSourceContext(session.Id, "src/Program.cs", new string('x', 8001));

        await Assert.ThrowsAsync<ArgumentException>(
            () => chat.SendWithSourceAsync(session.Id, "explain", wrongSession));
        await Assert.ThrowsAsync<ArgumentException>(
            () => chat.SendWithSourceAsync(session.Id, "explain", traversal));
        await Assert.ThrowsAsync<ArgumentException>(
            () => chat.SendWithSourceAsync(session.Id, "explain", huge));

        Assert.Empty(provider.Requests);
        Assert.Empty(history.Read(session.Id));
    }

    [Fact]
    public async Task ApprovedSourceProviderFailureDoesNotPersistSourceContent()
    {
        using var temp = new TempDirectory();
        var folder = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(folder);
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var session = sessions.Create(folder);
        var history = new FileDeveloperConversationStore(paths, sessions);
        var provider = new FakeProvider { ShouldFail = true };
        var chat = new DeveloperConversationService(sessions, history, provider, new FakeRouter());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => chat.SendWithSourceAsync(session.Id, "explain",
                new DeveloperSourceContext(session.Id, "a.cs", "PRIVATE_FILE_DATA")));

        var turn = Assert.Single(history.Read(session.Id));
        Assert.Equal("explain", turn.Content);
        Assert.DoesNotContain("PRIVATE_FILE_DATA", turn.Content);
    }

    private sealed class FakeRouter(string model = "local-model") : IModelRouter
    {
        public ValueTask<ModelRoute> ResolveAsync(ModelRouteRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ModelRoute("local", model, false));
    }

    private sealed class FakeProvider : IChatProvider
    {
        public string ProviderId => "fake-local";
        public List<ChatRequest> Requests { get; } = [];
        public bool ShouldFail { get; set; }
        public bool ShouldBlock { get; set; }

        public Task<ChatCompletion> CompleteAsync(ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (ShouldBlock)
                return new TaskCompletionSource<ChatCompletion>(
                    TaskCreationOptions.RunContinuationsAsynchronously).Task;
            if (ShouldFail)
                return Task.FromResult(new ChatCompletion(
                    new ChatMessage(ChatRole.Assistant, string.Empty),
                    Error: new ProviderError(ProviderErrorKind.Network, "Simulated failure")));

            return Task.FromResult(new ChatCompletion(
                new ChatMessage(ChatRole.Assistant, $"Reply {Requests.Count}")));
        }

        public IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
