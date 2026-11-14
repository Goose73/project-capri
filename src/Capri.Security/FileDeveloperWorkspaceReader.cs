using System.Text;
using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Security;

/// <summary>
/// Permission-gated, single-directory workspace browsing and bounded text
/// previews. A developer bookmark alone NEVER grants file access.
/// </summary>
public sealed class FileDeveloperWorkspaceReader : IDeveloperWorkspaceReader
{
    private const int MaxEntriesPerDirectory = 120;
    private const int MaxPreviewBytes = 32 * 1024;
    private static readonly HashSet<string> DeniedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".vscode", ".idea", "node_modules", "bin", "obj",
        ".next", ".cache", ".venv", "venv", "__pycache__", "packages",
        "secrets", "credentials", "private", "AppData"
    };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".fs", ".fsproj", ".vb", ".sln", ".slnx",
        ".md", ".txt", ".json", ".jsonc", ".xml", ".yaml", ".yml",
        ".toml", ".xaml", ".props", ".targets", ".resx", ".razor",
        ".ts", ".tsx", ".js", ".jsx", ".mjs", ".py", ".go", ".rs",
        ".java", ".kt", ".html", ".css", ".scss", ".sql", ".sh",
        ".ps1", ".psm1", ".cmd", ".bat", ".c", ".h", ".cpp", ".hpp"
    };

    private readonly IDeveloperSessionStore _sessions;

    public FileDeveloperWorkspaceReader(IDeveloperSessionStore sessions) =>
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));

    public IReadOnlyList<DeveloperWorkspaceItem> List(
        Guid sessionId, string? relativeDirectory, bool authorized)
    {
        RequireConsent(authorized);
        var (root, path) = Resolve(sessionId, relativeDirectory, isDirectory: true);
        var infos = new DirectoryInfo(path).EnumerateFileSystemInfos()
            .Take(MaxEntriesPerDirectory + 1).ToArray();
        if (infos.Length > MaxEntriesPerDirectory)
            throw new InvalidOperationException("Folder has too many entries to preview safely.");

        var items = new List<DeveloperWorkspaceItem>();
        foreach (var item in infos)
        {
            if (IsSensitiveName(item.Name) ||
                (item.Attributes & (FileAttributes.Hidden | FileAttributes.System |
                                    FileAttributes.ReparsePoint)) != 0)
                continue;

            var isDirectory = (item.Attributes & FileAttributes.Directory) != 0;
            if (isDirectory && DeniedDirectories.Contains(item.Name)) continue;
            if (!isDirectory && !AllowedExtensions.Contains(Path.GetExtension(item.Name)))
                continue;
            var relative = Path.GetRelativePath(root, item.FullName);
            items.Add(new DeveloperWorkspaceItem(relative, item.Name, isDirectory));
        }
        return items.OrderByDescending(x => x.IsDirectory)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string ReadText(Guid sessionId, string relativeFile, bool authorized)
    {
        RequireConsent(authorized);
        var (_, path) = Resolve(sessionId, relativeFile, isDirectory: false);
        var name = Path.GetFileName(path);
        if (IsSensitiveName(name) ||
            !AllowedExtensions.Contains(Path.GetExtension(name)))
            throw new UnauthorizedAccessException("This file type is not eligible for a source preview.");

        var info = new FileInfo(path);
        if (info.Length > MaxPreviewBytes || info.Length <= 0)
            throw new InvalidOperationException("File is empty or exceeds the 32 KB preview limit.");
        if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden |
                                FileAttributes.System)) != 0)
            throw new UnauthorizedAccessException("Linked or hidden files cannot be previewed.");

        // Bounded read guards against a file being enlarged after checking Length.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
        var buffer = new byte[MaxPreviewBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0) break;
            read += n;
        }
        if (read > MaxPreviewBytes)
            throw new InvalidOperationException("File grew beyond the 32 KB preview limit.");
        var text = new UTF8Encoding(false, true).GetString(buffer, 0, read);
        if (text.StartsWith('\uFEFF')) text = text[1..]; // strip optional UTF-8 BOM
        if (text.Contains('\0') ||
            text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t' or '\uFEFF')))
            throw new InvalidDataException("Only valid UTF-8 source text can be previewed.");
        return text;
    }

    private (string Root, string Path) Resolve(
        Guid sessionId, string? relative, bool isDirectory)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("Select a developer session.", nameof(sessionId));
        var session = _sessions.Find(sessionId) ??
            throw new InvalidOperationException("The developer session was not found.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(session.WorkspaceDirectory));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The selected project folder no longer exists.");
        RejectReparseAncestors(root);

        var clean = (relative ?? string.Empty).Trim().Replace('/', Path.DirectorySeparatorChar);
        if (clean.Length > 2048 || Path.IsPathRooted(clean) || clean.Contains(':'))
            throw new UnauthorizedAccessException("Project preview paths must be relative.");
        var segments = clean.Split(Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".." || IsSensitiveName(s) ||
                              DeniedDirectories.Contains(s)))
            throw new UnauthorizedAccessException("Project preview path is not allowed.");
        if (!isDirectory && segments.Length == 0)
            throw new ArgumentException("Select a source file.", nameof(relative));

        var candidate = segments.Aggregate(root, Path.Combine);
        var full = Path.GetFullPath(candidate);
        if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("File is outside the selected project.");

        // Reject junctions and symlinks at EVERY existing path component.
        var current = root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked project paths cannot be previewed.");
        }
        if (isDirectory && !Directory.Exists(full))
            throw new DirectoryNotFoundException("Project subfolder does not exist.");
        if (!isDirectory && !File.Exists(full))
            throw new FileNotFoundException("The selected source file is missing.");
        return (root, full);
    }

    private static bool IsSensitiveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.')) return true;
        var lower = name.ToLowerInvariant();
        return lower.Contains("secret", StringComparison.Ordinal) ||
               lower.Contains("password", StringComparison.Ordinal) ||
               lower.Contains("credential", StringComparison.Ordinal) ||
               lower.Contains("token", StringComparison.Ordinal) ||
               lower.Contains("private", StringComparison.Ordinal) ||
               lower.StartsWith("id_rsa", StringComparison.Ordinal) ||
               lower.EndsWith(".key", StringComparison.Ordinal) ||
               lower.EndsWith(".pem", StringComparison.Ordinal) ||
               lower.EndsWith(".pfx", StringComparison.Ordinal);
    }

    private static void RejectReparseAncestors(string path)
    {
        for (DirectoryInfo? current = new(path); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked project directories cannot be previewed.");
        }
    }

    private static void RequireConsent(bool authorized)
    {
        if (!authorized)
            throw new UnauthorizedAccessException(
                "Browsing a project requires explicit permission for this request.");
    }
}
