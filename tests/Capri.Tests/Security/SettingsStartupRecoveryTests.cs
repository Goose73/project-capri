using System.IO;
using System.Text.Json;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Security;

public sealed class SettingsStartupRecoveryTests
{
    [Fact]
    public async Task FreshInstallationCreatesManagedSafeDefaultsWithoutCloudPermission()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);

        var result = await SettingsStartupRecovery.RunAsync(paths);

        Assert.Equal(SettingsStartupRecovery.Status.CreatedDefaults, result.State);
        Assert.False(result.NeedsReview);
        Assert.True(File.Exists(paths.SettingsFile));
        Assert.True(File.Exists(Path.Combine(paths.Root, "settings.last-good.json")));
        Assert.True(File.Exists(Path.Combine(paths.Root, "settings.managed.json")));
        using var store = new JsonAppSettingsStore(paths);
        Assert.False(store.Current.Provider.AllowCloudInference);
        Assert.True(store.Current.Provider.FreeModelsOnly);
        Assert.False(store.Current.AmbientVision.Enabled);
    }

    [Fact]
    public async Task BootingExistingValidCustomizedSettingsNeverChangesBytes()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        const string custom = """
            {
              "schemaVersion": 2,
              "provider": {
                "providerId": "custom-provider",
                "baseUrl": "https://custom.example.invalid/api",
                "chatModel": "model:private",
                "temperature": 0.19,
                "customProviderFeature": {"nested": [1,2,3]}
              },
              "customTopLevel": {"preserve": true}
            }
            """;
        await File.WriteAllTextAsync(paths.SettingsFile, custom);

        var report = await SettingsStartupRecovery.RunAsync(paths);

        Assert.Equal(SettingsStartupRecovery.Status.ExistingCustom, report.State);
        Assert.Equal(custom, await File.ReadAllTextAsync(paths.SettingsFile));
        Assert.False(File.Exists(Path.Combine(paths.Root, "settings.managed.json")));
        using var store = new JsonAppSettingsStore(paths);
        Assert.Equal("custom-provider", store.Current.Provider.ProviderId);
    }

    [Fact]
    public async Task ExistingEmptyUnmanagedFileIsNotOverwritten()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        await File.WriteAllBytesAsync(paths.SettingsFile, []);

        var result = await SettingsStartupRecovery.RunAsync(paths);

        Assert.True(result.NeedsReview);
        Assert.Empty(await File.ReadAllBytesAsync(paths.SettingsFile));
        Assert.False(File.Exists(Path.Combine(paths.Root, "settings.last-good.json")));
    }

    [Fact]
    public async Task EmptyManagedSettingsRestoreVerifiedLastGoodAndArchiveDamage()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        await SettingsStartupRecovery.RunAsync(paths);
        using (var settings = new JsonAppSettingsStore(paths))
        {
            await settings.UpdateAsync(settings.Current with
            {
                Provider = settings.Current.Provider with
                {
                    ProviderId = "ollama",
                    BaseUrl = "http://localhost:11434/v1",
                    ChatModel = "my-optimized-model",
                },
            });
        }
        var verified = await File.ReadAllBytesAsync(paths.SettingsFile);
        await File.WriteAllBytesAsync(paths.SettingsFile, []);

        var outcome = await SettingsStartupRecovery.RunAsync(paths);

        Assert.Equal(SettingsStartupRecovery.Status.RestoredSnapshot, outcome.State);
        Assert.Equal(verified, await File.ReadAllBytesAsync(paths.SettingsFile));
        Assert.Single(Directory.GetFiles(paths.Root, "settings.json.damaged-*.bak"));
        using var reloaded = new JsonAppSettingsStore(paths);
        Assert.Equal("my-optimized-model", reloaded.Current.Provider.ChatModel);
    }

    [Fact]
    public async Task MissingManagedSettingsRestoreVerifiedLastGood()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        await SettingsStartupRecovery.RunAsync(paths);
        var original = await File.ReadAllBytesAsync(paths.SettingsFile);
        File.Delete(paths.SettingsFile);

        var outcome = await SettingsStartupRecovery.RunAsync(paths);

        Assert.Equal(SettingsStartupRecovery.Status.RestoredSnapshot, outcome.State);
        Assert.Equal(original, await File.ReadAllBytesAsync(paths.SettingsFile));
    }

    [Fact]
    public async Task MalformedButNonemptyManagedSettingsAreNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        await SettingsStartupRecovery.RunAsync(paths);
        const string customDamage = "{\"provider\": {\"customKey\": \"user-work\"";
        await File.WriteAllTextAsync(paths.SettingsFile, customDamage);

        var result = await SettingsStartupRecovery.RunAsync(paths);

        Assert.True(result.NeedsReview);
        Assert.Equal(customDamage, await File.ReadAllTextAsync(paths.SettingsFile));
    }

    [Fact]
    public async Task TamperedRecoveryBackupNeverAuthorizesOverwrite()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        await SettingsStartupRecovery.RunAsync(paths);
        await File.WriteAllTextAsync(Path.Combine(paths.Root, "settings.last-good.json"),
            "{\"provider\": {\"chatModel\": \"tampered\"}}");
        await File.WriteAllBytesAsync(paths.SettingsFile, []);

        var result = await SettingsStartupRecovery.RunAsync(paths);

        Assert.True(result.NeedsReview);
        Assert.Empty(await File.ReadAllBytesAsync(paths.SettingsFile));
    }

    [Fact]
    public async Task ManualChangeToPreviouslyManagedSettingsDisablesFutureRestore()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        await SettingsStartupRecovery.RunAsync(paths);
        const string custom = """
            {"schemaVersion":2,"provider":{"providerId":"ollama","chatModel":"hand-tuned","customOption":42}}
            """;
        await File.WriteAllTextAsync(paths.SettingsFile, custom);

        var result = await SettingsStartupRecovery.RunAsync(paths);
        Assert.Equal(SettingsStartupRecovery.Status.ExistingCustom, result.State);
        Assert.Equal(custom, await File.ReadAllTextAsync(paths.SettingsFile));
        Assert.True(File.Exists(Path.Combine(paths.Root, "settings.user-managed")));

        await File.WriteAllBytesAsync(paths.SettingsFile, []);
        var later = await SettingsStartupRecovery.RunAsync(paths);
        Assert.True(later.NeedsReview);
        Assert.Empty(await File.ReadAllBytesAsync(paths.SettingsFile));
    }

    [Fact]
    public async Task StartupIsIdempotentAndDoesNotRewriteHealthyManagedConfig()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        await SettingsStartupRecovery.RunAsync(paths);
        var before = await File.ReadAllBytesAsync(paths.SettingsFile);

        var result = await SettingsStartupRecovery.RunAsync(paths);

        Assert.Equal(SettingsStartupRecovery.Status.Healthy, result.State);
        Assert.Equal(before, await File.ReadAllBytesAsync(paths.SettingsFile));
    }

    [Fact]
    public async Task AppSavedCustomExtrasSurviveRecoveryAndUnrelatedSettingsEdits()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        await SettingsStartupRecovery.RunAsync(paths);
        using (var store = new JsonAppSettingsStore(paths))
        {
            using var json = JsonDocument.Parse("""{"custom":true}""");
            var custom = json.RootElement.Clone();
            await store.UpdateAsync(store.Current with
            {
                CustomProperties = new Dictionary<string, JsonElement>
                {
                    ["thirdPartyOptions"] = custom
                },
                Provider = store.Current.Provider with
                {
                    CustomProperties = new Dictionary<string, JsonElement>
                    {
                        ["runtimeOptions"] = custom
                    }
                }
            });
        }

        await File.WriteAllBytesAsync(paths.SettingsFile, []);
        var result = await SettingsStartupRecovery.RunAsync(paths);
        Assert.Equal(SettingsStartupRecovery.Status.RestoredSnapshot, result.State);
        using var restored = new JsonAppSettingsStore(paths);
        Assert.True(restored.Current.CustomProperties!["thirdPartyOptions"]
            .GetProperty("custom").GetBoolean());
        Assert.True(restored.Current.Provider.CustomProperties!["runtimeOptions"]
            .GetProperty("custom").GetBoolean());
    }
}
