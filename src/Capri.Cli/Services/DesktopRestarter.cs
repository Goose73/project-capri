using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Capri.Security.Autonomy;

namespace Capri.Cli.Services;

/// <summary>Result of a desktop restart.</summary>
public sealed record DesktopRestartResult(bool Started, string Message);

/// <summary>Safe desktop restart operation independent of task automation.</summary>
public interface IDesktopRestarter
{
    Task<DesktopRestartResult> RestartAsync(CancellationToken cancellationToken = default);
}

/// <summary>A running process with its executable and Windows session.</summary>
public sealed record DesktopProcessSnapshot(int ProcessId, string? ExecutablePath, int SessionId);

/// <summary>Discover and ask the desktop to close without terminating it forcibly.</summary>
public interface IDesktopProcessController
{
    IReadOnlyList<DesktopProcessSnapshot> GetRunningDesktops();
    Task<bool> RequestGracefulExitAsync(int processId, CancellationToken cancellationToken);
}

/// <summary>
/// Restart only the exact built Capri executable and only after the desktop
/// confirms there is no active task. Never changes settings, clears emergency
/// stop, force-kills a process, or starts a duplicate instance.
/// </summary>
public sealed class DesktopRestarter : IDesktopRestarter
{
    private readonly IDesktopLauncher _launcher;
    private readonly Func<string?> _repositoryRoot;
    private readonly IDesktopProcessController _processes;
    private readonly Func<CancellationToken, Task<TaskDesktopStatus?>> _status;

    public DesktopRestarter(
        IDesktopLauncher launcher,
        Func<string?> repositoryRoot,
        IDesktopProcessController processes,
        Func<CancellationToken, Task<TaskDesktopStatus?>> status)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _repositoryRoot = repositoryRoot ?? throw new ArgumentNullException(nameof(repositoryRoot));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _status = status ?? throw new ArgumentNullException(nameof(status));
    }

    public static DesktopRestarter CreateDefault(IDesktopLauncher launcher) =>
        new(launcher, RepositoryLocator.FindRepositoryRoot,
            new WindowsDesktopProcessController(), ct => TaskStatusPipe.QueryAsync(ct));

    public async Task<DesktopRestartResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return new(false, "Capri desktop restart is available only on Windows.");

        var plan = DesktopLaunchPlanner.Create(_repositoryRoot());
        if (plan.Kind != DesktopLaunchKind.BuiltExecutable)
            return new(false, "Build the Release desktop first: dotnet build Capri.sln -c Release.");

        var running = _processes.GetRunningDesktops();
        if (running.Count > 1)
            return new(false, "Multiple Capri desktop processes are running. Close extras manually.");

        if (running.Count == 1)
        {
            var target = running[0];
            if (string.IsNullOrWhiteSpace(target.ExecutablePath) ||
                !string.Equals(Path.GetFullPath(target.ExecutablePath),
                    Path.GetFullPath(plan.FileName), StringComparison.OrdinalIgnoreCase))
                return new(false,
                    "A Capri desktop from another or inaccessible location is running; no process was stopped.");

            if (target.SessionId != Process.GetCurrentProcess().SessionId)
                return new(false, "The running Capri belongs to another Windows session.");

            TaskDesktopStatus? task;
            try
            {
                task = await _status(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(false, "Task-status check timed out; restart cancelled.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or
                                       UnauthorizedAccessException or TimeoutException)
            {
                return new(false, "Could not verify current task status; restart cancelled.");
            }

            if (task is null)
                return new(false, "Desktop task status is unavailable; restart cancelled.");
            if (task.ActiveTaskId is not null)
                return new(false, "A Capri task is active. Finish or stop it before restarting.");

            cancellationToken.ThrowIfCancellationRequested();
            if (!await _processes.RequestGracefulExitAsync(
                    target.ProcessId, cancellationToken).ConfigureAwait(false))
                return new(false,
                    "Capri did not shut down cleanly. No force-close or duplicate launch was attempted.");

            if (_processes.GetRunningDesktops().Count != 0)
                return new(false, "Another Capri process appeared during restart; no duplicate started.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var launch = _launcher.Launch();
        return launch.Started
            ? new(true, running.Count == 0
                ? "Capri was not running, so the desktop was started."
                : "Capri restarted. Settings and emergency stop were preserved.")
            : new(false, "Could not start Capri: " + launch.Message);
    }
}

/// <summary>
/// Windows dispatcher-based graceful close. A hidden WPF tray application has
/// a dispatcher window even if MainWindowHandle is zero. Posts WM_QUIT only
/// to a WPF window owned by the verified process; never uses Process.Kill.
/// </summary>
public sealed class WindowsDesktopProcessController : IDesktopProcessController
{
    private const uint WmQuit = 0x0012;

    public IReadOnlyList<DesktopProcessSnapshot> GetRunningDesktops()
    {
        if (!OperatingSystem.IsWindows()) return [];

        var result = new List<DesktopProcessSnapshot>();
        foreach (var process in Process.GetProcessesByName("Capri.Desktop"))
        {
            using (process)
            {
                string? path = null;
                var sessionId = -1;
                try
                {
                    path = process.MainModule?.FileName;
                    sessionId = process.SessionId;
                }
                catch (Exception ex) when (ex is Win32Exception or
                                           InvalidOperationException or NotSupportedException)
                {
                    // Unreadable identities are never eligible for restart.
                }

                result.Add(new(process.Id, path, sessionId));
            }
        }
        return result;
    }

    public async Task<bool> RequestGracefulExitAsync(
        int processId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || processId <= 0) return false;

        using var process = Process.GetProcessById(processId);
        if (process.HasExited) return true;

        uint dispatcherThread = 0;
        var windows = new EnumWindowsProc((window, _) =>
        {
            var thread = GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)processId || thread == 0) return true;

            var buffer = new char[192];
            var length = GetClassName(window, buffer, buffer.Length);
            if (length <= 0 ||
                !new string(buffer, 0, length).StartsWith(
                    "HwndWrapper[Capri.Desktop;", StringComparison.Ordinal))
                return true;

            dispatcherThread = thread;
            return false;
        });

        _ = EnumWindows(windows, IntPtr.Zero);
        GC.KeepAlive(windows);

        if (dispatcherThread == 0 ||
            !PostThreadMessage(dispatcherThread, WmQuit, IntPtr.Zero, IntPtr.Zero))
            return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.HasExited;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr window, [Out] char[] className, int maxCount);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
}
