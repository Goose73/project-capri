using System.Diagnostics;
using System.IO;
using System.Text;
using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Security;

/// <summary>
/// Safe local Git preview: fixed read-only commands, no shell, no configured
/// diff drivers/textconv, no hooks or optional repository locks, trusted Git
/// executable path, limited output and time. Never runs model-produced input.
/// </summary>
public sealed class DeveloperGitInspector : IDeveloperGitInspector
{
    private const int MaxPreviewChars = 32_768;
    private readonly IDeveloperSessionStore _sessions;
    private readonly IDeveloperWorkspaceReader _reader;

    public DeveloperGitInspector(
        IDeveloperSessionStore sessions, IDeveloperWorkspaceReader reader)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<DeveloperGitStatus> GetStatusAsync(
        Guid sessionId, bool authorized, CancellationToken cancellationToken = default)
    {
        RequireApproval(authorized);
        var root = GetSessionRoot(sessionId);
        // Only tracked changes: do not show names of untracked/private files.
        var status = await RunGitAsync(root, ["status", "--porcelain=v1", "--untracked-files=no", "--", "."],
            cancellationToken).ConfigureAwait(false);
        return new DeveloperGitStatus(sessionId, status);
    }

    public async Task<DeveloperGitDiff> GetDiffAsync(
        Guid sessionId, string relativeSourceFile, bool authorized,
        CancellationToken cancellationToken = default)
    {
        RequireApproval(authorized);
        // This shared secure source reader blocks symlink/junction traversal,
        // hidden files, common secrets, unsupported types and oversized files.
        // It is a containment and eligibility check, NOT an AI upload.
        _ = _reader.ReadText(sessionId, relativeSourceFile, authorized: true);

        var root = GetSessionRoot(sessionId);
        var path = relativeSourceFile.Replace('\\', '/');
        // No output is ever written to the repository. Both staged and
        // unstaged changes are shown; .git config diff drivers cannot run.
        var staged = await RunGitAsync(root,
            ["diff", "--cached", "--no-ext-diff", "--no-textconv",
             "--ignore-submodules", "--no-color", "--", path],
            cancellationToken).ConfigureAwait(false);
        var unstaged = await RunGitAsync(root,
            ["diff", "--no-ext-diff", "--no-textconv",
             "--ignore-submodules", "--no-color", "--", path],
            cancellationToken).ConfigureAwait(false);

        return new DeveloperGitDiff(sessionId, relativeSourceFile, staged, unstaged);
    }

    private string GetSessionRoot(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("Select a developer project.", nameof(sessionId));
        var session = _sessions.Find(sessionId) ??
            throw new InvalidOperationException("Developer project was not found.");
        var directory = Path.GetFullPath(session.WorkspaceDirectory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The project folder is missing.");

        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked project directories cannot be inspected.");
        }

        return directory;
    }

    private static void RequireApproval(bool authorized)
    {
        if (!authorized)
            throw new UnauthorizedAccessException(
                "Git inspection requires explicit local project-read permission.");
    }

    private static string LocateGit()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
            throw new InvalidOperationException("Trusted Git installation directory not available.");
        var candidates = new[]
        {
            Path.Combine(programFiles, "Git", "cmd", "git.exe"),
            Path.Combine(programFiles, "Git", "bin", "git.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ??
            throw new InvalidOperationException(
                "Git for Windows was not found in a trusted installation directory.");
    }

    private static async Task<string> RunGitAsync(
        string folder, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = LocateGit(),
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true)
        };
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
        info.Environment["GIT_PAGER"] = "cat";
        info.ArgumentList.Add("--no-pager");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.fsmonitor=false");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.hooksPath=NUL");
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        var started = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            process.Start();
            started = true;
            var output = ReadBoundedAsync(process.StandardOutput, MaxPreviewChars, timeout.Token);
            var errors = ReadBoundedAsync(process.StandardError, 4096, timeout.Token);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token))
                .ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "Git could not inspect this project. Confirm it is a Git worktree.");
            return output.Result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Git inspection took too long and was stopped.");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader, int maxChars, CancellationToken ct)
    {
        var buffer = new char[4096];
        var result = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) return result.ToString();
            if (result.Length + read > maxChars)
                throw new InvalidOperationException("Git output exceeds the safe preview limit.");
            result.Append(buffer, 0, read);
        }
    }
}
