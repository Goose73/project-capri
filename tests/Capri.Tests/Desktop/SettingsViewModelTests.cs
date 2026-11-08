using System.Runtime.CompilerServices;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Core.Providers;
using Capri.Desktop.ViewModels;
using Capri.Providers;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Desktop;

/// <summary>
/// Headless tests for provider presets, profile-scoped credential keys and the
/// local-vs-cloud API-key/consent rules. No WPF window or network call is used.
/// </summary>
public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task ReduceMotionLoadsAndSavesThroughSharedSettings()
    {
        var settings = CreateSettings(ProviderCatalog.OllamaId, ProviderCatalog.Ollama.DefaultBaseUrl);
        var viewModel = CreateViewModel(settings with { Ui = settings.Ui with { ReduceMotion = true } }, new FakeCredentialStore());
        await viewModel.LoadAsync();
        Assert.True(viewModel.ReduceMotion);

        viewModel.ReduceMotion = false;
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.False(viewModel.ReduceMotion);
    }

    [Fact]
    public async Task LoadUsesProfileScopedCredentialKeyForStatus()
    {
        var credentials = new FakeCredentialStore();
        await credentials.SetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenAi), "profile-key");
        await credentials.SetAsync(ProviderCredentials.LegacyApiKeyCredentialKey, "legacy-key");
        var viewModel = CreateViewModel(
            CreateSettings(ProviderCatalog.OpenAiId, ProviderCatalog.OpenAi.DefaultBaseUrl),
            credentials);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasStoredApiKey);
        Assert.Equal("API key stored securely for this provider profile.", viewModel.ApiKeyStatus);
    }

    [Fact]
    public async Task LoadDoesNotTreatLegacySharedKeyAsTheSelectedProfileKey()
    {
        var credentials = new FakeCredentialStore();
        await credentials.SetAsync(ProviderCredentials.LegacyApiKeyCredentialKey, "legacy-key");
        var viewModel = CreateViewModel(
            CreateSettings(ProviderCatalog.OpenAiId, ProviderCatalog.OpenAi.DefaultBaseUrl),
            credentials);

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasStoredApiKey);
        Assert.Equal("No API key stored for this provider profile.", viewModel.ApiKeyStatus);
    }

    [Fact]
    public async Task SelectingPresetClearsPendingKeyAndDoesNotWriteItToTheNewProfile()
    {
        var credentials = new FakeCredentialStore();
        await credentials.SetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenAi), "stored-key");
        var viewModel = CreateViewModel(
            CreateSettings(ProviderCatalog.OpenAiId, ProviderCatalog.OpenAi.DefaultBaseUrl),
            credentials);
        await viewModel.LoadAsync();
        viewModel.SetPendingApiKey("typed-but-unsaved");
        var clearedEvents = 0;
        viewModel.PendingApiKeyCleared += (_, _) => clearedEvents++;

        viewModel.SelectedPreset = viewModel.Presets.Single(
            preset => string.Equals(preset.ProviderId, ProviderCatalog.OpenRouterId, StringComparison.Ordinal));

        Assert.Equal(1, clearedEvents);
        Assert.Equal(ProviderCatalog.OpenRouterId, viewModel.ProviderId);
        Assert.Equal(ProviderCatalog.OpenRouter.DefaultBaseUrl, viewModel.BaseUrl);
        Assert.EndsWith(":free", viewModel.ChatModel, StringComparison.Ordinal);
        Assert.True(viewModel.RequiresApiKey);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(await credentials.GetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenRouterId)));
        Assert.Equal("stored-key", await credentials.GetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenAi)));
    }

    [Fact]
    public async Task ClearApiKeyDeletesOnlyTheSelectedProfileKey()
    {
        var credentials = new FakeCredentialStore();
        await credentials.SetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenAi), "openai-key");
        await credentials.SetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OllamaId), "ollama-key");
        var viewModel = CreateViewModel(
            CreateSettings(ProviderCatalog.OpenAiId, ProviderCatalog.OpenAi.DefaultBaseUrl),
            credentials);
        await viewModel.LoadAsync();

        await viewModel.ClearApiKeyCommand.ExecuteAsync(null);

        Assert.Null(await credentials.GetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenAi)));
        Assert.Equal("ollama-key", await credentials.GetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OllamaId)));
    }

    [Fact]
    public async Task SwitchingProfilesAfterClearDoesNotCarryTheClearRequest()
    {
        var credentials = new FakeCredentialStore();
        await credentials.SetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenAi), "openai-key");
        await credentials.SetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenRouterId), "router-key");
        var viewModel = CreateViewModel(
            CreateSettings(ProviderCatalog.OpenAiId, ProviderCatalog.OpenAi.DefaultBaseUrl),
            credentials);
        await viewModel.LoadAsync();

        await viewModel.ClearApiKeyCommand.ExecuteAsync(null);
        viewModel.SelectedPreset = viewModel.Presets.Single(
            preset => string.Equals(preset.ProviderId, ProviderCatalog.OpenRouterId, StringComparison.Ordinal));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(await credentials.GetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenAi)));
        Assert.Equal("router-key", await credentials.GetAsync(ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenRouterId)));
    }

    [Fact]
    public async Task LocalOllamaProfileNeedsNoApiKeyAndDisablesKeyEntry()
    {
        var viewModel = CreateViewModel(
            CreateSettings(ProviderCatalog.OllamaId, ProviderCatalog.Ollama.DefaultBaseUrl),
            new FakeCredentialStore());

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsLocalProvider);
        Assert.False(viewModel.IsCloudProvider);
        Assert.False(viewModel.RequiresApiKey);
        Assert.False(viewModel.CanEditApiKey);
        Assert.True(viewModel.ShowNoApiKeyRequired);
        Assert.Equal(SettingsViewModel.NoApiKeyRequiredStatus, viewModel.ApiKeyStatus);
        Assert.Empty(viewModel.CloudConsentWarning);
    }

    [Fact]
    public async Task CloudProfileRequiresApiKeyAndShowsConsentWarningUntilAllowed()
    {
        var viewModel = CreateViewModel(
            CreateSettings(ProviderCatalog.OpenAiId, ProviderCatalog.OpenAi.DefaultBaseUrl, allowCloudInference: false),
            new FakeCredentialStore());

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsCloudProvider);
        Assert.True(viewModel.RequiresApiKey);
        Assert.True(viewModel.CanEditApiKey);
        Assert.True(viewModel.IsCloudInferenceBlocked);
        Assert.NotEmpty(viewModel.CloudConsentWarning);

        viewModel.AllowCloudInference = true;

        Assert.False(viewModel.IsCloudInferenceBlocked);
        Assert.Empty(viewModel.CloudConsentWarning);
    }

    [Fact]
    public async Task CustomLoopbackProviderUsesLocalNoKeyRules()
    {
        var viewModel = CreateViewModel(
            CreateSettings("my-local-provider", "http://127.0.0.1:9999/v1"),
            new FakeCredentialStore());

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsLocalProvider);
        Assert.False(viewModel.RequiresApiKey);
        Assert.Equal(SettingsViewModel.NoApiKeyRequiredStatus, viewModel.ApiKeyStatus);
    }

    [Fact]
    public async Task InferenceTestIsBlockedWithoutCloudConsentAndSendsNoRequest()
    {
        var viewModel = CreateViewModel(
            CreateSettings(
                ProviderCatalog.OpenAiId,
                ProviderCatalog.OpenAi.DefaultBaseUrl,
                allowCloudInference: false,
                freeModelsOnly: false),
            new FakeCredentialStore());
        await viewModel.LoadAsync();

        await viewModel.TestInferenceCommand.ExecuteAsync(null);

        Assert.Contains("Inference blocked", viewModel.TestInferenceStatus, StringComparison.Ordinal);
        Assert.Contains(FreeModelGuard.CloudInferenceDisabledMessage, viewModel.TestInferenceStatus, StringComparison.Ordinal);
        Assert.Contains("no request sent", viewModel.TestInferenceStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthCheckIsBlockedWithoutCloudConsentAndSendsNoRequest()
    {
        var viewModel = CreateViewModel(
            CreateSettings(ProviderCatalog.OpenAiId, ProviderCatalog.OpenAi.DefaultBaseUrl, allowCloudInference: false),
            new FakeCredentialStore());
        await viewModel.LoadAsync();

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.Contains("Health check blocked", viewModel.TestConnectionStatus, StringComparison.Ordinal);
        Assert.Contains(FreeModelGuard.CloudInferenceDisabledMessage, viewModel.TestConnectionStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelRefreshIsBlockedWithoutCloudConsent()
    {
        var viewModel = CreateViewModel(
            CreateSettings(ProviderCatalog.OpenAiId, ProviderCatalog.OpenAi.DefaultBaseUrl, allowCloudInference: false),
            new FakeCredentialStore());
        await viewModel.LoadAsync();

        await viewModel.RefreshModelsCommand.ExecuteAsync(null);

        Assert.Contains("Model refresh blocked", viewModel.ModelDiscoveryStatus, StringComparison.Ordinal);
        Assert.Empty(viewModel.AvailableModels);
    }

    [Fact]
    public async Task SavePersistsCloudConsentAndFreeOnlyFlags()
    {
        var store = new FakeAppSettingsStore(
            CreateSettings(ProviderCatalog.OpenAiId, ProviderCatalog.OpenAi.DefaultBaseUrl, allowCloudInference: false));
        var viewModel = CreateViewModel(store, new FakeCredentialStore());
        await viewModel.LoadAsync();

        viewModel.AllowCloudInference = true;
        viewModel.FreeModelsOnly = false;
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Settings saved.", viewModel.SaveStatus);
        Assert.True(store.Current.Provider.AllowCloudInference);
        Assert.False(store.Current.Provider.FreeModelsOnly);
        Assert.NotNull(store.Current.Provider.CloudConsentRecordedUtc);
    }

    [Fact]
    public async Task SelectingDiscoveredModelUpdatesEditableModelField()
    {
        var viewModel = CreateViewModel(
            CreateSettings(ProviderCatalog.OllamaId, ProviderCatalog.Ollama.DefaultBaseUrl),
            new FakeCredentialStore());
        await viewModel.LoadAsync();
        viewModel.AvailableModels.Add("llama3.2:latest");

        viewModel.SelectedDiscoveredModel = "llama3.2:latest";

        Assert.Equal("llama3.2:latest", viewModel.ChatModel);
    }

    private static SettingsViewModel CreateViewModel(AppSettings settings, FakeCredentialStore credentials)
    {
        return CreateViewModel(new FakeAppSettingsStore(settings), credentials);
    }

    private static SettingsViewModel CreateViewModel(FakeAppSettingsStore settings, FakeCredentialStore credentials)
    {
        return new SettingsViewModel(settings, credentials, new NoopChatProvider());
    }

    private static AppSettings CreateSettings(
        string providerId,
        string baseUrl,
        bool allowCloudInference = false,
        bool freeModelsOnly = true,
        string model = "test-model")
    {
        var defaults = CapriDefaultSettings.Create();
        return defaults with
        {
            Provider = defaults.Provider with
            {
                ProviderId = providerId,
                BaseUrl = baseUrl,
                ChatModel = model,
                AllowCloudInference = allowCloudInference,
                FreeModelsOnly = freeModelsOnly
            }
        };
    }

    private sealed class NoopChatProvider : IChatProvider
    {
        public string ProviderId => "settings-test-noop";

        public Task<ChatCompletion> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatCompletion(new ChatMessage(ChatRole.Assistant, "noop")));
        }

        public async IAsyncEnumerable<ChatDelta> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProviderHealth(true, "noop"));
        }
    }
}
