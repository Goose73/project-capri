using System.Text.Json;
using Capri.Cli;
using Capri.Cli.Services;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class ModelsCommandTests
{
    [Fact]
    public async Task ModelsListJsonContainsInstalledModels()
    {
        using var fixture = new ModelsFixture(
            models:
            [
                new OllamaModelInfo { Name = "qwen3.5:4b", Size = 3_300_000_000, Details = new OllamaModelDetails { ParameterSize = "4B", QuantizationLevel = "Q4_K_M" } },
            ]);

        var exitCode = await fixture.RunAsync(CliParser.Parse(["models", "list", "--json"]));

        Assert.Equal(CliExitCodes.Success, exitCode);
        using var doc = JsonDocument.Parse(fixture.Console.Out);
        var models = doc.RootElement.GetProperty("models");
        Assert.Equal(JsonValueKind.Array, models.ValueKind);
        Assert.Contains(models.EnumerateArray(), m => m.GetProperty("name").GetString() == "qwen3.5:4b");
    }

    [Fact]
    public async Task ModelsRecommendContainsExpectedCandidatesAndInstalledMarker()
    {
        using var fixture = new ModelsFixture(
            models: [new OllamaModelInfo { Name = "qwen3.5:4b", Size = 1 }]);

        var exitCode = await fixture.RunAsync(CliParser.Parse(["models", "recommend", "--json"]));

        Assert.Equal(CliExitCodes.Success, exitCode);
        using var doc = JsonDocument.Parse(fixture.Console.Out);
        var candidates = doc.RootElement.GetProperty("candidates").EnumerateArray().ToList();
        Assert.Contains(candidates, c => c.GetProperty("name").GetString() == "qwen3.5:4b" && c.GetProperty("installed").GetBoolean());
        Assert.Contains(candidates, c => c.GetProperty("name").GetString() == "qwen3:4b-instruct");
        Assert.Contains(candidates, c => c.GetProperty("name").GetString() == "phi4-mini");
        Assert.Contains(candidates, c => c.GetProperty("name").GetString() == "gemma3:4b");
        Assert.Contains(candidates, c => c.GetProperty("name").GetString() == "qwen3.5:2b");
        Assert.True(doc.RootElement.GetProperty("hardware").GetProperty("estimates").GetBoolean());
    }

    [Fact]
    public async Task ModelsPullRefusesInNonInteractiveModeWithoutYes()
    {
        using var fixture = new ModelsFixture(interactive: false);

        var exitCode = await fixture.RunAsync(CliParser.Parse(["models", "pull", "qwen3.5:4b"]));

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Equal(0, fixture.Ollama.PullCalls);
        Assert.Contains("--yes", fixture.Console.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelsPullWithYesCallsOllamaMock()
    {
        using var fixture = new ModelsFixture(
            interactive: false,
            pullProgress:
            [
                new OllamaPullProgress("pulling manifest"),
                new OllamaPullProgress("success"),
            ]);

        var exitCode = await fixture.RunAsync(CliParser.Parse(["models", "pull", "qwen3.5:4b", "--yes"]));

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(1, fixture.Ollama.PullCalls);
        Assert.Equal("qwen3.5:4b", fixture.Ollama.LastPullModel);
    }

    [Fact]
    public async Task ModelsUsePersistsModelAndLocalProvider()
    {
        using var fixture = new ModelsFixture(models: [new OllamaModelInfo { Name = "qwen3.5:4b" }]);

        var exitCode = await fixture.RunAsync(CliParser.Parse(["models", "use", "qwen3.5:4b", "--json"]));

        Assert.Equal(CliExitCodes.Success, exitCode);
        using var doc = JsonDocument.Parse(fixture.Console.Out);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("qwen3.5:4b", doc.RootElement.GetProperty("model").GetString());
        var settings = fixture.Services.Settings.Current;
        Assert.Equal("qwen3.5:4b", settings.Provider.ChatModel);
        Assert.True(Capri.Core.Providers.ProviderCatalog.IsLocal(settings.Provider.ProviderId)
            || Capri.Core.Providers.ProviderCatalog.IsLoopbackAllowedHttp(settings.Provider.BaseUrl));
    }

    [Fact]
    public async Task ModelsPullJsonErrorDoesNotEchoSecrets()
    {
        const string secret = "sk-do-not-print";
        using var fixture = new ModelsFixture(interactive: false);

        var exitCode = await fixture.RunAsync(CliParser.Parse(["models", "pull", "qwen3.5:4b"]));

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.DoesNotContain(secret, fixture.Console.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, fixture.Console.Error, StringComparison.Ordinal);
    }

    private sealed class ModelsFixture : IDisposable
    {
        private readonly TempDirectory _temp;

        public ModelsFixture(
            IReadOnlyList<OllamaModelInfo>? models = null,
            IReadOnlyList<OllamaPullProgress>? pullProgress = null,
            bool interactive = false)
        {
            _temp = new TempDirectory();
            Ollama = new StubOllamaClient
            {
                Models = models ?? [],
                PullProgress = pullProgress,
            };
            Services = new StubCliServices(
                new CapriPaths(_temp.Path),
                new FakeAppSettingsStore(CapriDefaultSettings.Create() with
                {
                    Provider = CapriDefaultSettings.Create().Provider with
                    {
                        ProviderId = "ollama",
                        BaseUrl = "http://127.0.0.1:11434/v1",
                        ChatModel = "qwen3.5:4b",
                    },
                }),
                ollamaClient: Ollama);
            Console = new FakeCliConsole(interactive: interactive);
            Application = new CliApplication(
                Console,
                Services,
                new StubEndpointProbe(reachable: true),
                new StubDesktopLauncher(),
                new StubDpapiProbe(available: true),
                Ollama);
        }

        public FakeCliConsole Console { get; }
        public StubCliServices Services { get; }
        public StubOllamaClient Ollama { get; }
        public CliApplication Application { get; }

        public Task<int> RunAsync(CliInvocation invocation) => Application.RunAsync(invocation);

        public void Dispose()
        {
            Services.Dispose();
            _temp.Dispose();
        }
    }
}
