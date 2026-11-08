using System.Text.Json;
using Capri.Cli;
using Capri.Cli.Services;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class ProvidersCommandTests
{
    [Fact]
    public async Task ProvidersTestSuccessReportsLatencyAndNoSecrets()
    {
        const string secret = "sk-do-not-print";
        using var fixture = new ProvidersFixture(
            new StubChatProvider { CompletionResult = new ChatCompletion(new ChatMessage(ChatRole.Assistant, "Capri ready")) });

        var exitCode = await fixture.RunAsync(CliParser.Parse(["providers", "test", "--json"]));

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.DoesNotContain(secret, fixture.Console.Out, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(fixture.Console.Out);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("latencyMs").GetInt32() >= 0);
        Assert.Equal("Reply with: Capri ready", doc.RootElement.GetProperty("probe").GetString());
        Assert.Equal(1, ((StubChatProvider)fixture.Services.ChatProvider!).CompleteCalls);
    }

    [Fact]
    public async Task ProvidersTestReportsProviderErrorAndConsentStyleFailure()
    {
        using var fixture = new ProvidersFixture(
            new StubChatProvider
            {
                CompletionResult = new ChatCompletion(
                    new ChatMessage(ChatRole.Assistant, string.Empty),
                    Error: new ProviderError(ProviderErrorKind.Configuration, "Cloud inference is disabled. Enable cloud inference and confirm consent before using this provider.")),
            });

        var exitCode = await fixture.RunAsync(CliParser.Parse(["providers", "test", "--json"]));

        Assert.Equal(CliExitCodes.Error, exitCode);
        using var doc = JsonDocument.Parse(fixture.Console.Out);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("Cloud inference is disabled", doc.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvidersTestReportsNoModelConfigured()
    {
        var settings = CapriDefaultSettings.Create() with
        {
            Provider = CapriDefaultSettings.Create().Provider with { ProviderId = "ollama", BaseUrl = "http://127.0.0.1:11434/v1", ChatModel = string.Empty },
        };
        using var fixture = new ProvidersFixture(new StubChatProvider(), settings);

        var exitCode = await fixture.RunAsync(CliParser.Parse(["providers", "test"]));

        Assert.Equal(CliExitCodes.Error, exitCode);
        Assert.Contains("No chat model is configured", fixture.Console.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProvidersFixture : IDisposable
    {
        private readonly TempDirectory _temp;

        public ProvidersFixture(StubChatProvider provider, AppSettings? settings = null)
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
                modelRouter: new StubModelRouter("ollama", effective.Provider.ChatModel, supportsStreaming: false));
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
