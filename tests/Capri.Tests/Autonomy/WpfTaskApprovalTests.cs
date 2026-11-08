using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Capri.Core.Autonomy;
using Capri.Desktop.Services;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Autonomy;

/// <summary>
/// Integration tests for the production <see cref="WpfTaskApproval"/> window.
/// The real WPF window is shown on the test host dispatcher and driven through its
/// automation peer. No desktop applications, folder pickers, or task executor are invoked.
/// </summary>
public sealed class WpfTaskApprovalTests
{
    private const string ApprovalTitle = "Approve Capri task";
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromSeconds(12);

    [Fact]
    public async Task AcceptNotePlanShowsExactPlanAndReturnsSelectedDisposableWorkspace()
    {
        using var workspace = new TempDirectory();
        var plan = TaskPlanParser.Parse(
            "{\"steps\":[{\"tool\":\"create_note\",\"target\":\"plan.txt\",\"text\":\"exact literal text\"},{\"tool\":\"open_note\",\"target\":\"plan.txt\"}]}");
        var approval = new WpfTaskApproval(workspace.Path);
        string? returned = null;
        string? details = null;
        var topmost = false;
        var width = 0d;

        await WpfTestHost.InvokeAsync(async () =>
        {
            using var automation = new WindowAutomation(
                ApprovalTitle,
                window => FindVisualChildren<TextBox>(window).Any(),
                window =>
                {
                    var text = FindVisualChildren<TextBox>(window).Single();
                    details = text.Text;
                    Assert.True(text.IsReadOnly);
                    Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
                    topmost = window.Topmost;
                    width = window.Width;
                    InvokeButton(window, "Approve exact plan");
                });

            returned = await approval.ApproveAsync(plan, CancellationToken.None).WaitAsync(ApprovalTimeout);
            automation.ThrowIfFailed();
            Assert.True(automation.SawWindow, "The production approval window was never shown.");
        });

        var expectedWorkspace = Path.GetFullPath(workspace.Path);
        Assert.Equal(expectedWorkspace, returned);
        Assert.NotNull(details);
        Assert.Contains("Workspace: " + expectedWorkspace, details, StringComparison.Ordinal);
        Assert.Contains("1. CreateNote: " + Path.Combine(expectedWorkspace, "plan.txt"), details, StringComparison.Ordinal);
        Assert.Contains("2. OpenNote: " + Path.Combine(expectedWorkspace, "plan.txt"), details, StringComparison.Ordinal);
        Assert.Contains("Exact note text: exact literal text", details, StringComparison.Ordinal);
        Assert.True(topmost, "Approval window should remain topmost.");
        Assert.Equal(620d, width);
        // Approval alone must not perform the note action.
        Assert.Empty(Directory.GetFiles(workspace.Path));
    }

    [Fact]
    public async Task DenyNotePlanReturnsNullAndPerformsNoFileAction()
    {
        using var workspace = new TempDirectory();
        var plan = TaskPlanParser.Parse(
            "{\"steps\":[{\"tool\":\"create_note\",\"target\":\"denied.txt\",\"text\":\"must not be written\"}]}");
        var approval = new WpfTaskApproval(workspace.Path);
        string? returned = null;
        string? details = null;

        await WpfTestHost.InvokeAsync(async () =>
        {
            using var automation = new WindowAutomation(
                ApprovalTitle,
                window => FindVisualChildren<Button>(window).Any(b => Equals(b.Content, "Deny")),
                window =>
                {
                    details = FindVisualChildren<TextBox>(window).Single().Text;
                    InvokeButton(window, "Deny");
                });

            returned = await approval.ApproveAsync(plan, CancellationToken.None).WaitAsync(ApprovalTimeout);
            automation.ThrowIfFailed();
            Assert.True(automation.SawWindow, "The production approval window was never shown.");
        });

        Assert.Null(returned);
        Assert.NotNull(details);
        Assert.Contains("Exact note text: must not be written", details, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(workspace.Path));
    }

    [Fact]
    public async Task AcceptNonNotePlanReturnsEmptyWorkspaceWithoutFolderPicker()
    {
        var plan = TaskPlanParser.Parse("{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"notepad\"}]}");
        var approval = new WpfTaskApproval();
        string? returned = null;
        string? details = null;

        await WpfTestHost.InvokeAsync(async () =>
        {
            using var automation = new WindowAutomation(
                ApprovalTitle,
                window => FindVisualChildren<TextBox>(window).Any(),
                window =>
                {
                    details = FindVisualChildren<TextBox>(window).Single().Text;
                    InvokeButton(window, "Approve exact plan");
                });

            returned = await approval.ApproveAsync(plan, CancellationToken.None).WaitAsync(ApprovalTimeout);
            automation.ThrowIfFailed();
            Assert.True(automation.SawWindow, "The production approval window was never shown.");
        });

        Assert.Equal(string.Empty, returned);
        Assert.NotNull(details);
        Assert.Contains("Workspace: (none)", details, StringComparison.Ordinal);
        Assert.Contains("1. LaunchApp: notepad", details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelWhileDialogVisibleClosesWindowAndThrows()
    {
        var plan = TaskPlanParser.Parse("{\"steps\":[{\"tool\":\"open_url\",\"target\":\"https://example.com/safe\"}]}");
        var approval = new WpfTaskApproval();
        using var cancellation = new CancellationTokenSource();

        await WpfTestHost.InvokeAsync(async () =>
        {
            using var automation = new WindowAutomation(
                ApprovalTitle,
                _ => true,
                window =>
                {
                    Assert.True(window.IsVisible);
                    // Delay cancellation briefly to prove the modal dispatcher loop remains responsive.
                    var delayed = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(250)
                    };
                    delayed.Tick += (_, _) =>
                    {
                        delayed.Stop();
                        cancellation.Cancel();
                    };
                    delayed.Start();
                });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => approval.ApproveAsync(plan, cancellation.Token).WaitAsync(ApprovalTimeout));
            automation.ThrowIfFailed();
            Assert.True(automation.SawWindow, "The production approval window was never shown.");
            Assert.DoesNotContain(
                Application.Current.Windows.Cast<Window>(),
                w => w.Title == ApprovalTitle);
        });
    }

    [Fact]
    public async Task CancellationBeforeDispatcherStepShowsNoApprovalWindow()
    {
        var plan = TaskPlanParser.Parse("{\"steps\":[{\"tool\":\"launch_app\",\"target\":\"calculator\"}]}");
        var approval = new WpfTaskApproval();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        OperationCanceledException? observed = null;

        await WpfTestHost.InvokeAsync(async () =>
        {
            try
            {
                await approval.ApproveAsync(plan, cancellation.Token);
            }
            catch (OperationCanceledException ex)
            {
                observed = ex;
            }

            Assert.DoesNotContain(
                Application.Current.Windows.Cast<Window>(),
                w => w.Title == ApprovalTitle);
        });

        Assert.NotNull(observed);
        Assert.Equal(cancellation.Token, observed.CancellationToken);
    }

    [Fact]
    public async Task MissingFixedWorkspaceIsDeniedWithoutShowingDialog()
    {
        var missing = Path.Combine(Path.GetTempPath(), "Capri.Tests", Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(missing));
        var plan = TaskPlanParser.Parse(
            "{\"steps\":[{\"tool\":\"create_note\",\"target\":\"note.txt\",\"text\":\"x\"}]}");
        var approval = new WpfTaskApproval(missing);
        string? returned = "not-run";

        await WpfTestHost.InvokeAsync(async () =>
        {
            returned = await approval.ApproveAsync(plan, CancellationToken.None).WaitAsync(ApprovalTimeout);
            Assert.DoesNotContain(
                Application.Current.Windows.Cast<Window>(),
                w => w.Title == ApprovalTitle);
        });

        Assert.Null(returned);
    }

    [Fact]
    public async Task UnsafeManualNoteTargetIsDeniedWithoutShowingDialog()
    {
        using var workspace = new TempDirectory();
        // Defense in depth: bypasses the parser to prove WpfTaskApproval rechecks note names.
        var plan = new TaskPlan([
            new TaskStep(TaskTool.CreateNote, "..\\escape.txt", "must not be written")
        ]);
        var approval = new WpfTaskApproval(workspace.Path);
        string? returned = "not-run";

        await WpfTestHost.InvokeAsync(async () =>
        {
            returned = await approval.ApproveAsync(plan, CancellationToken.None).WaitAsync(ApprovalTimeout);
            Assert.DoesNotContain(
                Application.Current.Windows.Cast<Window>(),
                w => w.Title == ApprovalTitle);
        });

        Assert.Null(returned);
        Assert.Empty(Directory.GetFiles(workspace.Path));
        Assert.False(File.Exists(Path.Combine(Directory.GetParent(workspace.Path)!.FullName, "escape.txt")));
    }

    private static void InvokeButton(Window window, string content)
    {
        var button = FindVisualChildren<Button>(window)
            .SingleOrDefault(b => string.Equals(b.Content as string, content, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Approval button '{content}' was not found.");
        Assert.True(button.IsEnabled, $"Approval button '{content}' was disabled.");
        var peer = new ButtonAutomationPeer(button);
        if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
        {
            throw new InvalidOperationException($"Approval button '{content}' does not expose Invoke.");
        }

        invoke.Invoke();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Finds the real approval window on the dispatcher and runs one scripted UI action.</summary>
    private sealed class WindowAutomation : IDisposable
    {
        private readonly string _title;
        private readonly Func<Window, bool> _isReady;
        private readonly Action<Window> _action;
        private readonly DispatcherTimer _poll;
        private readonly DispatcherTimer _watchdog;
        private bool _handled;
        private Window? _window;
        private Exception? _failure;

        public WindowAutomation(string title, Func<Window, bool> isReady, Action<Window> action)
        {
            _title = title;
            _isReady = isReady;
            _action = action;
            var dispatcher = Dispatcher.CurrentDispatcher;
            _poll = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(25)
            };
            _poll.Tick += Poll;
            _watchdog = new DispatcherTimer(DispatcherPriority.Send, dispatcher)
            {
                Interval = TimeSpan.FromSeconds(8)
            };
            _watchdog.Tick += Watchdog;
            _poll.Start();
            _watchdog.Start();
        }

        public bool SawWindow => _window is not null;

        public void ThrowIfFailed()
        {
            if (_failure is not null)
            {
                ExceptionDispatchInfo.Capture(_failure).Throw();
            }
        }

        public void Dispose()
        {
            StopTimers();
            if (!_handled && _window is { IsVisible: true })
            {
                try
                {
                    _window.Close();
                }
                catch (InvalidOperationException)
                {
                    // Window already closed.
                }
            }
        }

        private void Poll(object? sender, EventArgs e)
        {
            if (_handled)
            {
                return;
            }

            var window = Application.Current.Windows
                .Cast<Window>()
                .FirstOrDefault(w => w.Title == _title && w.IsVisible);
            if (window is null)
            {
                return;
            }

            _window = window;
            if (!window.IsLoaded || !_isReady(window))
            {
                return;
            }

            _handled = true;
            StopTimers();
            try
            {
                _action(window);
            }
            catch (Exception ex)
            {
                _failure = ex;
                TryClose(window);
            }
        }

        private void Watchdog(object? sender, EventArgs e)
        {
            if (_handled)
            {
                return;
            }

            _handled = true;
            StopTimers();
            _failure = new TimeoutException(
                $"Timed out waiting for approval window '{_title}' or its scripted UI action.");
            if (_window is not null)
            {
                TryClose(_window);
                return;
            }

            foreach (var window in Application.Current.Windows.Cast<Window>().ToArray())
            {
                TryClose(window);
            }
        }

        private void StopTimers()
        {
            _poll.Stop();
            _watchdog.Stop();
            _poll.Tick -= Poll;
            _watchdog.Tick -= Watchdog;
        }

        private static void TryClose(Window window)
        {
            try
            {
                if (window.IsVisible)
                {
                    window.DialogResult = false;
                }
            }
            catch (InvalidOperationException)
            {
                // Window is not a modal dialog or is already closing.
            }

            try
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
            catch (InvalidOperationException)
            {
                // Window is already closed.
            }
        }
    }
}
