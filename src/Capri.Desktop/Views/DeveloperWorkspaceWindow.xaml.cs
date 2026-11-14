using System.IO;
using System.Windows;
using Capri.Core.Abstractions;
using Microsoft.Win32;

namespace Capri.Desktop.Views;

/// <summary>
/// GUI entry point for metadata-only project sessions. Future coding tools must
/// use the shared task authority and request their own separate permissions.
/// </summary>
public partial class DeveloperWorkspaceWindow : Window
{
    private readonly IDeveloperSessionStore _sessions;

    public DeveloperWorkspaceWindow(IDeveloperSessionStore sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        InitializeComponent();
        Loaded += (_, _) => RefreshSessions();
    }

    private void RefreshSessions()
    {
        try
        {
            SessionList.ItemsSource = _sessions.List();
            if (SessionList.Items.Count == 0)
                StatusMessage.Text = "No projects saved yet. Choose a folder to add your first session.";
            else
                StatusMessage.Text = "Sessions are saved privately on this PC. No source files were opened.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SessionList.ItemsSource = null;
            StatusMessage.Text = "Could not read protected project-session metadata. Nothing was changed.";
        }
    }

    private void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = "Choose a development project folder",
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;

        try
        {
            var saved = _sessions.Create(picker.FolderName);
            RefreshSessions();
            StatusMessage.Text = $"Saved '{saved.DisplayName}'. No code was read or changed.";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage.Text = "Capri could not safely save this folder. Select an existing, non-linked project directory.";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshSessions();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
