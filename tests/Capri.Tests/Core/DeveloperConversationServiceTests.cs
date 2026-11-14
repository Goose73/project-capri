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

        public Task<ChatCompletion> CompleteAsync(ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
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
