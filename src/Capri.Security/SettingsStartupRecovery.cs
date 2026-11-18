using System.Security.Cryptography;
using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Security;

/// <summary>
/// Conservative first-run and startup recovery. Only configuration that Capri
/// originally created and can verify using its own digest manifest can be
/// restored automatically. Existing untracked/custom configuration is NEVER
/// overwritten, even if invalid or missing required fields.
/// </summary>
public static class SettingsStartupRecovery
{
    private const string ManifestName = "settings.managed.json";
    private const string SnapshotName = "settings.last-good.json";
    private const string CustomMarkerName = "settings.user-managed";
    private const int ManifestVersion = 1;
    private const int MaxSettingsBytes = 1_048_576;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public enum Status
    {
        Healthy,
        CreatedDefaults,
        RestoredSnapshot,
        ExistingCustom,
        NeedsReview
    }

    public sealed record Result(Status State, string Summary)
    {
        public bool NeedsReview => State == Status.NeedsReview;
        public bool Changed => State is Status.CreatedDefaults or Status.RestoredSnapshot;
    }

    private sealed record Manifest(int Version, string Sha256);

    /// <summary>
    /// Called once before WPF host composition. Never starts a model, accesses
    /// credentials, enables permissions, or touches an existing valid config.
    /// </summary>
    public static async Task<Result> RunAsync(
        ICapriPaths paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();

        var manifestPath = Path.Combine(paths.Root, ManifestName);
        var snapshotPath = Path.Combine(paths.Root, SnapshotName);
        var customMarkerPath = Path.Combine(paths.Root, CustomMarkerName);

        // Once a previously Capri-owned file has been edited outside Capri,
        // retire its automatic recovery authority for future boots. This
        // marker is independent of the settings and never alters user values.
        if (File.Exists(customMarkerPath))
        {
            if (!File.Exists(paths.SettingsFile))
                return new(Status.NeedsReview,
                    "User-managed settings are missing; automatic restoration is disabled.");
            try
            {
                var customBytes = await File.ReadAllBytesAsync(paths.SettingsFile, cancellationToken)
                    .ConfigureAwait(false);
                return IsValidSettingsObject(customBytes)
                    ? new(Status.ExistingCustom, "User-managed settings were preserved.")
                    : new(Status.NeedsReview,
                        "User-managed settings are damaged; no automatic replacement was made.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new(Status.NeedsReview, "User-managed settings could not be read.");
            }
        }

        if (!File.Exists(paths.SettingsFile))
        {
            if (File.Exists(manifestPath) || File.Exists(snapshotPath))
            {
                var trusted = await ReadVerifiedSnapshotAsync(manifestPath, snapshotPath, cancellationToken)
                    .ConfigureAwait(false);
                if (trusted is null)
                    return new(Status.NeedsReview,
                        "Previous Capri-managed settings are missing and the recovery snapshot cannot be verified. No files were changed.");

                await AtomicFile.WriteAllBytesAsync(paths.SettingsFile, trusted, cancellationToken)
                    .ConfigureAwait(false);
                return new(Status.RestoredSnapshot,
                    "Restored missing Capri-managed settings from the verified local snapshot.");
            }

            // Fresh install only: create a *safe*, cloud-disabled default, and
            // mark that exact byte sequence as Capri-owned.
            var defaults = JsonSerializer.SerializeToUtf8Bytes(CapriDefaultSettings.Create(), JsonOptions);
            Directory.CreateDirectory(paths.Root);
            await AtomicFile.WriteAllBytesAsync(paths.SettingsFile, defaults, cancellationToken)
                .ConfigureAwait(false);
            await WriteSnapshotAsync(paths, defaults, cancellationToken).ConfigureAwait(false);
            return new(Status.CreatedDefaults,
                "Created safe first-run settings. Choose your local model in guided setup.");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(paths.SettingsFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(Status.NeedsReview,
                "Settings could not be read. Existing files have not been changed.");
        }

        if (IsValidSettingsObject(bytes))
        {
            var managed = await IsManagedVersionAsync(manifestPath, bytes, cancellationToken)
                .ConfigureAwait(false);
            if (managed)
                return new(Status.Healthy, "Capri-managed settings passed startup validation.");

            if (File.Exists(manifestPath))
            {
                // The last known good fingerprint no longer matches the
                // valid document. Treat it as a deliberate manual override and
                // disable future automatic restore; never overwrite contents.
                await AtomicFile.WriteAllBytesAsync(customMarkerPath, "custom"u8.ToArray(),
                    cancellationToken).ConfigureAwait(false);
            }
            return new(Status.ExistingCustom,
                "Existing or manually customised settings preserved unchanged. Unknown options are not automatically repaired.");
        }

        // A zero-byte file cannot be a working user configuration. Only restore
        // if it was *already* managed and its complete last-good snapshot is
        // hash-verified. Archive the damaged bytes first, never delete them.
        if (bytes.Length == 0)
        {
            var trusted = await ReadVerifiedSnapshotAsync(manifestPath, snapshotPath, cancellationToken)
                .ConfigureAwait(false);
            if (trusted is not null)
            {
                var archive = paths.SettingsFile + ".damaged-" +
                    DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) +
                    "-" + Guid.NewGuid().ToString("N")[..8] + ".bak";
                await AtomicFile.WriteAllBytesAsync(archive, bytes, cancellationToken)
                    .ConfigureAwait(false);
                await AtomicFile.WriteAllBytesAsync(paths.SettingsFile, trusted, cancellationToken)
                    .ConfigureAwait(false);
                return new(Status.RestoredSnapshot,
                    "Restored a zero-byte Capri-managed settings file from its verified snapshot; damaged file archived.");
            }
        }

        // A manually edited, malformed nonempty file may contain intentional
        // custom configuration. Even with a last-good snapshot, do not guess.
        return new(Status.NeedsReview,
            "Settings are empty or malformed. No custom content was overwritten. Review the file or use an explicit recovery workflow.");
    }

    /// <summary>
    /// Keep the last known-good snapshot in step with a normal Capri settings
    /// save, but ONLY if the old file matches a valid Capri ownership manifest.
    /// A manual edit (changed digest) immediately ends automatic ownership.
    /// </summary>
    internal static async Task RememberManagedSaveAsync(
        ICapriPaths paths, byte[]? previousBytes, byte[] newBytes)
    {
        if (previousBytes is null) return;
        var manifestPath = Path.Combine(paths.Root, ManifestName);
        if (!await IsManagedVersionAsync(manifestPath, previousBytes, CancellationToken.None)
                .ConfigureAwait(false))
            return;

        await WriteSnapshotAsync(paths, newBytes, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task WriteSnapshotAsync(
        ICapriPaths paths, byte[] bytes, CancellationToken cancellationToken)
    {
        // Snapshot must exist and be durable BEFORE publishing the digest that
        // would trust it. Interrupted metadata updates fail closed at startup.
        await AtomicFile.WriteAllBytesAsync(Path.Combine(paths.Root, SnapshotName),
            bytes, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(
            new Manifest(ManifestVersion, Digest(bytes)), JsonOptions);
        await AtomicFile.WriteAllBytesAsync(Path.Combine(paths.Root, ManifestName),
            manifest, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsManagedVersionAsync(
        string manifestPath, byte[] bytes, CancellationToken cancellationToken)
    {
        var manifest = await TryReadManifestAsync(manifestPath, cancellationToken)
            .ConfigureAwait(false);
        return manifest is not null &&
            string.Equals(manifest.Sha256, Digest(bytes), StringComparison.Ordinal);
    }

    private static async Task<byte[]?> ReadVerifiedSnapshotAsync(
        string manifestPath, string snapshotPath, CancellationToken cancellationToken)
    {
        var manifest = await TryReadManifestAsync(manifestPath, cancellationToken)
            .ConfigureAwait(false);
        if (manifest is null || !File.Exists(snapshotPath)) return null;

        try
        {
            var file = new FileInfo(snapshotPath);
            if (file.Length is <= 0 or > MaxSettingsBytes) return null;
            var bytes = await File.ReadAllBytesAsync(snapshotPath, cancellationToken)
                .ConfigureAwait(false);
            return bytes.Length <= MaxSettingsBytes &&
                string.Equals(Digest(bytes), manifest.Sha256, StringComparison.Ordinal) &&
                IsValidSettingsObject(bytes) ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static async Task<Manifest?> TryReadManifestAsync(
        string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length is <= 0 or > 4096) return null;
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<Manifest>(bytes, JsonOptions);
            return manifest is { Version: ManifestVersion, Sha256.Length: 64 } &&
                manifest.Sha256.All(Uri.IsHexDigit) ? manifest : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool IsValidSettingsObject(byte[] bytes)
    {
        if (bytes.Length is <= 0 or > MaxSettingsBytes) return false;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            // Unknown values are preserved; recognized configuration must be
            // deserializable, but loading is never an implicit save.
            return JsonSerializer.Deserialize<AppSettings>(bytes, JsonOptions) is not null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static string Digest(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));
}
