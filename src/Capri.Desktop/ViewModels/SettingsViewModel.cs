using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Core.Providers;
using Capri.Providers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Status shown when the selected provider/profile never uses an API key.</summary>
    public const string NoApiKeyRequiredStatus = "No API key required.";

    private const string CustomProviderIdFallback = "custom";
    private const string InferencePrompt = "Reply with: Capri ready";
    private const int InferenceMaxOutputTokens = 32;

    private readonly IAppSettingsStore _settingsStore;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly IChatProvider _chatProvider;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ProviderPresetOption _customPreset;
    private string? _pendingApiKey; // never exposed as a bindable persisted property
    private bool _clearApiKeyRequested;
    private bool _isLoading;
    private bool _isApplyingPreset;

    [ObservableProperty]
    private string _providerId = string.Empty;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _chatModel = string.Empty;

    [ObservableProperty]
    private ProviderPresetOption _selectedPreset = null!;

    [ObservableProperty]
    private string? _selectedDiscoveredModel;

    [ObservableProperty]
    private bool _enableStreaming = true;

    [ObservableProperty]
    private double _temperature = 0.7;

    [ObservableProperty]
    private int _maxOutputTokens = 1024;

    [ObservableProperty]
    private int _requestTimeoutSeconds = 60;

    [ObservableProperty]
    private bool _automationEnabled = true;

    [ObservableProperty]
    private bool _allowAppLaunch = true;

    [ObservableProperty]
    private bool _allowWebLaunch = true;

    [ObservableProperty]
    private bool _allowMediaControl = true;

    [ObservableProperty]
    private int _characterHeight = 250;

    [ObservableProperty]
    private bool _reduceMotion;

    [ObservableProperty]
    private bool _allowCloudInference;

    [ObservableProperty]
    private bool _freeModelsOnly = true;

    [ObservableProperty]
    private string _apiKeyStatus = "Checking...";

    [ObservableProperty]
    private bool _hasStoredApiKey;

    [ObservableProperty]
    private string _testConnectionStatus = string.Empty;

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private string _testInferenceStatus = string.Empty;

    [ObservableProperty]
    private bool _isTestingInference;

    [ObservableProperty]
    private string _modelDiscoveryStatus = string.Empty;

    [ObservableProperty]
    private bool _isRefreshingModels;

    [ObservableProperty]
    private string _saveStatus = string.Empty;

    public SettingsViewModel(
        IAppSettingsStore settingsStore,
        ISecureCredentialStore credentialStore,
        IChatProvider chatProvider,
        ILogger<SettingsViewModel>? logger = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _chatProvider = chatProvider ?? throw new ArgumentNullException(nameof(chatProvider));
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;

        Presets = CreatePresets();
        _customPreset = Presets[^1];
        _selectedPreset = _customPreset;
    }

    /// <summary>Raised when the pending key entry must be discarded (for example after a profile switch).</summary>
    public event EventHandler? PendingApiKeyCleared;

    /// <summary>Known provider presets. Selecting one fills provider/base URL/model defaults.</summary>
    public IReadOnlyList<ProviderPresetOption> Presets { get; }

    /// <summary>Model ids returned by the most recent successful model refresh.</summary>
    public ObservableCollection<string> AvailableModels { get; } = [];

    /// <summary>Provider id used for profile-scoped credential operations.</summary>
    public string SelectedProfileId
    {
        get
        {
            var id = (ProviderId ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(id) ? CustomProviderIdFallback : id;
        }
    }

    /// <summary>True when the current provider runs on this machine.</summary>
    public bool IsLocalProvider => ResolveCurrentProfile() is { IsLocal: true };

    /// <summary>True when the current provider is (or is treated as) a cloud service.</summary>
    public bool IsCloudProvider => !IsLocalProvider;

    /// <summary>True when the current provider/profile requires an API key.</summary>
    public bool RequiresApiKey
    {
        get
        {
            var profile = ResolveCurrentProfile();
            if (profile is null)
            {
                // Unknown non-loopback providers are treated as cloud and require a credential.
                return true;
            }

            return profile.RequiresApiKey;
        }
    }

    /// <summary>True when the API key entry controls should be enabled.</summary>
    public bool CanEditApiKey => RequiresApiKey;

    /// <summary>True when the selected provider needs no API key (local/no-auth profile).</summary>
    public bool ShowNoApiKeyRequired => !RequiresApiKey;

    /// <summary>User-facing key-entry guidance for the selected profile.</summary>
    public string ApiKeyHelpText => RequiresApiKey
        ? "Leave blank to keep the current stored key unchanged. Keys are stored per provider profile."
        : "This provider does not use an API key; the key entry is disabled.";

    /// <summary>Warning shown when a cloud provider is selected without explicit consent.</summary>
    public string CloudConsentWarning => IsCloudProvider && !AllowCloudInference
        ? "Cloud inference is disabled. Enable 'Allow cloud inference' before checking the connection, testing inference, refreshing cloud models, or saving cloud requests."
        : string.Empty;

    /// <summary>True when cloud actions must be blocked until consent is granted.</summary>
    public bool IsCloudInferenceBlocked => IsCloudProvider && !AllowCloudInference;

    /// <summary>Explains why free-only mode may block a provider with no free model tier.</summary>
    public string FreeModelsOnlyHint
    {
        get
        {
            var profile = ResolveCurrentProfile();
            return profile is { IsCloud: true, SupportsFreeModelsOnly: false } && FreeModelsOnly
                ? "This cloud provider does not offer free models. Turn off 'Free models only' to use it; Capri will never auto-select a paid model."
                : string.Empty;
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var current = _settingsStore.Current;
        _isLoading = true;
        try
        {
            ProviderId = current.Provider.ProviderId;
            BaseUrl = current.Provider.BaseUrl;
            ChatModel = current.Provider.ChatModel;
            EnableStreaming = current.Provider.EnableStreaming;
            Temperature = current.Provider.Temperature;
            MaxOutputTokens = current.Provider.MaxOutputTokens;
            RequestTimeoutSeconds = current.Provider.RequestTimeoutSeconds;
            AllowCloudInference = current.Provider.AllowCloudInference;
            FreeModelsOnly = current.Provider.FreeModelsOnly;
            AutomationEnabled = current.Automation.Enabled;
            AllowAppLaunch = current.Automation.AllowAppLaunch;
            AllowWebLaunch = current.Automation.AllowWebLaunch;
            AllowMediaControl = current.Automation.AllowMediaControl;
            CharacterHeight = Math.Clamp(current.Ui.CharacterHeight, 150, 400);
            ReduceMotion = current.Ui.ReduceMotion;
            SelectedPreset = FindPreset(current.Provider.ProviderId, current.Provider.BaseUrl);
        }
        finally
        {
            _isLoading = false;
        }

        AvailableModels.Clear();
        SelectedDiscoveredModel = null;
        _pendingApiKey = null;
        _clearApiKeyRequested = false;
        PendingApiKeyCleared?.Invoke(this, EventArgs.Empty);
        RaiseProfileFlags();
        await RefreshApiKeyStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public void SetPendingApiKey(string? value)
    {
        if (!RequiresApiKey)
        {
            // Local/no-auth profiles never need (or accept) a dummy key.
            _pendingApiKey = null;
            return;
        }

        // Called from PasswordChanged; do not store in observable properties or logs.
        _pendingApiKey = string.IsNullOrWhiteSpace(value) ? null : value;
        if (_pendingApiKey is not null)
        {
            _clearApiKeyRequested = false;
        }
    }

    [RelayCommand]
    private async Task ClearApiKeyAsync()
    {
        _pendingApiKey = null;
        PendingApiKeyCleared?.Invoke(this, EventArgs.Empty);

        if (!RequiresApiKey)
        {
            _clearApiKeyRequested = false;
            HasStoredApiKey = false;
            ApiKeyStatus = NoApiKeyRequiredStatus;
            SaveStatus = "No API key is required for this provider.";
            return;
        }

        _clearApiKeyRequested = true;
        try
        {
            await _credentialStore.DeleteAsync(GetProfileCredentialKey()).ConfigureAwait(false);
            await RefreshApiKeyStatusAsync().ConfigureAwait(false);
            SaveStatus = "Stored API key cleared for this provider profile.";
        }
        catch (Exception ex)
        {
            LogClearApiKeyFailed(_logger, ex);
            SaveStatus = "Failed to clear the stored API key.";
        }
    }

    public async Task RefreshApiKeyStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!RequiresApiKey)
        {
            HasStoredApiKey = false;
            ApiKeyStatus = NoApiKeyRequiredStatus;
            return;
        }

        var credentialKey = GetProfileCredentialKey();
        try
        {
            var has = await _credentialStore.HasAsync(credentialKey, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(credentialKey, GetProfileCredentialKey(), StringComparison.Ordinal))
            {
                // The user switched profiles while the check was in flight.
                return;
            }

            HasStoredApiKey = has;
            ApiKeyStatus = has
                ? "API key stored securely for this provider profile."
                : "No API key stored for this provider profile.";
        }
        catch (Exception ex)
        {
            LogReadCredentialStatusFailed(_logger, ex);
            HasStoredApiKey = false;
            ApiKeyStatus = "Unable to check stored API key status.";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var current = _settingsStore.Current;
            var updated = current with
            {
                Provider = current.Provider with
                {
                    ProviderId = (ProviderId ?? string.Empty).Trim(),
                    BaseUrl = (BaseUrl ?? string.Empty).Trim(),
                    ChatModel = (ChatModel ?? string.Empty).Trim(),
                    EnableStreaming = EnableStreaming,
                    Temperature = Temperature,
                    MaxOutputTokens = MaxOutputTokens,
                    RequestTimeoutSeconds = RequestTimeoutSeconds,
                    FreeModelsOnly = FreeModelsOnly,
                    AllowCloudInference = AllowCloudInference,
                    CloudConsentRecordedUtc = ResolveConsentTimestamp(current.Provider)
                },
                Automation = current.Automation with
                {
                    Enabled = AutomationEnabled,
                    AllowAppLaunch = AllowAppLaunch,
                    AllowWebLaunch = AllowWebLaunch,
                    AllowMediaControl = AllowMediaControl
                },
                Ui = current.Ui with
                {
                    CharacterHeight = Math.Clamp(CharacterHeight, 150, 400),
                    ReduceMotion = ReduceMotion
                }
            };

            await _settingsStore.UpdateAsync(updated).ConfigureAwait(false);

            if (RequiresApiKey)
            {
                var credentialKey = GetProfileCredentialKey();
                if (_clearApiKeyRequested)
                {
                    await _credentialStore.DeleteAsync(credentialKey).ConfigureAwait(false);
                }
                else if (!string.IsNullOrWhiteSpace(_pendingApiKey))
                {
                    await _credentialStore.SetAsync(credentialKey, _pendingApiKey).ConfigureAwait(false);
                }
            }

            _pendingApiKey = null;
            _clearApiKeyRequested = false;
            await RefreshApiKeyStatusAsync().ConfigureAwait(false);
            SaveStatus = "Settings saved.";
        }
        catch (Exception ex)
        {
            LogSaveSettingsFailed(_logger, ex);
            SaveStatus = "Failed to save settings. Check logs for details.";
        }
    }

    /// <summary>
    /// Transient health probe against the models endpoint. This is a connectivity
    /// check only and never proves that chat inference works.
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsTestingConnection)
        {
            return;
        }

        try
        {
            IsTestingConnection = true;
            TestConnectionStatus = "Health check: testing connection...";

            var providerSettings = BuildProviderSettingsSnapshot();
            var profile = FreeModelGuard.ResolveProfile(providerSettings);
            var guard = FreeModelGuard.EvaluateHealth(providerSettings, profile);
            if (!guard.IsAllowed)
            {
                TestConnectionStatus = $"Health check blocked: {guard.Message} (no request sent)";
                return;
            }

            var testingPendingKey = RequiresApiKeyForProfile(profile) && !string.IsNullOrWhiteSpace(_pendingApiKey);
            var probeApiKey = await ResolveProbeApiKeyAsync(profile).ConfigureAwait(false);

            // Probe with transient in-memory overrides; do not mutate persisted settings or credentials.
            using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var probeProvider = new OpenAiCompatibleChatProvider(
                httpClient,
                new TransientSettingsStore(_settingsStore.Current with { Provider = providerSettings }),
                new TransientCredentialStore(GetProfileCredentialKey(), probeApiKey));

            var health = await probeProvider.CheckHealthAsync().ConfigureAwait(false);
            var keyNote = testingPendingKey ? " (tested with entered key, not saved)" : string.Empty;
            if (health.IsHealthy)
            {
                var latency = health.Latency.HasValue ? $" ({health.Latency.Value.TotalMilliseconds:0} ms)" : string.Empty;
                TestConnectionStatus = $"Health check success: {health.Message ?? "Provider is reachable."}{latency}{keyNote}";
            }
            else
            {
                TestConnectionStatus = $"Health check failed: {health.Message ?? "Provider is not reachable."}{keyNote}";
            }
        }
        catch (Exception ex)
        {
            LogTestConnectionFailed(_logger, ex);
            TestConnectionStatus = "Health check failed: could not complete the connection test.";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    /// <summary>
    /// Sends one minimal chat request ("Reply with: Capri ready") to prove that
    /// real inference works. Settings and credentials are never saved by this action.
    /// </summary>
    [RelayCommand]
    private async Task TestInferenceAsync()
    {
        if (IsTestingInference)
        {
            return;
        }

        try
        {
            IsTestingInference = true;
            TestInferenceStatus = "Inference test: sending a minimal chat request...";

            var providerSettings = BuildProviderSettingsSnapshot();
            var profile = FreeModelGuard.ResolveProfile(providerSettings);
            var guard = FreeModelGuard.Evaluate(providerSettings, profile, providerSettings.ChatModel);
            if (!guard.IsAllowed)
            {
                TestInferenceStatus = $"Inference blocked: {guard.Message} (no request sent)";
                return;
            }

            var probeApiKey = await ResolveProbeApiKeyAsync(profile).ConfigureAwait(false);
            using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var probeProvider = new OpenAiCompatibleChatProvider(
                httpClient,
                new TransientSettingsStore(_settingsStore.Current with { Provider = providerSettings }),
                new TransientCredentialStore(GetProfileCredentialKey(), probeApiKey));

            var request = new ChatRequest(
                new[] { new ChatMessage(ChatRole.User, InferencePrompt) },
                ModelId: providerSettings.ChatModel,
                Temperature: 0,
                MaxOutputTokens: InferenceMaxOutputTokens);

            var completion = await probeProvider.CompleteAsync(request).ConfigureAwait(false);
            if (completion.Error is not null)
            {
                TestInferenceStatus = $"Inference failed: {completion.Error.Message}";
                return;
            }

            var model = string.IsNullOrWhiteSpace(completion.ModelId) ? providerSettings.ChatModel : completion.ModelId;
            TestInferenceStatus = $"Inference succeeded: the provider returned a chat response (model: {model}).";
        }
        catch (Exception ex)
        {
            LogTestInferenceFailed(_logger, ex);
            TestInferenceStatus = "Inference failed: could not complete the inference test.";
        }
        finally
        {
            IsTestingInference = false;
        }
    }

    /// <summary>
    /// Fetches the provider's model list from {baseUrl}/models. Local providers
    /// are contacted without credentials; cloud providers retain the consent guard.
    /// This never downloads models.
    /// </summary>
    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        if (IsRefreshingModels)
        {
            return;
        }

        var providerSettings = BuildProviderSettingsSnapshot();
        var profile = FreeModelGuard.ResolveProfile(providerSettings);
        var guard = FreeModelGuard.EvaluateHealth(providerSettings, profile);
        if (!guard.IsAllowed)
        {
            ModelDiscoveryStatus = $"Model refresh blocked: {guard.Message} (no request sent)";
            return;
        }

        if (!TryBuildModelsEndpoint(providerSettings.BaseUrl, out var endpoint, out var endpointError))
        {
            ModelDiscoveryStatus = $"Cannot refresh models: {endpointError}";
            return;
        }

        try
        {
            IsRefreshingModels = true;
            ModelDiscoveryStatus = "Refreshing models...";

            string? apiKey = null;
            if (RequiresApiKeyForProfile(profile))
            {
                apiKey = await ResolveProbeApiKeyAsync(profile).ConfigureAwait(false);
            }

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Clamp(providerSettings.RequestTimeoutSeconds, 1, 300))
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }

            using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                ModelDiscoveryStatus = MapModelDiscoveryFailure(response.StatusCode);
                return;
            }

            var modelIds = ParseModelIds(body);
            AvailableModels.Clear();
            foreach (var modelId in modelIds)
            {
                AvailableModels.Add(modelId);
            }

            SelectedDiscoveredModel = AvailableModels.FirstOrDefault(
                id => string.Equals(id, ChatModel, StringComparison.OrdinalIgnoreCase));

            ModelDiscoveryStatus = modelIds.Length == 0
                ? "No models were returned by the provider."
                : $"Models refreshed: {modelIds.Length} discovered.";
        }
        catch (OperationCanceledException)
        {
            ModelDiscoveryStatus = "Model refresh timed out.";
        }
        catch (HttpRequestException)
        {
            ModelDiscoveryStatus = "Model refresh failed: could not reach the provider.";
        }
        catch (IOException)
        {
            ModelDiscoveryStatus = "Model refresh failed: the provider connection failed.";
        }
        catch (Exception ex)
        {
            LogRefreshModelsFailed(_logger, ex);
            ModelDiscoveryStatus = "Model refresh failed: could not read the provider response.";
        }
        finally
        {
            IsRefreshingModels = false;
        }
    }

    private string GetProfileCredentialKey()
    {
        return ProviderCredentials.GetApiKeyCredentialKey(SelectedProfileId);
    }

    private ProviderProfile? ResolveCurrentProfile()
    {
        var provider = _settingsStore.Current.Provider with
        {
            ProviderId = NormalizeProviderId(ProviderId),
            BaseUrl = (BaseUrl ?? string.Empty).Trim()
        };

        return FreeModelGuard.ResolveProfile(provider);
    }

    private static bool RequiresApiKeyForProfile(ProviderProfile? profile)
    {
        if (profile is { IsLocal: true })
        {
            return false;
        }

        return profile is null || profile.RequiresApiKey;
    }

    private async Task<string?> ResolveProbeApiKeyAsync(ProviderProfile? profile)
    {
        if (!RequiresApiKeyForProfile(profile))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_pendingApiKey))
        {
            return _pendingApiKey;
        }

        try
        {
            return await _credentialStore.GetAsync(GetProfileCredentialKey()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogReadCredentialStatusFailed(_logger, ex);
            return null;
        }
    }

    private AppSettings.ProviderSettings BuildProviderSettingsSnapshot()
    {
        var current = _settingsStore.Current.Provider;
        return current with
        {
            ProviderId = NormalizeProviderId(ProviderId),
            BaseUrl = (BaseUrl ?? string.Empty).Trim(),
            ChatModel = (ChatModel ?? string.Empty).Trim(),
            EnableStreaming = EnableStreaming,
            Temperature = Temperature,
            MaxOutputTokens = MaxOutputTokens,
            RequestTimeoutSeconds = RequestTimeoutSeconds,
            FreeModelsOnly = FreeModelsOnly,
            AllowCloudInference = AllowCloudInference
        };
    }

    private DateTimeOffset? ResolveConsentTimestamp(AppSettings.ProviderSettings current)
    {
        if (!AllowCloudInference)
        {
            return null;
        }

        return current.CloudConsentRecordedUtc ?? DateTimeOffset.UtcNow;
    }

    private static string NormalizeProviderId(string? providerId)
    {
        var trimmed = (providerId ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? CustomProviderIdFallback : trimmed;
    }

    private ProviderPresetOption FindPreset(string? providerId, string? baseUrl)
    {
        var id = (providerId ?? string.Empty).Trim();
        var byId = Presets.FirstOrDefault(
            preset => !preset.IsCustom && string.Equals(preset.ProviderId, id, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            return byId;
        }

        if (ProviderCatalog.TryGetByBaseUrl(baseUrl, out var profile))
        {
            var byUrl = Presets.FirstOrDefault(
                preset => !preset.IsCustom && string.Equals(preset.ProviderId, profile.Id, StringComparison.OrdinalIgnoreCase));
            if (byUrl is not null)
            {
                return byUrl;
            }
        }

        return _customPreset;
    }

    private void RaiseProfileFlags()
    {
        OnPropertyChanged(nameof(SelectedProfileId));
        OnPropertyChanged(nameof(IsLocalProvider));
        OnPropertyChanged(nameof(IsCloudProvider));
        OnPropertyChanged(nameof(RequiresApiKey));
        OnPropertyChanged(nameof(CanEditApiKey));
        OnPropertyChanged(nameof(ShowNoApiKeyRequired));
        OnPropertyChanged(nameof(ApiKeyHelpText));
        OnPropertyChanged(nameof(CloudConsentWarning));
        OnPropertyChanged(nameof(IsCloudInferenceBlocked));
        OnPropertyChanged(nameof(FreeModelsOnlyHint));
    }

    private void ClearPendingKeyState()
    {
        _pendingApiKey = null;
        _clearApiKeyRequested = false;
        PendingApiKeyCleared?.Invoke(this, EventArgs.Empty);
    }

    partial void OnProviderIdChanged(string value)
    {
        if (_isApplyingPreset)
        {
            RaiseProfileFlags();
            return;
        }

        if (_isLoading)
        {
            return;
        }

        var match = Presets.FirstOrDefault(
            preset => !preset.IsCustom && string.Equals(preset.ProviderId, value, StringComparison.OrdinalIgnoreCase));

        _isApplyingPreset = true;
        try
        {
            SelectedPreset = match ?? _customPreset;
        }
        finally
        {
            _isApplyingPreset = false;
        }

        ClearPendingKeyState();
        RaiseProfileFlags();
        _ = RefreshApiKeyStatusAsync();
    }

    partial void OnBaseUrlChanged(string value)
    {
        RaiseProfileFlags();
        if (_isLoading || _isApplyingPreset)
        {
            return;
        }

        if (RequiresApiKey)
        {
            _ = RefreshApiKeyStatusAsync();
        }
        else
        {
            HasStoredApiKey = false;
            ApiKeyStatus = NoApiKeyRequiredStatus;
        }
    }

    partial void OnSelectedPresetChanged(ProviderPresetOption value)
    {
        if (_isLoading || _isApplyingPreset || value is null)
        {
            return;
        }

        _isApplyingPreset = true;
        try
        {
            if (!value.IsCustom)
            {
                ProviderId = value.ProviderId;
                BaseUrl = value.BaseUrl;
                ChatModel = value.DefaultModel;
            }
        }
        finally
        {
            _isApplyingPreset = false;
        }

        ClearPendingKeyState();
        RaiseProfileFlags();
        _ = RefreshApiKeyStatusAsync();
    }

    partial void OnAllowCloudInferenceChanged(bool value)
    {
        OnPropertyChanged(nameof(CloudConsentWarning));
        OnPropertyChanged(nameof(IsCloudInferenceBlocked));
    }

    partial void OnSelectedDiscoveredModelChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && !string.Equals(ChatModel, value, StringComparison.Ordinal))
        {
            ChatModel = value;
        }
    }

    partial void OnFreeModelsOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(FreeModelsOnlyHint));
    }

    private static bool TryBuildModelsEndpoint(string? baseUrl, out Uri endpoint, out string error)
    {
        endpoint = null!;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            error = "no provider base URL is configured.";
            return false;
        }

        var normalized = baseUrl.Trim().TrimEnd('/');
        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"/chat/completions".Length];
        }

        if (normalized.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"/models".Length];
        }

        var candidate = string.Concat(normalized, "/models");
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            error = "the provider base URL is not a valid HTTP or HTTPS address.";
            return false;
        }

        endpoint = uri;
        error = string.Empty;
        return true;
    }

    private static string[] ParseModelIds(string json)
    {
        var modelIds = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                CollectModelIds(root, modelIds);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "data", "models", "items" })
                {
                    if (root.TryGetProperty(propertyName, out var array)
                        && array.ValueKind == JsonValueKind.Array)
                    {
                        CollectModelIds(array, modelIds);
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed payloads simply produce no models; the caller shows a friendly status.
        }

        return modelIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CollectModelIds(JsonElement array, List<string> modelIds)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    modelIds.Add(value);
                }

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var propertyName in new[] { "id", "name", "model" })
            {
                if (item.TryGetProperty(propertyName, out var property)
                    && property.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.GetString()))
                {
                    modelIds.Add(property.GetString()!);
                    break;
                }
            }
        }
    }

    private static string MapModelDiscoveryFailure(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "Model refresh failed: the provider rejected the API key (401).",
            HttpStatusCode.Forbidden => "Model refresh failed: the provider denied access (403).",
            HttpStatusCode.NotFound => "Model refresh failed: the provider does not expose a /models endpoint (404).",
            HttpStatusCode.TooManyRequests => "Model refresh failed: the provider rate limited the request (429).",
            _ when (int)statusCode >= 500 => "Model refresh failed: the provider reported a server error.",
            _ => $"Model refresh failed: the provider returned HTTP {(int)statusCode}."
        };
    }

    private static ProviderPresetOption[] CreatePresets()
    {
        return new[]
        {
            new ProviderPresetOption(
                "Ollama Local (recommended, no key)",
                ProviderCatalog.OllamaId,
                ProviderCatalog.Ollama.DefaultBaseUrl,
                "llama3.2"),
            new ProviderPresetOption(
                "LM Studio Local (no key)",
                ProviderCatalog.LmStudioId,
                ProviderCatalog.LmStudio.DefaultBaseUrl,
                "local-model"),
            new ProviderPresetOption(
                "OpenRouter (cloud, key required, free models)",
                ProviderCatalog.OpenRouterId,
                ProviderCatalog.OpenRouter.DefaultBaseUrl,
                "meta-llama/llama-3.1-8b-instruct:free"),
            new ProviderPresetOption(
                "OpenAI API (optional paid, key required)",
                ProviderCatalog.OpenAiId,
                ProviderCatalog.OpenAi.DefaultBaseUrl,
                "gpt-4o-mini"),
            new ProviderPresetOption(
                "Custom / Advanced (edit fields manually)",
                CustomProviderIdFallback,
                string.Empty,
                string.Empty,
                IsCustom: true)
        };
    }

    /// <summary>Read-only settings snapshot for probe operations; never persists.</summary>
    private sealed class TransientSettingsStore : IAppSettingsStore
    {
        public TransientSettingsStore(AppSettings current)
        {
            Current = current ?? throw new ArgumentNullException(nameof(current));
        }

        public AppSettings Current { get; }

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Probe settings are transient and cannot be persisted.");
        }

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>In-memory credential snapshot for probe operations; never persists.</summary>
    private sealed class TransientCredentialStore : ISecureCredentialStore
    {
        private readonly string _key;
        private readonly string? _value;

        public TransientCredentialStore(string key, string? value)
        {
            _key = key;
            _value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public Task<bool> HasAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Equals(key, _key, StringComparison.Ordinal) && _value is not null);
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Equals(key, _key, StringComparison.Ordinal) ? _value : null);
        }

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Probe credentials are transient and cannot be persisted.");
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Probe credentials are transient and cannot be persisted.");
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to clear API key.")]
    private static partial void LogClearApiKeyFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to read credential status.")]
    private static partial void LogReadCredentialStatusFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Failed to save settings.")]
    private static partial void LogSaveSettingsFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Test connection failed unexpectedly.")]
    private static partial void LogTestConnectionFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Test inference failed unexpectedly.")]
    private static partial void LogTestInferenceFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Model refresh failed unexpectedly.")]
    private static partial void LogRefreshModelsFailed(ILogger logger, Exception exception);
}

/// <summary>
/// A provider preset shown in the settings UI. Presets contain no secrets and
/// perform no I/O; selecting one only fills editable defaults.
/// </summary>
public sealed record ProviderPresetOption(
    string DisplayName,
    string ProviderId,
    string BaseUrl,
    string DefaultModel,
    bool IsCustom = false)
{
    /// <summary>True when the catalog says this preset requires an API key.</summary>
    public bool RequiresApiKey => !IsCustom && ProviderCatalog.RequiresApiKey(ProviderId);

    /// <summary>True when this preset is a cloud service in the catalog.</summary>
    public bool IsCloud => !IsCustom && !ProviderCatalog.IsLocal(ProviderId);
}
