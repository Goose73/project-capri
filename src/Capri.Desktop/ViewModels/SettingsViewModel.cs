using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Providers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly IChatProvider _chatProvider;
    private readonly ILogger<SettingsViewModel> _logger;
    private string? _pendingApiKey; // never exposed as a bindable persisted property
    private bool _clearApiKeyRequested;

    [ObservableProperty]
    private string _providerId = string.Empty;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _chatModel = string.Empty;

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
    private string _apiKeyStatus = "Checking...";

    [ObservableProperty]
    private bool _hasStoredApiKey;

    [ObservableProperty]
    private string _testConnectionStatus = string.Empty;

    [ObservableProperty]
    private bool _isTestingConnection;

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
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var current = _settingsStore.Current;
        ProviderId = current.Provider.ProviderId;
        BaseUrl = current.Provider.BaseUrl;
        ChatModel = current.Provider.ChatModel;
        EnableStreaming = current.Provider.EnableStreaming;
        Temperature = current.Provider.Temperature;
        MaxOutputTokens = current.Provider.MaxOutputTokens;
        RequestTimeoutSeconds = current.Provider.RequestTimeoutSeconds;
        AutomationEnabled = current.Automation.Enabled;
        AllowAppLaunch = current.Automation.AllowAppLaunch;
        AllowWebLaunch = current.Automation.AllowWebLaunch;
        AllowMediaControl = current.Automation.AllowMediaControl;
        CharacterHeight = Math.Clamp(current.Ui.CharacterHeight, 150, 400);

        await RefreshApiKeyStatusAsync(cancellationToken).ConfigureAwait(false);
        _pendingApiKey = null;
        _clearApiKeyRequested = false;
    }

    public void SetPendingApiKey(string? value)
    {
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
        _clearApiKeyRequested = true;
        try
        {
            await _credentialStore.DeleteAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey).ConfigureAwait(false);
            await RefreshApiKeyStatusAsync().ConfigureAwait(false);
            SaveStatus = "Stored API key cleared.";
        }
        catch (Exception ex)
        {
            LogClearApiKeyFailed(_logger, ex);
            SaveStatus = "Failed to clear the stored API key.";
        }
    }

    public async Task RefreshApiKeyStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var has = await _credentialStore.HasAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, cancellationToken).ConfigureAwait(false);
            HasStoredApiKey = has;
            ApiKeyStatus = has ? "API key stored securely." : "No API key stored.";
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
                    RequestTimeoutSeconds = RequestTimeoutSeconds
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
                    CharacterHeight = Math.Clamp(CharacterHeight, 150, 400)
                }
            };

            await _settingsStore.UpdateAsync(updated).ConfigureAwait(false);

            if (_clearApiKeyRequested)
            {
                await _credentialStore.DeleteAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey).ConfigureAwait(false);
                _clearApiKeyRequested = false;
            }
            else if (!string.IsNullOrWhiteSpace(_pendingApiKey))
            {
                await _credentialStore.SetAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, _pendingApiKey).ConfigureAwait(false);
                _pendingApiKey = null;
            }

            await RefreshApiKeyStatusAsync().ConfigureAwait(false);
            SaveStatus = "Settings saved.";
        }
        catch (Exception ex)
        {
            LogSaveSettingsFailed(_logger, ex);
            SaveStatus = "Failed to save settings. Check logs for details.";
        }
    }

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
            TestConnectionStatus = "Testing connection...";

            var current = _settingsStore.Current;
            var probeSettings = current with
            {
                Provider = current.Provider with
                {
                    ProviderId = (ProviderId ?? string.Empty).Trim(),
                    BaseUrl = (BaseUrl ?? string.Empty).Trim(),
                    ChatModel = (ChatModel ?? string.Empty).Trim(),
                    RequestTimeoutSeconds = RequestTimeoutSeconds
                }
            };

            var testingPendingKey = !string.IsNullOrWhiteSpace(_pendingApiKey);
            string? probeApiKey = _pendingApiKey;
            if (!testingPendingKey)
            {
                try
                {
                    probeApiKey = await _credentialStore
                        .GetAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogReadCredentialStatusFailed(_logger, ex);
                    probeApiKey = null;
                }
            }

            // Probe with transient in-memory overrides; do not mutate persisted settings or credentials.
            using var httpClient = new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var probeProvider = new OpenAiCompatibleChatProvider(
                httpClient,
                new TransientSettingsStore(probeSettings),
                new TransientCredentialStore(
                    OpenAiCompatibleChatProvider.ApiKeyCredentialKey,
                    probeApiKey));

            var health = await probeProvider.CheckHealthAsync().ConfigureAwait(false);
            var keyNote = testingPendingKey ? " (tested with entered key, not saved)" : string.Empty;
            if (health.IsHealthy)
            {
                var latency = health.Latency.HasValue ? $" ({health.Latency.Value.TotalMilliseconds:0} ms)" : string.Empty;
                TestConnectionStatus = $"Success: {health.Message ?? "Provider is reachable."}{latency}{keyNote}";
            }
            else
            {
                TestConnectionStatus = $"Failed: {health.Message ?? "Provider is not reachable."}{keyNote}";
            }
        }
        catch (Exception ex)
        {
            LogTestConnectionFailed(_logger, ex);
            TestConnectionStatus = "Failed: could not complete the connection test.";
        }
        finally
        {
            IsTestingConnection = false;
        }
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
}
