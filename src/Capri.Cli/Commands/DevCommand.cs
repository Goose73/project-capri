using System.Globalization;
using Capri.Cli.Configuration;
using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Cli.Commands;

/// <summary>
/// Metadata-only developer workspace entry point. It deliberately does not
/// read a project's files, invoke any AI provider, or dispatch commands.
/// </summary>
public sealed class DevCommand
{
    private readonly ICliConsole _console;
    private readonly IDeveloperSessionStore _sessions;

    public DevCommand(ICliConsole console, IDeveloperSessionStore sessions)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public int Execute(CliInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (invocation.Arguments.Count == 0)
            return Fail(invocation.Json, "usage", "Use 'capri dev list', 'capri dev new <folder>', or 'capri dev show <id>'.");

        try
        {
            return invocation.Arguments[0] switch
            {
                "list" => List(invocation.Json),
                "new" => Create(invocation.Arguments[1], invocation.Json),
                "show" => Show(invocation.Arguments[1], invocation.Json),
                _ => Fail(invocation.Json, "usage", "Unknown developer session command.")
            };
        }
        catch (ArgumentException)
        {
            return Fail(invocation.Json, "invalid_workspace", "Choose an existing, valid project folder.");
        }
        catch (DirectoryNotFoundException)
        {
            return Fail(invocation.Json, "missing_workspace", "Choose an existing project folder.");
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(invocation.Json, "access_denied", "Capri cannot safely access its protected developer session metadata.");
        }
        catch (InvalidDataException)
        {
            return Fail(invocation.Json, "corrupt_metadata", "Developer session metadata is invalid. It was not modified.");
        }
        catch (InvalidOperationException)
        {
            return Fail(invocation.Json, "rejected_workspace", "Project selection or private session storage was rejected.");
        }
        catch (IOException)
        {
            return Fail(invocation.Json, "storage_error", "Developer session storage is unavailable. Nothing was executed.");
        }
    }

    private int List(bool json)
    {
        var sessions = _sessions.List();
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, sessions }));
            return CliExitCodes.Success;
        }

        _console.WriteLine("Developer sessions (metadata only):");
        if (sessions.Count == 0)
            _console.WriteLine("  None yet. Use 'capri dev new <project-folder>' to register a folder.");
        foreach (var session in sessions)
            Print(session);
        return CliExitCodes.Success;
    }

    private int Create(string workspace, bool json)
    {
        var session = _sessions.Create(workspace);
        if (json)
        {
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, session }));
            return CliExitCodes.Success;
        }

        _console.WriteLine("Project session saved locally. No project contents were read or modified.");
        Print(session);
        _console.WriteLine("This is a session bookmark, not yet a coding-agent session.");
        return CliExitCodes.Success;
    }

    private int Show(string id, bool json)
    {
        if (!Guid.TryParse(id, out var sessionId) || sessionId == Guid.Empty)
            return Fail(json, "invalid_id", "Provide a valid developer session ID.");

        var session = _sessions.Find(sessionId);
        if (session is null)
            return Fail(json, "not_found", "Developer session not found.");

        if (json)
            _console.WriteLine(JsonOutput.Serialize(new { ok = true, session }));
        else
            Print(session);
        return CliExitCodes.Success;
    }

    private void Print(DeveloperSession session)
    {
        _console.WriteLine($"  {session.DisplayName}  [{session.Id:N}]");
        _console.WriteLine($"    Folder: {session.WorkspaceDirectory}");
        _console.WriteLine($"    Added: {session.CreatedUtc.ToString("u", CultureInfo.InvariantCulture)}");
    }

    private int Fail(bool json, string code, string message)
    {
        if (json)
            _console.WriteLine(JsonOutput.Serialize(new { ok = false, error = new { code, message } }));
        else
            _console.WriteErrorLine($"capri: {message}");
        return CliExitCodes.Usage;
    }
}
