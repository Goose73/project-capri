using System.IO;
using Capri.Cli;
using Capri.Cli.Commands;
using Capri.Cli.Services;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class SetupWizardTests
{
    [Fact]
    public async Task RunAsyncRendersDetectionSummaryAndSavesSelectedInstalledModel()
    {
        using var fixture = new SetupFixture(
            settings: new AppSettings(),
            models: [new OllamaModelInfo { Name = "qwen3.5:4b", Size = 3_300_000_000 }],
            chatProvider: StubSuccessProvider("Capri ready"),
            input:
            [
                "1", // select recommended installed model
                "s", "s", "s", "s", // voice
                "s", "s", "s", "s", "s", "s", "s", "s", // ui/automation
                "n", // intro
            ]);

        var exitCode = await fixture.RunAsync(repair: false);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Hardware detection", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Local provider detection", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Model recommendation", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Inference test", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Setup summary", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("qwen3.5:4b", fixture.Services.Settings.Current.Provider.ChatModel);
        Assert.Equal("ollama", fixture.Services.Settings.Current.Provider.ProviderId, StringComparer.OrdinalIgnoreCase);
        Assert.False(fixture.Services.Settings.Current.Provider.AllowCloudInference);
        Assert.Equal(0, fixture.Ollama.PullCalls);
        Assert.DoesNotContain("sk-test-secret-value", fixture.Console.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-secret-value", fixture.Console.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncDownloadRequiresConfirmationAndCanBeSkipped()
    {
        using var fixture = new SetupFixture(
            settings: new AppSettings(),
            models: [],
            chatProvider: StubSuccessProvider("Capri ready"),
            input:
            [
                "1", // choose recommended model (not installed)
                "n", // decline download
                "s", // skip model config
                "s", "s", "s", "s", // voice
                "s", "s", "s", "s", "s", "s", "s", "s", // ui/automation
                // no intro prompt if inference not run due to no model? inference skipped when no model
            ]);

        var exitCode = await fixture.RunAsync(repair: false);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(0, fixture.Ollama.PullCalls);
        Assert.Contains("explicit confirmation", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Download skipped", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsyncDownloadCanBeCancelled()
    {
        using var fixture = new SetupFixture(
            settings: new AppSettings(),
            models: [],
            chatProvider: StubSuccessProvider("Capri ready"),
            input:
            [
                "1", // choose recommended model
                "c", // cancel at download confirmation
            ]);

        var exitCode = await fixture.RunAsync(repair: false);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(0, fixture.Ollama.PullCalls);
        Assert.Contains("cancel", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsyncInferenceFailureIsReportedClearlyAndSetupCompletes()
    {
        using var fixture = new SetupFixture(
            settings: new AppSettings(),
            models: [new OllamaModelInfo { Name = "qwen3.5:4b" }],
            chatProvider: new StubChatProvider
            {
                CompletionResult = new ChatCompletion(
                    new ChatMessage(ChatRole.Assistant, string.Empty),
                    Error: new ProviderError(ProviderErrorKind.Network, "unreachable")),
            },
            input:
            [
                "1",
                "s", "s", "s", "s",
                "s", "s", "s", "s", "s", "s", "s", "s",
                // no intro when inference fails
            ]);

        var exitCode = await fixture.RunAsync(repair: false);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("FAIL", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FAIL", fixture.Console.Error + fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsyncRepairDiagnosticsOnlyRunsWithoutReconfiguring()
    {
        var initial = new AppSettings
        {
            Provider = new AppSettings.ProviderSettings
            {
                ProviderId = "ollama",
                BaseUrl = "http://127.0.0.1:11434/v1",
                ChatModel = "qwen3.5:4b",
            },
        };
        using var fixture = new SetupFixture(
            settings: initial,
            models: [new OllamaModelInfo { Name = "qwen3.5:4b" }],
            chatProvider: StubSuccessProvider("Capri ready"),
            input: ["2"]); // repair resume -> diagnostics only

        var exitCode = await fixture.RunAsync(repair: true);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Repair diagnostics", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("qwen3.5:4b", fixture.Services.Settings.Current.Provider.ChatModel);
    }

    [Fact]
    public async Task RunAsyncDiagnosticsOnlyNeverUpdatesOrOverwritesMalformedSettings()
    {
        var initial = new AppSettings
        {
            Provider = new AppSettings.ProviderSettings { ProviderId = "ollama", ChatModel = "gemma3:4b" },
        };
        using var fixture = new SetupFixture(settings: initial, chatProvider: StubSuccessProvider("Capri ready"), input: ["2"]);
        var badJson = "{ invalid settings";
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Services.Paths.SettingsFile)!);
        File.WriteAllText(fixture.Services.Paths.SettingsFile, badJson);
        var updates = 0;
        fixture.Services.Settings.Changed += (_, _) => updates++;

        var exitCode = await fixture.RunAsync(repair: true);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Diagnostics complete", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, updates);
        Assert.Equal(badJson, File.ReadAllText(fixture.Services.Paths.SettingsFile));
    }

    [Fact]
    public async Task RunAsyncMalformedSettingsPreventsProvisionalAndFinalSave()
    {
        using var fixture = new SetupFixture(
            settings: new AppSettings(),
            models: [new OllamaModelInfo { Name = "qwen3.5:4b" }],
            chatProvider: StubSuccessProvider("Capri ready"),
            input: ["1", "s", "s", "s", "s", "s", "s", "s", "s", "s", "s", "s", "s"]);
        var badJson = "{ invalid settings";
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Services.Paths.SettingsFile)!);
        File.WriteAllText(fixture.Services.Paths.SettingsFile, badJson);
        var updates = 0;
        fixture.Services.Settings.Changed += (_, _) => updates++;

        var exitCode = await fixture.RunAsync(repair: false);

        Assert.Equal(CliExitCodes.Error, exitCode);
        Assert.Equal(0, updates);
        Assert.Equal(badJson, File.ReadAllText(fixture.Services.Paths.SettingsFile));
        Assert.Contains("settings file problem", fixture.Console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("gemma3:4b")]
    public async Task RunAsyncDifferentInstalledTagStillOffersDownload(string choice)
    {
        using var fixture = new SetupFixture(
            settings: new AppSettings(),
            models: [new OllamaModelInfo { Name = "gemma3:1b" }],
            input: [choice, "n", "c"]);

        var exitCode = await fixture.RunAsync(repair: false);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("Model not installed locally: gemma3:4b", fixture.Console.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("gemma3:4b [installed]", fixture.Console.Out, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Ollama.PullCalls);
        Assert.NotEqual("gemma3:4b", fixture.Services.Settings.Current.Provider.ChatModel);
    }

    [Fact]
    public async Task RunAsyncCancelAtResumeMakesNoChanges()
    {
        var initial = new AppSettings
        {
            Provider = new AppSettings.ProviderSettings { ProviderId = "ollama", ChatModel = "old:model" },
        };
        using var fixture = new SetupFixture(settings: initial, input: ["3"]);

        var exitCode = await fixture.RunAsync(repair: true);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal("old:model", fixture.Services.Settings.Current.Provider.ChatModel);
        Assert.Contains("No changes were made", fixture.Console.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsyncNonInteractiveReturnsUsage()
    {
        using var fixture = new SetupFixture(interactive: false);

        var exitCode = await fixture.RunAsync(repair: false);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("interactive", fixture.Console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsyncNonInteractiveRepairRefusesWithoutChangingSettings()
    {
        using var fixture = new SetupFixture(interactive: false);
        var initial = fixture.Services.Settings.Current;

        var exitCode = await fixture.RunAsync(repair: true);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--repair", fixture.Console.Error, StringComparison.Ordinal);
        Assert.Equal(initial, fixture.Services.Settings.Current);
    }

    private static StubChatProvider StubSuccessProvider(string text)
    {
        return new StubChatProvider
        {
            CompletionResult = new ChatCompletion(new ChatMessage(ChatRole.Assistant, text)),
        };
    }

    private sealed class SetupFixture : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public SetupFixture(
            IEnumerable<string?>? input = null,
            bool interactive = true,
            AppSettings? settings = null,
            IReadOnlyList<OllamaModelInfo>? models = null,
            IChatProvider? chatProvider = null)
        {
            Console = new FakeCliConsole(input, interactive);
            Ollama = new StubOllamaClient { Models = models ?? [] };
            var effectiveModel = !string.IsNullOrWhiteSpace(settings?.Provider.ChatModel)
                ? settings!.Provider.ChatModel
                : "qwen3.5:4b";
            Services = new StubCliServices(
                new CapriPaths(_temp.Path),
                new FakeAppSettingsStore(settings),
                new FakeCredentialStore(),
                chatProvider,
                chatProvider is null ? null : new StubModelRouter("ollama", effectiveModel),
                Ollama);
            Probe = new StubEndpointProbe(true);
        }

        public FakeCliConsole Console { get; }
        public StubOllamaClient Ollama { get; }
        public StubCliServices Services { get; }
        public StubEndpointProbe Probe { get; }

        public Task<int> RunAsync(bool repair)
        {
            var wizard = new SetupWizard(Console, Services, Probe, repair);
            return wizard.RunAsync(CancellationToken.None);
        }

        public void Dispose()
        {
            Services.Dispose();
            _temp.Dispose();
        }
    }
}
