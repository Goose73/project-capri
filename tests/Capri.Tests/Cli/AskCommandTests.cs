using System.Text.Json;
using Capri.Cli;
using Capri.Cli.Services;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class AskCommandTests
{
    [Fact]
    public async Task AskSuccessPrintsResponseAndJsonHasNoSecrets()
    {
        const string secret = "sk-do-not-print";
        using var fixture = new AskFixture(
            new StubChatProvider { StreamDeltas = [new ChatDelta("Capri ready")] });

        var exitCode = await fixture.RunAsync(CliParser.Parse(["ask", "hello", "--json"]));

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.DoesNotContain(secret, fixture.Console.Out, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(fixture.Console.Out);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Capri ready", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal("ollama", doc.RootElement.GetProperty("providerId").GetString());
    }

    [Fact]
    public async Task AskReportsNoModelConfigured()
    {
        var settings = CapriDefaultSettings.Create() with
        {
            Provider = CapriDefaultSettings.Create().Provider with { ProviderId = "ollama", BaseUrl = "http://127.0.0.1:11434/v1", ChatModel = string.Empty },
        };
        using var fixture = new AskFixture(new StubChatProvider(), settings: settings);

        var exitCode = await fixture.RunAsync(CliParser.Parse(["ask", "hello"]));

        Assert.Equal(CliExitCodes.Error, exitCode);
        Assert.Contains("No chat model is configured", fixture.Console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskReportsProviderError()
    {
        using var fixture = new AskFixture(
            new StubChatProvider
            {
                StreamDeltas = [new ChatDelta(string.Empty, Error: new ProviderError(ProviderErrorKind.Network, "down"))],
            });

        var exitCode = await fixture.RunAsync(CliParser.Parse(["ask", "hello"]));

        Assert.Equal(CliExitCodes.Error, exitCode);
        Assert.Contains("Could not reach", fixture.Console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskCancellationPropagates()
    {
        using var fixture = new AskFixture(
            new StubChatProvider { ThrowOnStream = new OperationCanceledException() });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Application.RunAsync(CliParser.Parse(["ask", "hello"]), cts.Token));
    }

    private sealed class AskFixture : IDisposable
    {
        private readonly TempDirectory _temp;

        public AskFixture(StubChatProvider provider, AppSettings? settings = null)
        {
            _temp = new TempDirectory();
            var effective = settings ?? CapriDefaultSettings.Create() with
            {
                Provider = CapriDefaultSettings.Create().Provider with
                {
                    ProviderId = "ollama",
                    BaseUrl = "http://127.0.0.1:11434/v1",
                    ChatModel = "qwen3.5:4b",
                },
            };
            Services = new StubCliServices(
                new CapriPaths(_temp.Path),
                new FakeAppSettingsStore(effective),
                chatProvider: provider,
                modelRouter: new StubModelRouter("ollama", effective.Provider.ChatModel, supportsStreaming: true));
            Console = new FakeCliConsole(interactive: false);
            Application = new CliApplication(Console, Services, new StubEndpointProbe(true), new StubDesktopLauncher(), new StubDpapiProbe(true), new StubOllamaClient());
        }

        public FakeCliConsole Console { get; }
        public StubCliServices Services { get; }
        public CliApplication Application { get; }
        public Task<int> RunAsync(CliInvocation invocation) => Application.RunAsync(invocation);
        public void Dispose() { Services.Dispose(); _temp.Dispose(); }
    }
}
