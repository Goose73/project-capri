using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Security.Autonomy;

namespace Capri.Security;

/// <summary>
/// Cross-client developer chat history: encrypted per-message records, bounded
/// and atomically created. No transcript is stored as plaintext JSON.
/// </summary>
public sealed class FileDeveloperConversationStore : IDeveloperConversationStore
{
    private const int MaxEntries = 160;
    private const int MaxMessageCharacters = 8000;
    private const int MaxEncryptedFileBytes = 65536;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ICapriPaths _paths;
    private readonly IDeveloperSessionStore _sessions;

    public FileDeveloperConversationStore(ICapriPaths paths, IDeveloperSessionStore sessions)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    private string Root => Path.Combine(_paths.Root, "developer", "conversations");
    private string SessionDirectory(Guid id) => Path.Combine(Root, id.ToString("N"));

    public IReadOnlyList<DeveloperConversationEntry> Read(Guid sessionId, int maxCount = 40)
    {
        RequireSession(sessionId);
        maxCount = Math.Clamp(maxCount, 1, MaxEntries);
        var directory = SessionDirectory(sessionId);
        if (!Directory.Exists(directory)) return [];
        TaskDirectorySecurity.EnsurePrivateDirectory(directory);

        var all = Directory.EnumerateFiles(directory, "*.bin")
            .Take(MaxEntries + 1).ToArray();
        if (all.Length > MaxEntries)
            throw new InvalidDataException("Developer conversation is over the storage limit.");

        return all.OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .TakeLast(maxCount)
            .Select(file => ReadRecord(file, sessionId))
            .ToArray();
    }

    public DeveloperConversationEntry Append(Guid sessionId, ChatRole role, string content)
    {
        RequireSession(sessionId);
        if (role is not (ChatRole.User or ChatRole.Assistant))
            throw new ArgumentException("Only developer chat turns can be saved.", nameof(role));
        if (string.IsNullOrWhiteSpace(content) || content.Length > MaxMessageCharacters)
            throw new ArgumentException("A nonempty message of at most 8000 characters is required.", nameof(content));

        var directory = SessionDirectory(sessionId);
        TaskDirectorySecurity.EnsurePrivateDirectory(_paths.Root);
        TaskDirectorySecurity.EnsurePrivateDirectory(Path.Combine(_paths.Root, "developer"));
        TaskDirectorySecurity.EnsurePrivateDirectory(Root);
        TaskDirectorySecurity.EnsurePrivateDirectory(directory);

        if (Directory.EnumerateFiles(directory, "*.bin").Take(MaxEntries).Count() >= MaxEntries)
            throw new InvalidOperationException("This conversation is full. Export/archive is required before more messages.");

        var entry = new DeveloperConversationEntry(
            Guid.NewGuid(), sessionId, role, content, DateTimeOffset.UtcNow);
        var json = JsonSerializer.SerializeToUtf8Bytes(entry, Json);
        var protectedBytes = ProtectedData.Protect(
            json, Entropy(sessionId), DataProtectionScope.CurrentUser);
        if (protectedBytes.Length > MaxEncryptedFileBytes)
            throw new InvalidOperationException("Protected conversation entry exceeded the safe size limit.");

        var filename = entry.CreatedUtc.UtcTicks.ToString("D19",
            System.Globalization.CultureInfo.InvariantCulture) + "_" +
            entry.Id.ToString("N") + ".bin";
        var file = Path.Combine(directory, filename);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
            {
                stream.Write(protectedBytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, file); // never overwrite an existing message
            return entry;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static DeveloperConversationEntry ReadRecord(string file, Guid sessionId)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var components = name.Split('_');
        if (components.Length != 2 ||
            components[0].Length != 19 ||
            !long.TryParse(components[0], out _) ||
            !Guid.TryParseExact(components[1], "N", out var entryId))
            throw new InvalidDataException("Developer conversation filename is invalid.");

        var info = new FileInfo(file);
        if (info.Length <= 0 || info.Length > MaxEncryptedFileBytes ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Protected developer conversation file failed validation.");

        try
        {
            var bytes = File.ReadAllBytes(file);
            var plaintext = ProtectedData.Unprotect(
                bytes, Entropy(sessionId), DataProtectionScope.CurrentUser);
            var entry = JsonSerializer.Deserialize<DeveloperConversationEntry>(plaintext, Json);
            if (entry is null ||
                entry.Id != entryId || entry.SessionId != sessionId ||
                entry.Role is not (ChatRole.User or ChatRole.Assistant) ||
                string.IsNullOrWhiteSpace(entry.Content) ||
                entry.Content.Length > MaxMessageCharacters ||
                entry.CreatedUtc == default)
                throw new InvalidDataException("Developer conversation content failed validation.");
            return entry;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException)
        {
            throw new InvalidDataException("Protected developer conversation could not be read.", ex);
        }
    }

    private void RequireSession(Guid sessionId)
    {
        if (sessionId == Guid.Empty || _sessions.Find(sessionId) is null)
            throw new InvalidOperationException("Developer session does not exist.");
    }

    private static byte[] Entropy(Guid id) =>
        Encoding.UTF8.GetBytes("Capri.DeveloperChat.v1:" + id.ToString("N"));
}
