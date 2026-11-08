using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Capri.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Security;

/// <summary>
/// Stores credentials as DPAPI-protected blobs in a JSON map on disk.
/// Protection uses <see cref="DataProtectionScope.CurrentUser"/> with per-key
/// entropy so an entry cannot be moved to another key or user account.
/// </summary>
/// <remarks>
/// The store fails closed: missing, corrupt, tampered, or unreadable files are
/// treated as having no credentials. Secret values are never logged or included
/// in exception messages.
/// </remarks>
public sealed partial class DpapiCredentialStore : ISecureCredentialStore, IDisposable
{
    internal const int CurrentVersion = 1;
    private const string EntropyPrefix = "Capri.v1:";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly ICapriPaths _paths;
    private readonly ILogger<DpapiCredentialStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Creates a credential store backed by <see cref="ICapriPaths.SecretsFile"/>.
    /// </summary>
    public DpapiCredentialStore(ICapriPaths paths, ILogger<DpapiCredentialStore>? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? NullLogger<DpapiCredentialStore>.Instance;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async Task<bool> HasAsync(string key, CancellationToken cancellationToken = default)
    {
        return await GetAsync(key, cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
            if (entries is null || !entries.TryGetValue(key, out var encodedValue) || string.IsNullOrWhiteSpace(encodedValue))
            {
                return null;
            }

            try
            {
                var protectedBytes = Convert.FromBase64String(encodedValue);
                var plainBytes = ProtectedData.Unprotect(
                    protectedBytes,
                    CreateEntropy(key),
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException or PlatformNotSupportedException)
            {
                LogDecryptionFailure(_logger, key);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The credential store is unreadable and cannot be updated.");

            var plainBytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectedData.Protect(
                plainBytes,
                CreateEntropy(key),
                DataProtectionScope.CurrentUser);

            entries[key] = Convert.ToBase64String(protectedBytes);
            await WriteEntriesAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
            if (entries is null)
            {
                LogDeleteSkippedUnreadable(_logger, key);
                return;
            }

            if (!entries.Remove(key))
            {
                return;
            }

            await WriteEntriesAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, string>?> ReadEntriesAsync(CancellationToken cancellationToken)
    {
        var secretsFile = _paths.SecretsFile;
        if (!File.Exists(secretsFile))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(secretsFile, cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                LogStoreEmpty(_logger);
                return null;
            }

            var document = JsonSerializer.Deserialize<SecretsDocument>(bytes, SerializerOptions);
            if (document is null || document.Version is <= 0 or > CurrentVersion)
            {
                LogUnsupportedFormat(_logger);
                return null;
            }

            return document.Entries ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LogStoreUnreadable(_logger);
            return null;
        }
    }

    private async Task WriteEntriesAsync(Dictionary<string, string> entries, CancellationToken cancellationToken)
    {
        var document = new SecretsDocument
        {
            Version = CurrentVersion,
            Entries = entries,
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        await AtomicFile.WriteAllBytesAsync(_paths.SecretsFile, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] CreateEntropy(string key)
    {
        return Encoding.UTF8.GetBytes(EntropyPrefix + key);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Credential {CredentialKey} could not be decrypted and is treated as absent.")]
    private static partial void LogDecryptionFailure(ILogger logger, string credentialKey);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Credential {CredentialKey} was not deleted because the credential store is unreadable.")]
    private static partial void LogDeleteSkippedUnreadable(ILogger logger, string credentialKey);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Credential store is empty and is treated as having no credentials.")]
    private static partial void LogStoreEmpty(ILogger logger);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Credential store has an unsupported format and is treated as having no credentials.")]
    private static partial void LogUnsupportedFormat(ILogger logger);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Credential store is unreadable and is treated as having no credentials.")]
    private static partial void LogStoreUnreadable(ILogger logger);

    internal sealed class SecretsDocument
    {
        public int Version { get; set; } = CurrentVersion;

        public Dictionary<string, string> Entries { get; set; } = new(StringComparer.Ordinal);
    }
}
