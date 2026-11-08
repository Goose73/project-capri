using System.IO;
using System.Text.Json;
using Capri.Cli;
using Capri.Cli.Commands;
using Capri.Cli.Configuration;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class ConfigCommandTests
{
    [Fact]
    public async Task ListJsonContainsOnlySafeSettings()
    {
        using var temp = new TempDirectory();
        using var services = StubCliServices.CreateInTemp(temp.Path, CapriDefaultSettings.Create());
        var console = new FakeCliConsole();
        var command = new ConfigCommand(console, services);

        var exitCode = await command.ExecuteAsync(["list"], json: true);

        Assert.Equal(CliExitCodes.Success, exitCode);
        using var document = JsonDocument.Parse(console.Out);
        var root = document.RootElement;
        Assert.Equal("gpt-4o-mini", root.GetProperty("provider.model").GetString());
        Assert.Equal("openai", root.GetProperty("provider.id").GetString());

        foreach (var property in root.EnumerateObject())
        {
            Assert.False(SecretGuard.IsSecretLike(property.Name), property.Name);
        }
    }

    [Fact]
    public async Task GetPlainPrintsOnlyTheValue()
    {
        using var temp = new TempDirectory();
        using var services = StubCliServices.CreateInTemp(temp.Path, CapriDefaultSettings.Create());
        var console = new FakeCliConsole();
        var command = new ConfigCommand(console, services);

        var exitCode = await command.ExecuteAsync(["get", "provider.model"], json: false);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal($"gpt-4o-mini{Environment.NewLine}", console.Out);
    }

    [Fact]
    public async Task GetSecretLikeNameIsRejectedWithoutEcho()
    {
        using var temp = new TempDirectory();
        using var services = StubCliServices.CreateInTemp(temp.Path, CapriDefaultSettings.Create());
        var console = new FakeCliConsole();
        var command = new ConfigCommand(console, services);

        var exitCode = await command.ExecuteAsync(["get", "provider.openai.apiKey"], json: false);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("not accessible", console.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", console.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUnknownSecretLookingInputIsNotEchoed()
    {
        const string sentinel = "sk-test-secret-value";
        using var temp = new TempDirectory();
        using var services = StubCliServices.CreateInTemp(temp.Path, CapriDefaultSettings.Create());
        var console = new FakeCliConsole();
        var command = new ConfigCommand(console, services);

        var exitCode = await command.ExecuteAsync(["get", sentinel], json: false);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.DoesNotContain(sentinel, console.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, console.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetPersistsValidatedValue()
    {
        using var temp = new TempDirectory();
        var store = new FakeAppSettingsStore(CapriDefaultSettings.Create());
        using var services = new StubCliServices(new CapriPaths(temp.Path), store);
        var console = new FakeCliConsole();
        var command = new ConfigCommand(console, services);

        var exitCode = await command.ExecuteAsync(
            ["set", "provider.model", "llama3.2"],
            json: true);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal("llama3.2", store.Current.Provider.ChatModel);

        using var document = JsonDocument.Parse(console.Out);
        Assert.True(document.RootElement.GetProperty("persisted").GetBoolean());
        Assert.Equal("provider.model", document.RootElement.GetProperty("key").GetString());
        Assert.False(document.RootElement.TryGetProperty("value", out _));
    }

    [Fact]
    public async Task SetRejectsInvalidValueWithoutChangingSettings()
    {
        using var temp = new TempDirectory();
        var store = new FakeAppSettingsStore(CapriDefaultSettings.Create());
        using var services = new StubCliServices(new CapriPaths(temp.Path), store);
        var console = new FakeCliConsole();
        var command = new ConfigCommand(console, services);

        var exitCode = await command.ExecuteAsync(
            ["set", "provider.model", "two words"],
            json: false);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Equal("gpt-4o-mini", store.Current.Provider.ChatModel);
        Assert.Contains("invalid value", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetRejectsBaseUrlCredentialsWithoutEcho()
    {
        using var temp = new TempDirectory();
        var store = new FakeAppSettingsStore(CapriDefaultSettings.Create());
        using var services = new StubCliServices(new CapriPaths(temp.Path), store);
        var console = new FakeCliConsole();
        var command = new ConfigCommand(console, services);

        var exitCode = await command.ExecuteAsync(
            ["set", "provider.baseUrl", "https://user:pass@example.com/v1"],
            json: false);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.DoesNotContain("user:pass", console.Out, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user:pass", console.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://api.openai.com/v1", store.Current.Provider.BaseUrl);
    }

    [Fact]
    public async Task SetRefusesToOverwriteMalformedSettingsFile()
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{ not valid json");
        var store = new FakeAppSettingsStore(CapriDefaultSettings.Create());
        using var services = new StubCliServices(new CapriPaths(temp.Path), store);
        var console = new FakeCliConsole();
        var command = new ConfigCommand(console, services);

        var exitCode = await command.ExecuteAsync(
            ["set", "provider.model", "llama3.2"],
            json: false);

        Assert.Equal(CliExitCodes.Error, exitCode);
        Assert.Equal("gpt-4o-mini", store.Current.Provider.ChatModel);
        Assert.Equal("{ not valid json", await File.ReadAllTextAsync(settingsPath));
        Assert.Contains("not valid JSON", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KeysJsonListsAllowlistOnly()
    {
        using var temp = new TempDirectory();
        using var services = StubCliServices.CreateInTemp(temp.Path, CapriDefaultSettings.Create());
        var console = new FakeCliConsole();
        var command = new ConfigCommand(console, services);

        var exitCode = await command.ExecuteAsync(["keys"], json: true);

        Assert.Equal(CliExitCodes.Success, exitCode);
        using var document = JsonDocument.Parse(console.Out);
        var keys = document.RootElement.GetProperty("keys").EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Contains("provider.id", keys);
        Assert.DoesNotContain(keys, key => key is not null && key.Contains("apiKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InteractiveConfigFallsBackWhenNotInteractive()
    {
        using var temp = new TempDirectory();
        using var services = StubCliServices.CreateInTemp(temp.Path, CapriDefaultSettings.Create());
        var console = new FakeCliConsole(interactive: false);
        var command = new ConfigCommand(console, services);

        var exitCode = await command.ExecuteAsync(Array.Empty<string>(), json: false);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("interactive config menu needs a terminal", console.Error, StringComparison.OrdinalIgnoreCase);
    }
}
