using System.Security.Cryptography;
using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Security;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON with atomic replacement.
/// Settings never contain secrets; credentials belong in
/// <see cref="ISecureCredentialStore"/>.
/// </summary>
public sealed partial class JsonAppSettingsStore : IAppSettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly ICapriPaths _paths;
    private readonly ILogger<JsonAppSettingsStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _current;
    // Tracks the exact last loaded/written bytes. External edits, deleted
    // files, or a corrupt file cannot be silently overwritten by stale UI.
    private string? _sourceDigest;

    /// <summary>
    /// Creates a store backed by <see cref="ICapriPaths.SettingsFile"/>. A
    /// missing or unreadable file falls back to defaults.
    /// </summary>
    public JsonAppSettingsStore(ICapriPaths paths, ILogger<JsonAppSettingsStore>? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? NullLogger<JsonAppSettingsStore>.Instance;
        _current = LoadInitial();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public AppSettings Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public event EventHandler<AppSettingsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public async Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = Normalize(settings);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, SerializerOptions);

        AppSettings previous;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[]? originalBytes = null;
            if (File.Exists(_paths.SettingsFile))
                originalBytes = await File.ReadAllBytesAsync(_paths.SettingsFile, cancellationToken)
                    .ConfigureAwait(false);

            var actualDigest = originalBytes is null ? null : Digest(originalBytes);
            if (!string.Equals(actualDigest, _sourceDigest, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Settings were edited or damaged outside Capri. Reload and review the file before saving.");
            }

            await AtomicFile
                .WriteAllBytesAsync(_paths.SettingsFile, bytes, cancellationToken)
                .ConfigureAwait(false);

            _sourceDigest = Digest(bytes);
            previous = Interlocked.Exchange(ref _current, normalized);

            // Recovery metadata is best effort. If it fails, the config is
            // still valid; the stale manifest simply cannot authorize an
            // automatic restore next time.
            try
            {
                await SettingsStartupRecovery.RememberManagedSaveAsync(
                    _paths, originalBytes, bytes).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                LogRecoverySnapshotUnavailable(_logger);
            }
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged(previous, normalized);
    }

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        AppSettings? loaded;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            loaded = await TryLoadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (loaded is null)
        {
            // Unreadable or malformed settings are ignored; the last known good
            // snapshot remains active rather than partially applying bad data.
            return;
        }

        var previous = Interlocked.Exchange(ref _current, loaded);
        if (!EqualityComparer<AppSettings>.Default.Equals(previous, loaded))
        {
            RaiseChanged(previous, loaded);
        }
    }

    private AppSettings LoadInitial()
    {
        try
        {
            var settingsFile = _paths.SettingsFile;
            if (!File.Exists(settingsFile))
            {
                return CapriDefaultSettings.Create();
            }

            var bytes = File.ReadAllBytes(settingsFile);
            if (bytes.Length == 0)
            {
                LogSettingsEmptyUsingDefaults(_logger);
                return CapriDefaultSettings.Create();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
            if (settings is null) return CapriDefaultSettings.Create();
            _sourceDigest = Digest(bytes);
            return Normalize(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LogSettingsUnreadableUsingDefaults(_logger);
            return CapriDefaultSettings.Create();
        }
    }

    private async Task<AppSettings?> TryLoadAsync(CancellationToken cancellationToken)
    {
        var settingsFile = _paths.SettingsFile;
        if (!File.Exists(settingsFile))
        {
            return CapriDefaultSettings.Create();
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(settingsFile, cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                LogSettingsEmptyKeepingCurrent(_logger);
                return null;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
            if (settings is null) return null;
            _sourceDigest = Digest(bytes);
            return Normalize(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LogSettingsUnreadableKeepingCurrent(_logger);
            return null;
        }
    }

    private void RaiseChanged(AppSettings previous, AppSettings current)
    {
        Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, current));
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Settings file is empty; defaults will be used.")]
    private static partial void LogSettingsEmptyUsingDefaults(ILogger logger);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Settings file is unreadable; defaults will be used.")]
    private static partial void LogSettingsUnreadableUsingDefaults(ILogger logger);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Settings file is empty; keeping the current settings snapshot.")]
    private static partial void LogSettingsEmptyKeepingCurrent(ILogger logger);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Settings file is unreadable; keeping the current settings snapshot.")]
    private static partial void LogSettingsUnreadableKeepingCurrent(ILogger logger);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "The managed settings recovery snapshot could not be updated; existing settings remain saved.")]
    private static partial void LogRecoverySnapshotUnavailable(ILogger logger);

    private static string Digest(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private static AppSettings Normalize(AppSettings settings)
    {
        var provider = settings.Provider ?? new AppSettings.ProviderSettings();
        var migratingToCurrentSchema = settings.SchemaVersion < CapriDefaultSettings.CurrentSchemaVersion;

        var providerId = NormalizeProviderId(provider.ProviderId, provider.BaseUrl);
        var baseUrl = string.IsNullOrWhiteSpace(provider.BaseUrl)
            ? CapriDefaultSettings.GetDefaultBaseUrl(providerId)
            : provider.BaseUrl.Trim();

        var normalizedProvider = provider with
        {
            ProviderId = providerId,
            BaseUrl = baseUrl,
            ChatModel = string.IsNullOrWhiteSpace(provider.ChatModel)
                ? CapriDefaultSettings.DefaultModelId
                : provider.ChatModel.Trim(),
            Temperature = double.IsFinite(provider.Temperature)
                ? provider.Temperature
                : CapriDefaultSettings.DefaultTemperature,
            MaxOutputTokens = provider.MaxOutputTokens > 0
                ? provider.MaxOutputTokens
                : CapriDefaultSettings.DefaultMaxOutputTokens,
            RequestTimeoutSeconds = provider.RequestTimeoutSeconds > 0
                ? provider.RequestTimeoutSeconds
                : CapriDefaultSettings.DefaultRequestTimeoutSeconds,
            // Version 2 additions. Migration establishes the safe defaults;
            // existing version 2 values are preserved unchanged.
            FreeModelsOnly = migratingToCurrentSchema || provider.FreeModelsOnly,
            AllowCloudInference = !migratingToCurrentSchema && provider.AllowCloudInference,
            CloudConsentRecordedUtc = migratingToCurrentSchema
                ? null
                : provider.CloudConsentRecordedUtc,
        };

        return settings with
        {
            SchemaVersion = migratingToCurrentSchema
                ? CapriDefaultSettings.CurrentSchemaVersion
                : settings.SchemaVersion,
            Provider = normalizedProvider,
        };
    }

    /// <summary>
    /// Resolves the provider identifier without discarding a custom base URL.
    /// Known identifiers are canonicalized; unknown identifiers fall back to
    /// the legacy default. A blank identifier is inferred from a catalog base
    /// URL when possible, otherwise it also falls back to the legacy default.
    /// </summary>
    private static string NormalizeProviderId(string? providerId, string? baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            return ProviderCatalog.TryGetById(providerId.Trim(), out var knownProfile)
                ? knownProfile.Id
                : providerId.Trim(); // Unknown/custom provider identifiers must survive saves.
        }

        return ProviderCatalog.TryGetByBaseUrl(baseUrl, out var inferredProfile)
            ? inferredProfile.Id
            : CapriDefaultSettings.DefaultProviderId;
    }
}
