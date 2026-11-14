using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Security.Autonomy;

namespace Capri.Security;

/// <summary>
/// Durable, private, metadata-only developer sessions. Each session has an immutable
/// UUID-named file; no source file content, prompts, secrets or command output is stored.
/// The Windows directory permission policy is shared with Capri's task journal.
/// </summary>
public sealed class FileDeveloperSessionStore : IDeveloperSessionStore
{
    private const int MaxSessionCount = 100;
    private const int MaxDocumentBytes = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ICapriPaths _paths;

    public FileDeveloperSessionStore(ICapriPaths paths) =>
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private string DirectoryPath => Path.Combine(_paths.Root, "developer", "sessions");

    public DeveloperSession Create(string workspaceDirectory, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceDirectory.Trim()));
        if (path.Length > 2048)
            throw new ArgumentException("Selected project path is too long.", nameof(workspaceDirectory));
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException("Choose an existing project folder.");

        // Explicit folder selection authorizes checking that folder's existence only.
        // Do not recursively inspect a project or follow symlinks/junctions.
        for (DirectoryInfo? current = new(path); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Cannot select a linked or redirected project folder.");
        }

        var name = SanitizeName(displayName, path);
        EnsureStorage();
        if (Directory.EnumerateFiles(DirectoryPath, "*.json").Take(MaxSessionCount).Count() >= MaxSessionCount)
            throw new InvalidOperationException("The developer session limit has been reached.");

        var session = new DeveloperSession(Guid.NewGuid(), name, path, DateTimeOffset.UtcNow);
        var content = JsonSerializer.SerializeToUtf8Bytes(session, Json);
        if (content.Length > MaxDocumentBytes)
            throw new InvalidOperationException("Session metadata is too large.");

        var target = GetPath(session.Id);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, target); // no overwrites, no partial JSON visible
            return session;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public IReadOnlyList<DeveloperSession> List(int maxCount = 30)
    {
        maxCount = Math.Clamp(maxCount, 1, MaxSessionCount);
        if (!Directory.Exists(DirectoryPath)) return [];
        TaskDirectorySecurity.EnsurePrivateDirectory(DirectoryPath);
        var files = Directory.EnumerateFiles(DirectoryPath, "*.json")
            .Take(MaxSessionCount + 1).ToArray();
        if (files.Length > MaxSessionCount)
            throw new InvalidDataException("Too many developer session metadata files.");

        var records = new List<DeveloperSession>(files.Length);
        foreach (var file in files)
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id))
                throw new InvalidDataException("Unexpected developer session metadata filename.");
            records.Add(ReadDocument(file, id));
        }
        return records.OrderByDescending(x => x.CreatedUtc)
            .ThenBy(x => x.Id).Take(maxCount).ToArray();
    }

    public DeveloperSession? Find(Guid sessionId)
    {
        if (sessionId == Guid.Empty) return null;
        if (!Directory.Exists(DirectoryPath)) return null;
        TaskDirectorySecurity.EnsurePrivateDirectory(DirectoryPath);
        var path = GetPath(sessionId);
        return File.Exists(path) ? ReadDocument(path, sessionId) : null;
    }

    private void EnsureStorage()
    {
        TaskDirectorySecurity.EnsurePrivateDirectory(_paths.Root);
        TaskDirectorySecurity.EnsurePrivateDirectory(Path.Combine(_paths.Root, "developer"));
        TaskDirectorySecurity.EnsurePrivateDirectory(DirectoryPath);
    }

    private string GetPath(Guid id) => Path.Combine(DirectoryPath, id.ToString("N") + ".json");

    private static DeveloperSession ReadDocument(string path, Guid expectedId)
    {
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > MaxDocumentBytes ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Developer session metadata failed validation.");

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var record = JsonSerializer.Deserialize<DeveloperSession>(stream, Json);
            if (record is null || record.Id != expectedId ||
                string.IsNullOrWhiteSpace(record.DisplayName) ||
                record.DisplayName.Length > 80 ||
                record.DisplayName.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(record.WorkspaceDirectory) ||
                record.WorkspaceDirectory.Length > 2048 ||
                !Path.IsPathFullyQualified(record.WorkspaceDirectory))
                throw new InvalidDataException("Developer session metadata failed validation.");
            return record;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Developer session metadata contains invalid JSON.", ex);
        }
    }

    private static string SanitizeName(string? proposed, string path)
    {
        var name = string.IsNullOrWhiteSpace(proposed)
            ? Path.GetFileName(path)
            : proposed;
        name = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).Take(80).ToArray()).Trim();
        return name.Length == 0 ? "Project" : name;
    }
}
