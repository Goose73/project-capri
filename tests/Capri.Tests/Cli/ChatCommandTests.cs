using Capri.Cli;
using Capri.Cli.Services;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class ChatCommandTests
{
    [Fact]
    public async Task ChatRequiresInteractiveTerminal()
    {
        using var fixture = new ChatFixture();

        var exitCode = await fixture.RunAsync(CliParser.Parse(["chat"]));

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("interactive terminal", fixture.Console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatReportsNoModelConfigured()
    {
        var settings = CapriDefaultSettings.Create() with
        {
            Provider = CapriDefaultSettings.Create().Provider with { ProviderId = "ollama", BaseUrl = "http://127.0.0.1:11434/v1", ChatModel = string.Empty },
        };
        using var fixture = new ChatFixture(settings: settings, interactive: true);

        var exitCode = await fixture.RunAsync(CliParser.Parse(["chat"]));

        Assert.Equal(CliExitCodes.Error, exitCode);
        Assert.Contains("No chat model is configured", fixture.Console.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ChatFixture : IDisposable
    {
        private readonly TempDirectory _temp;

        public ChatFixture(Capri.Core.Models.AppSettings? settings = null, bool interactive = false)
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
                chatProvider: new StubChatProvider(),
                modelRouter: new StubModelRouter("ollama", effective.Provider.ChatModel));
            Console = new FakeCliConsole(interactive: interactive);
            Application = new CliApplication(Console, Services, new StubEndpointProbe(true), new StubDesktopLauncher(), new StubDpapiProbe(true), new StubOllamaClient());
        }

        public FakeCliConsole Console { get; }
        public StubCliServices Services { get; }
        public CliApplication Application { get; }
        public Task<int> RunAsync(CliInvocation invocation) => Application.RunAsync(invocation);
        public void Dispose() { Services.Dispose(); _temp.Dispose(); }
    }
}
