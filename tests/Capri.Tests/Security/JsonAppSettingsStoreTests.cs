using System.IO;
using System.Text.Json;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Security;

public sealed class JsonAppSettingsStoreTests
{
    [Fact]
    public void MissingFileUsesSensibleDefaults()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        using var store = new JsonAppSettingsStore(paths);

        Assert.Equal("openai", store.Current.Provider.ProviderId);
        Assert.Equal("https://api.openai.com/v1", store.Current.Provider.BaseUrl);
        Assert.Equal("gpt-4o-mini", store.Current.Provider.ChatModel);
        Assert.True(store.Current.Provider.EnableStreaming);
        Assert.Equal(0.7, store.Current.Provider.Temperature);
        Assert.Equal(1024, store.Current.Provider.MaxOutputTokens);
        Assert.Equal(60, store.Current.Provider.RequestTimeoutSeconds);
        Assert.False(File.Exists(paths.SettingsFile));
    }

    [Fact]
    public async Task UpdatePersistsJsonAndRaisesChanged()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        using var store = new JsonAppSettingsStore(paths);
        AppSettingsChangedEventArgs? raised = null;
        store.Changed += (_, args) => raised = args;

        var updated = store.Current with
        {
            Provider = store.Current.Provider with
            {
                ProviderId = "ollama",
                BaseUrl = "http://localhost:11434/v1",
                ChatModel = "llama3",
                EnableStreaming = false,
                Temperature = 0.2,
                MaxOutputTokens = 256,
                RequestTimeoutSeconds = 30,
            },
        };

        await store.UpdateAsync(updated);

        Assert.Equal(updated, store.Current);
        Assert.NotNull(raised);
        Assert.Equal(updated, raised.Current);
        Assert.True(File.Exists(paths.SettingsFile));

        var json = await File.ReadAllTextAsync(paths.SettingsFile);
        Assert.Contains("\"chatModel\": \"llama3\"", json, StringComparison.Ordinal);
        Assert.Contains("\"providerId\": \"ollama\"", json, StringComparison.Ordinal);

        using var reloaded = new JsonAppSettingsStore(paths);
        Assert.Equal(updated, reloaded.Current);
    }

    [Fact]
    public async Task ReloadPicksUpExternalChangesAndRaisesChanged()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        using var store = new JsonAppSettingsStore(paths);
        AppSettingsChangedEventArgs? raised = null;
        store.Changed += (_, args) => raised = args;

        var updated = store.Current with
        {
            Provider = store.Current.Provider with { ChatModel = "external-model" },
        };

        using (var writer = new JsonAppSettingsStore(paths))
        {
            await writer.UpdateAsync(updated);
        }

        await store.ReloadAsync();

        Assert.Equal("external-model", store.Current.Provider.ChatModel);
        Assert.NotNull(raised);
        Assert.Equal("external-model", raised.Current.Provider.ChatModel);
    }

    [Fact]
    public async Task CorruptFileFallsBackToDefaultsAndReloadKeepsCurrent()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        await File.WriteAllTextAsync(paths.SettingsFile, "{ this is not json");
        using var store = new JsonAppSettingsStore(paths);

        Assert.Equal(CapriDefaultSettings.DefaultModelId, store.Current.Provider.ChatModel);

        var updated = store.Current with
        {
            Provider = store.Current.Provider with { ChatModel = "kept-model" },
        };
        await store.UpdateAsync(updated);

        await File.WriteAllTextAsync(paths.SettingsFile, "{ broken again");
        await store.ReloadAsync();

        Assert.Equal("kept-model", store.Current.Provider.ChatModel);
    }

    [Fact]
    public async Task BlankProviderFieldsAreNormalizedToDefaults()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        using var store = new JsonAppSettingsStore(paths);

        var sparse = new AppSettings
        {
            SchemaVersion = 0,
            Provider = new AppSettings.ProviderSettings
            {
                ProviderId = " ",
                BaseUrl = string.Empty,
                ChatModel = "  ",
                Temperature = double.NaN,
                MaxOutputTokens = 0,
                RequestTimeoutSeconds = 0,
            },
        };

        await store.UpdateAsync(sparse);

        Assert.Equal(1, store.Current.SchemaVersion);
        Assert.Equal(CapriDefaultSettings.DefaultProviderId, store.Current.Provider.ProviderId);
        Assert.Equal(CapriDefaultSettings.DefaultBaseUrl, store.Current.Provider.BaseUrl);
        Assert.Equal(CapriDefaultSettings.DefaultModelId, store.Current.Provider.ChatModel);
        Assert.Equal(CapriDefaultSettings.DefaultTemperature, store.Current.Provider.Temperature);
        Assert.Equal(CapriDefaultSettings.DefaultMaxOutputTokens, store.Current.Provider.MaxOutputTokens);
        Assert.Equal(CapriDefaultSettings.DefaultRequestTimeoutSeconds, store.Current.Provider.RequestTimeoutSeconds);
    }

    [Fact]
    public async Task SettingsFileContainsNoCredentialFields()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        using var store = new JsonAppSettingsStore(paths);

        await store.UpdateAsync(store.Current);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(paths.SettingsFile));
        Assert.False(document.RootElement.TryGetProperty("apiKey", out _));
        Assert.False(document.RootElement.GetProperty("provider").TryGetProperty("apiKey", out _));
    }
}
