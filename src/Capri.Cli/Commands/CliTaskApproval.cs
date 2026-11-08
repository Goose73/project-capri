using System.Globalization;
using System.Text;
using Capri.Cli.Services;
using Capri.Core.Autonomy;

namespace Capri.Cli.Commands;

/// <summary>
/// Terminal approval UI. Never accepts piped/model/implicit approval. Default reject.
/// Returns selected existing disposable workspace for note plans; null means denied.
/// </summary>
public sealed class CliTaskApproval : ITaskApproval
{
    private readonly ICliConsole _console;
    private readonly TimeSpan _approvalTimeout;
    private TaskApprovalContext _context = new("(request unavailable)", null, null);

    public CliTaskApproval(ICliConsole console, TimeSpan? approvalTimeout = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _approvalTimeout = approvalTimeout ?? TimeSpan.FromMinutes(5);
    }

    public void SetContext(TaskApprovalContext context) => _context = context ?? _context;

    public Task<string?> ApproveAsync(TaskPlan plan, CancellationToken cancellationToken)
        => ApproveCoreAsync(plan, _context, cancellationToken);

    private async Task<string?> ApproveCoreAsync(TaskPlan plan, TaskApprovalContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        // Non-interactive execution is refused; never accept piped stdin/flags/env/model text as blanket approval.
        if (!_console.IsInteractive)
        {
            _console.WriteErrorLine("capri: task approval requires an interactive terminal. No actions were dispatched.");
            return null;
        }

        string? workspace = null;
        if (plan.Steps.Any(s => s.Tool is TaskTool.CreateNote or TaskTool.OpenNote))
        {
            workspace = await SelectWorkspaceAsync(cancellationToken).ConfigureAwait(false);
            if (workspace is null)
            {
                _console.WriteLine("Denied: no approved workspace selected. No actions were dispatched.");
                return null;
            }
        }

        Render(plan, context, workspace);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_approvalTimeout);
        try
        {
            var decision = await ReadDecisionAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!decision)
            {
                _console.WriteLine("Denied. No actions were dispatched.");
                return null;
            }

            return workspace ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            _console.WriteErrorLine("capri: approval timed out or was cancelled. No actions were dispatched.");
            return null;
        }
    }

    private void Render(TaskPlan plan, TaskApprovalContext context, string? workspace)
    {
        _console.WriteLine("");
        _console.WriteLine("=== Capri Task Approval (exact plan) ===");
        _console.WriteLine($"Request: {SafeSingleLine(context.Goal, 400)}");
        _console.WriteLine($"Provider/model: {SafeSingleLine(context.ProviderId ?? "(unknown)", 80)}/{SafeSingleLine(context.ModelId ?? "(unknown)", 120)}");
        _console.WriteLine($"Workspace (notes only): {(workspace ?? "(none required)")}");
        _console.WriteLine("");
        _console.WriteLine("Planned actions (in order):");
        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            var modifies = step.Tool is TaskTool.CreateNote ? "modifies computer (creates new file; never overwrites)" : "read-only dispatch/UI launch";
            var target = DescribeTarget(step, workspace);
            _console.WriteLine($"  {i + 1}. {step.Tool} [{modifies}]");
            _console.WriteLine($"     Target: {target}");
            if (step.Tool == TaskTool.CreateNote)
            {
                _console.WriteLine($"     Exact note filename: {step.Target}");
                _console.WriteLine($"     Exact destination: {Path.Combine(workspace!, step.Target)}");
                _console.WriteLine("     Complete proposed note text:");
                _console.WriteLine(IndentBlock(step.Text ?? string.Empty));
            }
        }

        _console.WriteLine("");
        _console.WriteLine("Limitations / verification uncertainty:");
        _console.WriteLine("  - Browser/app windows and page contents are not fully verified; some steps are dispatch-only.");
        _console.WriteLine("  - Brave search requires Brave installed in a supported location.");
        _console.WriteLine("  - Note file contents are verified on disk after create; editor contents/UI typing are not claimed.");
        _console.WriteLine("  - Emergency stop cancels queued work; an already-dispatched external action may have occurred.");
        _console.WriteLine("");
        _console.WriteLine("To approve, type exactly: approve PLAN");
        _console.Write("Decision (default reject): ");
    }

    private static string DescribeTarget(TaskStep step, string? workspace)
    {
        return step.Tool switch
        {
            TaskTool.LaunchApp => $"Allowlisted app: {step.Target}",
            TaskTool.OpenUrl => $"Exact HTTPS URL: {step.Target}",
            TaskTool.BraveSearch => $"Brave search query: {step.Target}",
            TaskTool.CreateNote => $"New note file: {Path.Combine(workspace ?? "(no workspace)", step.Target)}",
            TaskTool.OpenNote => $"Note file to open in Notepad: {Path.Combine(workspace ?? "(no workspace)", step.Target)}",
            _ => step.Target,
        };
    }

    private async Task<string?> SelectWorkspaceAsync(CancellationToken cancellationToken)
    {
        var tempCandidate = Path.Combine(Path.GetTempPath(), "CapriTaskNotes");
        try { Directory.CreateDirectory(tempCandidate); } catch { /* candidate may still be entered manually */ }

        _console.WriteLine("");
        _console.WriteLine("Note steps require an existing, approved disposable directory. Capri never overwrites files.");
        _console.WriteLine($"  1. Use disposable candidate: {tempCandidate}");
        _console.WriteLine("  2. Enter an existing directory path");
        _console.WriteLine("  3. Cancel");
        _console.Write("Select workspace option (1-3, default cancel): ");

        var choice = await ReadLineWithTimeoutAsync(TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        if (choice is null) return null;
        string? selected = choice.Trim() switch
        {
            "1" => tempCandidate,
            "2" => await PromptForPathAsync(cancellationToken).ConfigureAwait(false),
            _ => null,
        };

        return ValidateWorkspace(selected);
    }

    private async Task<string?> PromptForPathAsync(CancellationToken cancellationToken)
    {
        _console.Write("Enter existing disposable directory path: ");
        return await ReadLineWithTimeoutAsync(TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
    }

    private string? ValidateWorkspace(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full;
        try { full = Path.GetFullPath(path.Trim().Trim('"')); }
        catch { _console.WriteErrorLine("capri: invalid workspace path."); return null; }

        if (!Path.IsPathFullyQualified(full) || !Directory.Exists(full))
        {
            _console.WriteErrorLine("capri: workspace must be an existing fully-qualified directory.");
            return null;
        }

        // Basic system-critical guards (reasonable, not a sandbox).
        var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            _console.WriteErrorLine("capri: refusing drive/root directory as a disposable workspace.");
            return null;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (IsUnder(full, windows) || IsUnder(full, pf) || IsUnder(full, pfx))
        {
            _console.WriteErrorLine("capri: refusing system/program directory as a disposable workspace.");
            return null;
        }

        return full;
    }

    private static bool IsUnder(string path, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent)) return false;
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ReadDecisionAsync(CancellationToken cancellationToken)
    {
        var line = await ReadLineWithTimeoutAsync(_approvalTimeout, cancellationToken).ConfigureAwait(false);
        if (line is null) return false;
        return string.Equals(line.Trim(), "approve PLAN", StringComparison.Ordinal);
    }

    private Task<string?> ReadLineWithTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        // No background Console.ReadLine: cancellation stops the input pump itself.
        // EOF returns null => reject.
        return ReadTimedAsync(timeout, cancellationToken);
    }

    private async Task<string?> ReadTimedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        return await _console.ReadLineAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
    }

    private static string SafeSingleLine(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var chars = value.Replace('\r', ' ').Replace('\n', ' ').Where(c => !char.IsControl(c)).Take(max).ToArray();
        return new string(chars).Trim();
    }

    private static string IndentBlock(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var sb = new StringBuilder();
        foreach (var line in normalized.Split('\n'))
            sb.AppendLine(CultureInfo.InvariantCulture, $"        {line}");
        return sb.ToString().TrimEnd();
    }
}
