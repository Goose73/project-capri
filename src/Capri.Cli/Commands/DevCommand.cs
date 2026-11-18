using System.Globalization;
using System.Text;
using Capri.Cli.Configuration;
using Capri.Core.Abstractions;
using Capri.Core.Conversation;
using Capri.Core.Models;

namespace Capri.Cli.Commands;

/// <summary>
/// Shared developer sessions, optional explicitly consented read-only previews,
/// and genuine persistent conversation using Capri's configured model.
/// Never executes code or edits a workspace.
/// </summary>
public sealed class DevCommand
{
    private readonly ICliConsole _console;
    private readonly IDeveloperSessionStore _sessions;
    private readonly IDeveloperConversationStore? _history;
    private readonly IDeveloperWorkspaceReader? _reader;
    private readonly DeveloperConversationService? _chat;
    private readonly IDeveloperGitInspector? _git;

    public DevCommand(
        ICliConsole console,
        IDeveloperSessionStore sessions,
        IDeveloperConversationStore? history = null,
        IDeveloperWorkspaceReader? reader = null,
        DeveloperConversationService? chat = null,
        IDeveloperGitInspector? git = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _history = history;
        _reader = reader;
        _chat = chat;
        _git = git;
    }

    public async Task<int> ExecuteAsync(CliInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        if (invocation.Arguments.Count == 0)
            return Execute(invocation);
        if (invocation.Arguments[0] is "git-status" or "git-diff")
            return await GitPreviewAsync(invocation, cancellationToken).ConfigureAwait(false);
        if (invocation.Arguments[0] != "ask")
            return Execute(invocation);
        cancellationToken.ThrowIfCancellationRequested();
        if (_chat is null)
            return Fail(invocation.Json, "provider_unavailable",
                "Configure a Capri AI provider and model before developer chat.", CliExitCodes.Error);
        try
        {
            if (!TryId(invocation.Arguments[1], out var sessionId))
                return Fail(invocation.Json, "invalid_id", "Enter a valid developer session ID.");

            DeveloperSourceContext? approvedSource = null;
            if (invocation.ContextFile is { } relativeFile)
            {
                // Three explicit switches for one file. A saved session never
                // grants implicit project reading or provider transmission.
                if (!invocation.AllowRead || !invocation.SendContext || _reader is null)
                    return Fail(invocation.Json, "context_consent_required",
                        "Source context requires --context <file> --allow-read --send-context.");

                var sourceText = _reader.ReadText(sessionId, relativeFile, authorized: true);
                if (sourceText.Length > 8000)
                    return Fail(invocation.Json, "context_too_large",
                        "Only source files up to 8000 characters can be sent to AI.");
                approvedSource = new DeveloperSourceContext(sessionId, relativeFile, sourceText);
            }

            var response = await _chat.SendWithSourceAsync(
                sessionId, invocation.Arguments[2], approvedSource,
                cancellationToken).ConfigureAwait(false);
            if (invocation.Json)
                _console.WriteLine(JsonOutput.Serialize(new { ok = true, response }));
            else
                _console.WriteLine($"capri> {response.Content}");
            return CliExitCodes.Success;
        }
        catch (OperationCanceledException) { throw; }
        catch (ArgumentException)
        {
            return Fail(invocation.Json, "invalid_message", "Enter a nonempty question of at most 8000 characters.");
        }
        catch (Exception)
        {
            // Provider implementations may throw arbitrary transport errors.
            // Never expose raw exception strings (potentially sensitive) or
            // allow them to escape as a CLI crash.
            return Fail(invocation.Json, "chat_failed",
                "Developer conversation failed. Check saved sessions, model settings, provider consent and local storage.",
                CliExitCodes.Error);
        }
    }

    private async Task<int> GitPreviewAsync(CliInvocation invocation, CancellationToken ct)
    {
        if (_git is null)
            return Fail(invocation.Json, "unavailable", "Read-only Git inspector is not available.");
        if (!invocation.AllowRead)
            return Fail(invocation.Json, "consent_required", "Git previews need --allow-read.");
        if (!TryId(invocation.Arguments[1], out var id))
            return Fail(invocation.Json, "invalid_id", "Enter a valid developer session ID.");

        try
        {
            if (invocation.Arguments[0] == "git-status")
            {
                var result = await _git.GetStatusAsync(id, authorized: true, ct).ConfigureAwait(false);
                if (invocation.Json)
                    _console.WriteLine(JsonOutput.Serialize(new { ok = true, result }));
                else
                    _console.WriteLine(result.Output.Length == 0
                        ? "No tracked Git changes." : result.Output);
            }
            else
            {
                var result = await _git.GetDiffAsync(id, invocation.Arguments[2], true, ct)
                    .ConfigureAwait(false);
                if (invocation.Json)
                    _console.WriteLine(JsonOutput.Serialize(new { ok = true, result }));
                else
                {
                    _console.WriteLine("Staged changes:");
                    _console.WriteLine(result.StagedDiff.Length == 0 ? "  None" : result.StagedDiff);
                    _console.WriteLine("Unstaged changes:");
                    _console.WriteLine(result.UnstagedDiff.Length == 0 ? "  None" : result.UnstagedDiff);
                }
            }
            return CliExitCodes.Success;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return Fail(invocation.Json, "git_preview_failed",
                "Read-only Git preview unavailable. Verify the project, Git installation, and source permissions.");
        }
    }

    public int Execute(CliInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (invocation.Arguments.Count == 0)
            return Fail(invocation.Json, "usage", "Use 'capri help dev' for commands.");

        try
        {
            return invocation.Arguments[0] switch
            {
                "list" => List(invocation.Json),
                "new" => Create(invocation.Arguments[1], invocation.Json),
                "show" => Show(invocation.Arguments[1], invocation.Json),
                "history" => History(invocation.Arguments[1], invocation.Json),
                "files" => Files(invocation),
                "file" => FilePreview(invocation),
                _ => Fail(invocation.Json, "usage", "Use 'capri help dev' for supported commands.")
            };
        }
        catch (DecoderFallbackException)
        {
            return Fail(invocation.Json, "invalid_text", "Only valid UTF-8 text files can be previewed.");
        }
        catch (ArgumentException)
        {
            return Fail(invocation.Json, "invalid_path", "Choose an existing valid project folder or relative source path.");
        }
        catch (DirectoryNotFoundException)
        {
            return Fail(invocation.Json, "missing_workspace", "The selected project folder or subfolder is missing.");
        }
        catch (FileNotFoundException)
        {
            return Fail(invocation.Json, "missing_file", "The source file was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(invocation.Json, "access_denied",
                "Permission denied or the selected path was blocked by the read-only policy.");
        }
        catch (InvalidDataException)
        {
            return Fail(invocation.Json, "invalid_data", "Protected metadata or source preview failed validation.");
        }
        catch (InvalidOperationException)
        {
            return Fail(invocation.Json, "operation_rejected",
                "The requested session, safe preview or private storage is unavailable.");
        }
        catch (IOException)
        {
            return Fail(invocation.Json, "storage_error", "Local storage is unavailable; no project edits occurred.");
        }
    }

    private int List(bool json)
    {
        var sessions = _sessions.List();
        if (json)
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, sessions }));
        else
        {
            _console.WriteLine("Developer sessions:");
            if (sessions.Count == 0)
                _console.WriteLine("  None yet. Use 'capri dev new <project-folder>'.");
            foreach (var session in sessions) Print(session);
        }
        return CliExitCodes.Success;
    }

    private int Create(string workspace, bool json)
    {
        var session = _sessions.Create(workspace);
        if (json)
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, session }));
        else
        {
            _console.WriteLine("Project bookmark saved locally. Its source files were not read.");
            Print(session);
        }
        return CliExitCodes.Success;
    }

    private int Show(string id, bool json)
    {
        if (!TryId(id, out var sessionId))
            return Fail(json, "invalid_id", "Provide a valid developer session ID.");
        var session = _sessions.Find(sessionId);
        if (session is null) return Fail(json, "not_found", "Developer session not found.");
        if (json)
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, session }));
        else Print(session);
        return CliExitCodes.Success;
    }

    private int History(string id, bool json)
    {
        if (_history is null)
            return Fail(json, "unavailable", "Developer chat history is unavailable.", CliExitCodes.Error);
        if (!TryId(id, out var sessionId))
            return Fail(json, "invalid_id", "Provide a valid developer session ID.");
        var entries = _history.Read(sessionId);
        if (json) _console.WriteLine(JsonOutput.Serialize(new { ok = true, entries }));
        else
        {
            if (entries.Count == 0) _console.WriteLine("No developer messages yet.");
            foreach (var entry in entries)
                _console.WriteLine($"{(entry.Role == ChatRole.User ? "you" : "capri")}> {entry.Content}");
        }
        return CliExitCodes.Success;
    }

    private int Files(CliInvocation invocation)
    {
        if (_reader is null)
            return Fail(invocation.Json, "unavailable", "Project reader is not available.", CliExitCodes.Error);
        if (!TryId(invocation.Arguments[1], out var id))
            return Fail(invocation.Json, "invalid_id", "Provide a valid developer session ID.");
        var relative = invocation.Arguments.Count > 2 ? invocation.Arguments[2] : string.Empty;
        if (!ConsentToRead(invocation, id,
            $"list names in '{(relative.Length == 0 ? "." : relative)}'"))
            return Fail(invocation.Json, "consent_required",
                "Reading project names requires explicit permission. Use --allow-read or approve interactively.");

        var items = _reader.List(id, relative, authorized: true);
        if (invocation.Json) _console.WriteLine(JsonOutput.Serialize(new { ok = true, items }));
        else
        {
            if (items.Count == 0) _console.WriteLine("No eligible source entries in this directory.");
            foreach (var item in items)
                _console.WriteLine($"{(item.IsDirectory ? "[dir]" : "[file]")} {item.RelativePath}");
        }
        return CliExitCodes.Success;
    }

    private int FilePreview(CliInvocation invocation)
    {
        if (_reader is null)
            return Fail(invocation.Json, "unavailable", "Project reader is not available.", CliExitCodes.Error);
        if (!TryId(invocation.Arguments[1], out var id))
            return Fail(invocation.Json, "invalid_id", "Provide a valid developer session ID.");
        var relative = invocation.Arguments[2];
        if (!ConsentToRead(invocation, id, $"read up to 32 KB of '{relative}'"))
            return Fail(invocation.Json, "consent_required",
                "Reading project files requires explicit permission. Use --allow-read or approve interactively.");

        var content = _reader.ReadText(id, relative, authorized: true);
        if (invocation.Json)
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, relativePath = relative, content }));
        else _console.WriteLine(content);
        return CliExitCodes.Success;
    }

    private bool ConsentToRead(CliInvocation invocation, Guid sessionId, string operation)
    {
        if (invocation.AllowRead) return true;
        if (!_console.IsInteractive) return false;
        var session = _sessions.Find(sessionId);
        if (session is null) return false;
        var prompt = $"Allow a read-only request to {operation} under '{session.WorkspaceDirectory}'? [y/N]";
        if (invocation.Json) _console.WriteErrorLine(prompt);
        else _console.Write($"{prompt} ");
        var answer = _console.ReadLine()?.Trim();
        return string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryId(string raw, out Guid id) =>
        Guid.TryParse(raw, out id) && id != Guid.Empty;

    private void Print(DeveloperSession session)
    {
        _console.WriteLine($"  {session.DisplayName}  [{session.Id:N}]");
        _console.WriteLine($"    Folder: {session.WorkspaceDirectory}");
        _console.WriteLine($"    Added: {session.CreatedUtc.ToString("u", CultureInfo.InvariantCulture)}");
    }

    private int Fail(bool json, string code, string message, int exitCode = CliExitCodes.Usage)
    {
        if (json)
            _console.WriteLine(JsonOutput.Serialize(new { ok = false, error = new { code, message } }));
        else _console.WriteErrorLine($"capri: {message}");
        return exitCode;
    }
}
