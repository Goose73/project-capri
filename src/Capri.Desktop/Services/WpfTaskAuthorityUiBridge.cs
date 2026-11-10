using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Capri.Core.Autonomy;
using Capri.Core.Autonomy.Authority;

namespace Capri.Desktop.Services;

/// <summary>
/// Shows the exact-plan approval dialog on the WPF dispatcher. Never auto-approves.
/// </summary>
public sealed class WpfTaskAuthorityUiBridge : ITaskAuthorityUiBridge
{
    private readonly IUiDispatcher _dispatcher;
    public WpfTaskAuthorityUiBridge(IUiDispatcher dispatcher) => _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public Task<AuthorityApprovalDecision?> RequestApprovalAsync(ApprovalRequestSnapshot request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return _dispatcher.InvokeAsync(() => ShowDialog(request, cancellationToken), cancellationToken);
    }

    internal static bool IsApprovalAvailable(ApprovalRequestSnapshot? request) =>
        request is not null && request.TaskId != Guid.Empty && request.ApprovalId != Guid.Empty
        && !string.IsNullOrWhiteSpace(request.PlanHash) && request.Plan?.Steps is { Count: > 0 };

    private AuthorityApprovalDecision? ShowDialog(ApprovalRequestSnapshot request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        // If plan cannot be verified (missing hash/steps), approval is unavailable.
        if (!IsApprovalAvailable(request))
        {
            MessageBox.Show("Task approval unavailable: plan identity is incomplete or unverifiable.\n\nNo actions were taken.",
                "Capri — Approval unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return new AuthorityApprovalDecision(false, null);
        }

        var needsWorkspace = request.Plan.Steps.Any(s => s.Tool is TaskTool.CreateNote or TaskTool.OpenNote);
        var planText = BuildPlanText(request);
        var risks = DescribeRisks(request);

        var text = new System.Windows.Controls.TextBox
        {
            Text = planText,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 220,
            MaxHeight = 420,
        };

        var warning = new System.Windows.Controls.TextBlock
        {
            Text = risks + "\n\nApproval is explicit and bound to this exact plan hash. If the plan/task changes, this approval is invalid.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = System.Windows.Media.Brushes.OrangeRed,
        };

        var workspaceLabel = new System.Windows.Controls.TextBlock { Text = needsWorkspace ? "Required workspace (existing folder, for note actions):" : "Workspace: not required for this plan.", Margin = new Thickness(0, 0, 0, 4) };
        var workspaceBox = new System.Windows.Controls.TextBox { Text = needsWorkspace ? (TryGetDefaultWorkspace() ?? "") : "", IsEnabled = needsWorkspace, Margin = new Thickness(0, 0, 0, 8) };
        var browse = new System.Windows.Controls.Button { Content = "Browse...", IsEnabled = needsWorkspace, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10, 4, 10, 4) };
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select existing workspace folder" };
            if (dlg.ShowDialog() == true) workspaceBox.Text = dlg.FolderName;
        };

        var approve = new System.Windows.Controls.Button { Content = "Approve THIS exact plan", IsDefault = false, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var deny = new System.Windows.Controls.Button { Content = "Deny / Cancel", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) };
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(approve);
        buttons.Children.Add(deny);

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Project Capri — Explicit task approval required", FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(text);
        panel.Children.Add(warning);
        panel.Children.Add(workspaceLabel);
        panel.Children.Add(workspaceBox);
        if (needsWorkspace) panel.Children.Add(browse);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = "Capri — Approve exact plan",
            Content = panel,
            Width = 720,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.CanResize,
            Topmost = true,
            ShowInTaskbar = false,
        };

        AuthorityApprovalDecision? result = null;
        approve.Click += (_, _) =>
        {
            if (cancellationToken.IsCancellationRequested) { window.DialogResult = false; return; }
            string? workspace = null;
            if (needsWorkspace)
            {
                workspace = workspaceBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(workspace) || !Path.IsPathFullyQualified(workspace) || !Directory.Exists(workspace))
                {
                    MessageBox.Show(window, "Select an existing workspace folder before approving note actions.", "Workspace required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            result = new AuthorityApprovalDecision(true, workspace);
            window.DialogResult = true;
        };
        deny.Click += (_, _) => { result = new AuthorityApprovalDecision(false, null); window.DialogResult = false; };

        using var reg = cancellationToken.Register(() =>
        {
            try { _dispatcher.Invoke(() => { if (window.IsVisible) window.DialogResult = false; }); } catch { }
        });
        window.ShowDialog();
        return result?.Approved == true ? result : new AuthorityApprovalDecision(false, null);
    }

    internal static string BuildPlanText(ApprovalRequestSnapshot request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Task ID:      {request.TaskId}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Approval ID:  {request.ApprovalId}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Plan hash:    {request.PlanHash}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Provider:     {request.ProviderId ?? "(unknown)"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Model:        {request.ModelId ?? "(unknown)"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Requested:    {request.RequestedUtc:u}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Goal:         {request.Goal}");
        sb.AppendLine();
        sb.AppendLine("Ordered steps (exact):");
        for (var i = 0; i < request.Plan.Steps.Count; i++)
        {
            var s = request.Plan.Steps[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {i + 1}. tool={s.Tool} target={s.Target ?? "(none)"}");
            if (!string.IsNullOrWhiteSpace(s.Text))
                sb.AppendLine(CultureInfo.InvariantCulture, $"     text: \"{s.Text}\"");
        }
        sb.AppendLine();
        sb.AppendLine("NO actions occur without your explicit approval of this exact plan.");
        return sb.ToString();
    }

    internal static string DescribeRisks(ApprovalRequestSnapshot request)
    {
        var risks = new List<string>();
        foreach (var s in request.Plan.Steps)
        {
            switch (s.Tool)
            {
                case TaskTool.LaunchApp:
                    risks.Add($"Launches app: {s.Target}");
                    break;
                case TaskTool.CreateNote:
                    risks.Add($"Creates/writes file in selected workspace: {s.Target}");
                    break;
                case TaskTool.OpenNote:
                    risks.Add($"Opens file in associated app: {s.Target}");
                    break;
                case TaskTool.OpenUrl:
                    risks.Add($"Opens URL: {s.Target}");
                    break;
                case TaskTool.BraveSearch:
                    risks.Add($"Opens search/URL context: {s.Target}");
                    break;
                default:
                    risks.Add($"Unknown tool type {s.Tool} for target {s.Target} — review carefully.");
                    break;
            }
        }
        if (risks.Count == 0) risks.Add("No steps in plan — approval unavailable.");
        return "Potential effects/warnings:\n- " + string.Join("\n- ", risks.Distinct());
    }

    private static string? TryGetDefaultWorkspace()
    {
        try { var p = Path.Combine(Path.GetTempPath(), "CapriNotes"); return Directory.Exists(p) ? p : Path.GetTempPath(); } catch { return null; }
    }

}
