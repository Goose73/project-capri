using System.Text.Json;
using Capri.Cli;
using Capri.Cli.Commands;
using Capri.Core.Models;
using Capri.Providers;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class StatusCommandTests
{
    private const string SecretValue = "sk-live-secret-value";

    [Fact]
    public async Task StatusShowsProviderModelCredentialPresenceAndEndpoints()
    {
        using var temp = new TempDirectory();
        var credentials = new FakeCredentialStore();
        await credentials.SetAsync(ProviderCredentials.GetApiKeyCredentialKey("openai"), SecretValue);
        using var services = StubCliServices.CreateInTemp(
            temp.Path,
            CapriDefaultSettings.Create(),
            credentials);
        var console = new FakeCliConsole();
        var command = new StatusCommand(console, services, new StubEndpointProbe(reachable: true));

        var exitCode = await command.ExecuteAsync(json: false);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("openai", console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gpt-4o-mini", console.Out, StringComparison.Ordinal);
        Assert.Contains("present for OpenAI", console.Out, StringComparison.Ordinal);
        Assert.Contains("Ollama", console.Out, StringComparison.Ordinal);
        Assert.Contains("LM Studio", console.Out, StringComparison.Ordinal);
        Assert.Contains("reachable", console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SecretValue, console.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusJsonContainsNoSecretAndExposesSafeFields()
    {
        using var temp = new TempDirectory();
        var credentials = new FakeCredentialStore();
        await credentials.SetAsync(ProviderCredentials.GetApiKeyCredentialKey("openai"), SecretValue);
        using var services = StubCliServices.CreateInTemp(
            temp.Path,
            CapriDefaultSettings.Create(),
            credentials);
        var console = new FakeCliConsole();
        var command = new StatusCommand(console, services, new StubEndpointProbe(reachable: true));

        var exitCode = await command.ExecuteAsync(json: true);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.DoesNotContain(SecretValue, console.Out, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(console.Out);
        var root = document.RootElement;
        Assert.Equal("openai", root.GetProperty("provider").GetProperty("id").GetString());
        Assert.Equal("gpt-4o-mini", root.GetProperty("provider").GetProperty("model").GetString());
        Assert.Equal("present", root.GetProperty("credential").GetProperty("status").GetString());
        Assert.True(root.GetProperty("localEndpoints").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task LocalProviderReportsNoCredentialRequired()
    {
        using var temp = new TempDirectory();
        var settings = CapriDefaultSettings.Create() with
        {
            Provider = CapriDefaultSettings.Create().Provider with
            {
                ProviderId = "ollama",
                BaseUrl = "http://localhost:11434/v1",
                ChatModel = "llama3.2",
            },
        };
        using var services = StubCliServices.CreateInTemp(temp.Path, settings);
        var console = new FakeCliConsole();
        var command = new StatusCommand(console, services, new StubEndpointProbe(reachable: false));

        var exitCode = await command.ExecuteAsync(json: false);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("not required (local provider)", console.Out, StringComparison.Ordinal);
        Assert.Contains("not reachable", console.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BaseUrlCredentialsAreNeverDisplayed()
    {
        using var temp = new TempDirectory();
        var settings = CapriDefaultSettings.Create() with
        {
            Provider = CapriDefaultSettings.Create().Provider with
            {
                BaseUrl = "https://user:pass@example.com/v1?api_key=secret-value",
            },
        };
        using var services = StubCliServices.CreateInTemp(temp.Path, settings);
        var console = new FakeCliConsole();
        var command = new StatusCommand(console, services, new StubEndpointProbe(reachable: true));

        var exitCode = await command.ExecuteAsync(json: false);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.DoesNotContain("user:pass", console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-value", console.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownProviderDoesNotPerformCredentialLookup()
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
        var command = new StatusCommand(console, services, new StubEndpointProbe(reachable: true));

        var exitCode = await command.ExecuteAsync(json: true);

        Assert.Equal(CliExitCodes.Success, exitCode);
        using var document = JsonDocument.Parse(console.Out);
        Assert.Equal(
            "unknown-provider",
            document.RootElement.GetProperty("credential").GetProperty("status").GetString());
    }
}
