using System.Diagnostics;
using System.Text;
using Capri.Core.Abstractions.Automation;
using Capri.Core.Autonomy;

namespace Capri.Automation.Autonomy;

/// <summary>Only fixed, allowlisted desktop actions. A successful dispatch is not a verified window.</summary>
public sealed class SafeTaskExecutor : ITaskExecutor
{
    private readonly IAppAliasResolver _aliases;
    private readonly IAppLauncher _apps;
    private readonly IWebLauncher _web;

    public SafeTaskExecutor(IAppAliasResolver aliases, IAppLauncher apps, IWebLauncher web)
    { _aliases = aliases; _apps = apps; _web = web; }

    public async Task<TaskOutcome> ExecuteAsync(TaskStep action, string? workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (action.Tool)
        {
            case TaskTool.LaunchApp:
                var app = await _aliases.ResolveAsync(action.Target, cancellationToken);
                if (app is null) return new(false, false, "Approved application unavailable.");
                var launched = await _apps.LaunchAsync(app, cancellationToken);
                return new(launched.Succeeded, false, launched.Succeeded ? $"Launch dispatched: {app.Name}; window not verified." : "App launch failed.");
            case TaskTool.OpenUrl:
                var web = await _web.OpenAsync(new Uri(action.Target), cancellationToken);
                return new(web.Succeeded, false, web.Succeeded ? "URL dispatched to default browser; page not verified." : "URL launch failed.");
            case TaskTool.BraveSearch:
                var brave = FindBrave();
                if (brave is null) return new(false, false, "Brave unavailable (not installed in supported location). No search dispatched.");
                var url = "https://search.brave.com/search?q=" + Uri.EscapeDataString(action.Target);
                using (var process = Process.Start(new ProcessStartInfo(brave) { UseShellExecute = false, ArgumentList = { url } }))
                {
                    if (process is null) return new(false, false, "Brave launch unavailable.");
                    var observed = await ObserveWindowAsync(process, cancellationToken);
                    return new(true, observed, observed
                        ? "Brave window observed after search dispatch; page content and results not verified."
                        : "Brave search dispatched; browser window and page content not verified.");
                }
            case TaskTool.CreateNote:
                var note = NotePath(workspace, action.Target);
                // CreateNew prevents overwrite, even across concurrent tasks. Reparse points are rejected above.
                await using (var stream = new FileStream(note, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    await writer.WriteAsync(action.Text!.AsMemory(), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var contents = await File.ReadAllTextAsync(note, cancellationToken);
                return new(contents == action.Text, contents == action.Text, contents == action.Text ? $"New note contents verified on disk: {action.Target}. No UI typing claimed." : "Note verification failed; no further steps.");
            case TaskTool.OpenNote:
                var path = NotePath(workspace, action.Target);
                if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return new(false, false, "Note unavailable or linked.");
                var notepad = await _aliases.ResolveAsync("notepad", cancellationToken);
                if (notepad?.ExecutablePath is null || !File.Exists(notepad.ExecutablePath)) return new(false, false, "Notepad unavailable.");
                var preDispatchWindows = WindowObservation.SnapshotWindows(Path.GetFileName(path));
                using (var process = Process.Start(new ProcessStartInfo(notepad.ExecutablePath) { UseShellExecute = false, ArgumentList = { path } }))
                {
                    if (process is null) return new(false, false, "Notepad launch unavailable.");
                    var observed = await ObserveNotepadWindowAsync(process, notepad.ExecutablePath, Path.GetFileName(path), preDispatchWindows, cancellationToken);
                    return new(true, observed, observed
                        ? "Matching titled Notepad window observed after opening note; editor contents not verified."
                        : "Note open dispatched to Notepad; no new matching window observed (handoff or reuse uncertain); editor contents not verified.");
                }
            default: return new(false, false, "Unsupported action.");
        }
    }

    private static async Task<bool> ObserveWindowAsync(Process process, CancellationToken ct)
    {
        // Observe the exact launched process, not a pre-existing unrelated window or a title string.
        // Browser single-instance handoff may exit immediately: that remains dispatch-only.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                process.Refresh();
                if (process.HasExited) return false;
                if (process.MainWindowHandle != IntPtr.Zero) return true;
            }
            catch (InvalidOperationException) { return false; }
            await Task.Delay(150, ct);
        }
        return false;
    }

    private static async Task<bool> ObserveNotepadWindowAsync(Process launched, string executablePath, string noteName, HashSet<IntPtr> preDispatchWindows, CancellationToken ct)
    {
        // Modern Notepad may hand the file to an existing process and exit. Match only
        // the unique file title and a window owned by the approved Notepad executable.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (WindowObservation.HasNewNotepadWindow(executablePath, noteName, preDispatchWindows)) return true;
            try { launched.Refresh(); if (launched.HasExited) { /* continue for shell handoff */ } }
            catch (InvalidOperationException) { /* process handoff */ }
            await Task.Delay(150, ct);
        }
        return false;
    }

    private static string NotePath(string? workspace, string name)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Path.IsPathFullyQualified(workspace) || !Directory.Exists(workspace) || !TaskPlanParser.SafeNoteName(name))
            throw new InvalidOperationException("Approved workspace or note name invalid.");
        var root = Path.GetFullPath(workspace);
        // No directory traversal; do not follow linked workspace or ancestor directories.
        var current = new DirectoryInfo(root);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked workspace path rejected.");
            current = current.Parent;
        }
        return Path.Combine(root, name);
    }

    private static string? FindBrave()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var path = Path.Combine(root, "BraveSoftware", "Brave-Browser", "Application", "brave.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
