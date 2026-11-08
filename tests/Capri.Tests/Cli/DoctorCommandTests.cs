using System.IO;
using System.Text.Json;
using Capri.Cli;
using Capri.Cli.Commands;
using Capri.Core.Models;
using Capri.Providers;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class DoctorCommandTests
{
    [Fact]
    public async Task HealthyLocalConfigurationExitsZero()
    {
        using var temp = new TempDirectory();
        var settings = LocalOllamaSettings();
        using var services = StubCliServices.CreateInTemp(temp.Path, settings);
        var console = new FakeCliConsole();
        var command = new DoctorCommand(
            console,
            services,
            new StubEndpointProbe(reachable: true),
            new StubDpapiProbe(available: true));

        var exitCode = await command.ExecuteAsync(json: true);

        Assert.Equal(CliExitCodes.Success, exitCode);
        using var document = JsonDocument.Parse(console.Out);
        Assert.Equal(0, document.RootElement.GetProperty("summary").GetProperty("critical").GetInt32());
    }

    [Fact]
    public async Task UnknownProviderIsCritical()
    {
        using var temp = new TempDirectory();
        var settings = CapriDefaultSettings.Create() with
        {
            Provider = CapriDefaultSettings.Create().Provider with
            {
                ProviderId = "unknown-provider",
                ChatModel = "some-model",
            },
        };
        using var services = StubCliServices.CreateInTemp(temp.Path, settings);
        var console = new FakeCliConsole();
        var command = new DoctorCommand(
            console,
            services,
            new StubEndpointProbe(reachable: true),
            new StubDpapiProbe(available: true));

        var exitCode = await command.ExecuteAsync(json: false);

        Assert.Equal(CliExitCodes.Unhealthy, exitCode);
        Assert.Contains("[CRIT] provider.configured", console.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedSettingsFileIsCritical()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.json"), "{ not valid json");
        using var services = StubCliServices.CreateInTemp(temp.Path, LocalOllamaSettings());
        var console = new FakeCliConsole();
        var command = new DoctorCommand(
            console,
            services,
            new StubEndpointProbe(reachable: true),
            new StubDpapiProbe(available: true));

        var exitCode = await command.ExecuteAsync(json: true);

        Assert.Equal(CliExitCodes.Unhealthy, exitCode);
        using var document = JsonDocument.Parse(console.Out);
        var settingsCheck = document.RootElement.GetProperty("checks").EnumerateArray()
            .First(check => check.GetProperty("id").GetString() == "settings.readable");
        Assert.Equal("critical", settingsCheck.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task UnreachableActiveLocalEndpointIsOnlyAWarning()
    {
        using var temp = new TempDirectory();
        using var services = StubCliServices.CreateInTemp(temp.Path, LocalOllamaSettings());
        var console = new FakeCliConsole();
        var command = new DoctorCommand(
            console,
            services,
            new StubEndpointProbe(reachable: false),
            new StubDpapiProbe(available: true));

        var exitCode = await command.ExecuteAsync(json: true);

        Assert.Equal(CliExitCodes.Success, exitCode);
        using var document = JsonDocument.Parse(console.Out);
        var summary = document.RootElement.GetProperty("summary");
        Assert.True(summary.GetProperty("warnings").GetInt32() >= 1);
        Assert.Equal(0, summary.GetProperty("critical").GetInt32());
    }

    [Fact]
    public async Task CloudProviderWithoutKeyIsAWarningNotACritical()
    {
        using var temp = new TempDirectory();
        var settings = CapriDefaultSettings.Create() with
        {
            Provider = CapriDefaultSettings.Create().Provider with
            {
                AllowCloudInference = true,
                CloudConsentRecordedUtc = DateTimeOffset.UtcNow,
            },
        };
        using var services = StubCliServices.CreateInTemp(temp.Path, settings);
        var console = new FakeCliConsole();
        var command = new DoctorCommand(
            console,
            services,
            new StubEndpointProbe(reachable: true),
            new StubDpapiProbe(available: true));

        var exitCode = await command.ExecuteAsync(json: false);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("No API key is stored", console.Out, StringComparison.Ordinal);
        Assert.Contains("[WARN]", console.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableDpapiIsCritical()
    {
        using var temp = new TempDirectory();
        using var services = StubCliServices.CreateInTemp(temp.Path, LocalOllamaSettings());
        var console = new FakeCliConsole();
        var command = new DoctorCommand(
            console,
            services,
            new StubEndpointProbe(reachable: true),
            new StubDpapiProbe(available: false));

        var exitCode = await command.ExecuteAsync(json: false);

        Assert.Equal(CliExitCodes.Unhealthy, exitCode);
        Assert.Contains("credentials.dpapi", console.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorOutputNeverContainsCredentialValue()
    {
        const string secret = "sk-do-not-print";
        using var temp = new TempDirectory();
        var credentials = new FakeCredentialStore();
        await credentials.SetAsync(ProviderCredentials.GetApiKeyCredentialKey("openai"), secret);
        using var services = StubCliServices.CreateInTemp(
            temp.Path,
            CapriDefaultSettings.Create() with
            {
                Provider = CapriDefaultSettings.Create().Provider with
                {
                    AllowCloudInference = true,
                    CloudConsentRecordedUtc = DateTimeOffset.UtcNow,
                },
            },
            credentials);
        var console = new FakeCliConsole();
        var command = new DoctorCommand(
            console,
            services,
            new StubEndpointProbe(reachable: true),
            new StubDpapiProbe(available: true));

        _ = await command.ExecuteAsync(json: true);

        Assert.DoesNotContain(secret, console.Out, StringComparison.Ordinal);
    }

    private static AppSettings LocalOllamaSettings()
    {
        return CapriDefaultSettings.Create() with
        {
            Provider = CapriDefaultSettings.Create().Provider with
            {
                ProviderId = "ollama",
                BaseUrl = "http://localhost:11434/v1",
                ChatModel = "llama3.2",
            },
        };
    }
}
