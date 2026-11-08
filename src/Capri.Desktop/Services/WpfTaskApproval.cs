using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Capri.Core.Autonomy;
using Microsoft.Win32;

namespace Capri.Desktop.Services;

/// <summary>Independent user decision; no remembered or model-supplied consent.</summary>
public sealed class WpfTaskApproval : ITaskApproval
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    private readonly string? _fixedWorkspace;

    public WpfTaskApproval(string? fixedWorkspace = null) => _fixedWorkspace = fixedWorkspace;

    public Task<string?> ApproveAsync(TaskPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Provider calls resume off UI thread; marshal the complete permission flow onto WPF dispatcher.
        return Application.Current.Dispatcher.InvokeAsync(() => Decide(plan, _fixedWorkspace, cancellationToken)).Task;
    }

    private static string? Decide(TaskPlan plan, string? fixedWorkspace, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string? workspace = null;
        if (plan.Steps.Any(s => s.Tool is TaskTool.CreateNote or TaskTool.OpenNote))
        {
            if (fixedWorkspace is null)
            {
                MessageBox.Show("Select an EXISTING, disposable folder for NEW note files only. Never select a folder containing important files. Cancel to deny.", "Capri task workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
                var picker = new OpenFolderDialog { Title = "Select disposable task workspace", Multiselect = false };
                if (picker.ShowDialog() != true) return null;
                workspace = picker.FolderName;
            }
            else workspace = fixedWorkspace;
            if (!Path.IsPathFullyQualified(workspace) || !Directory.Exists(workspace)) return null;
            workspace = Path.GetFullPath(workspace);
            if (!plan.Steps.Where(s => s.Tool is TaskTool.CreateNote or TaskTool.OpenNote).All(s => TaskPlanParser.SafeNoteName(s.Target))) return null;
        }
        var details = new StringBuilder("Approve this exact task plan? No actions occur on denial.\n\n");
        details.AppendLine("Workspace: " + (workspace ?? "(none)"));
        foreach (var (step, i) in plan.Steps.Select((s, i) => (s, i)))
        {
            details.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{i + 1}. {step.Tool}: {(step.Tool is TaskTool.CreateNote or TaskTool.OpenNote ? Path.Combine(workspace!, step.Target) : step.Target)}");
            if (step.Text is not null) details.AppendLine("Exact note text: " + step.Text);
        }
        ct.ThrowIfCancellationRequested();
        // Dedicated selectable, scrollable dialog avoids truncating literal note text in MessageBox.
        var window = new Window { Title = "Approve Capri task", Width = 620, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ShowInTaskbar = true };
        if (Application.Current.MainWindow is { IsVisible: true } owner && owner != window) window.Owner = owner;
        var layout = new System.Windows.Controls.DockPanel { Margin = new Thickness(12) };
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var approve = new System.Windows.Controls.Button { Content = "Approve exact plan", Margin = new Thickness(5), Padding = new Thickness(8) };
        var deny = new System.Windows.Controls.Button { Content = "Deny", Margin = new Thickness(5), Padding = new Thickness(8), IsCancel = true };
        approve.Click += (_, _) => window.DialogResult = true;
        deny.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(deny); buttons.Children.Add(approve);
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(new System.Windows.Controls.TextBox { Text = details.ToString(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto });
        window.Content = layout;
        window.Loaded += (_, _) =>
        {
            window.Activate();
            // Windows may refuse foreground activation (for example, another app owns
            // the input queue); this is best-effort and never implies approval.
            _ = SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        };
        using var cancellation = ct.Register(() => window.Dispatcher.BeginInvoke(() => window.Close()));
        var allowed = window.ShowDialog() == true;
        ct.ThrowIfCancellationRequested();
        return allowed ? workspace ?? string.Empty : null;
    }
}
