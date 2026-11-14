using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Capri.Core.Abstractions;
using Capri.Core.Conversation;
using Capri.Core.Models;
using Microsoft.Win32;

namespace Capri.Desktop.Views;

/// <summary>
/// GUI-first developer chat and permission-scoped local previews. No project
/// code may be sent to the AI, executed or edited by opening this window.
/// </summary>
public partial class DeveloperWorkspaceWindow : Window
{
    private readonly IDeveloperSessionStore _sessions;
    private readonly IDeveloperConversationStore _history;
    private readonly IDeveloperWorkspaceReader _files;
    private readonly DeveloperConversationService _chat;
    private Guid? _readApprovedSessionId;
    private CancellationTokenSource? _generation;

    public DeveloperWorkspaceWindow(
        IDeveloperSessionStore sessions,
        IDeveloperConversationStore history,
        IDeveloperWorkspaceReader files,
        DeveloperConversationService chat)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));

        InitializeComponent();
        Loaded += (_, _) => RefreshSessions();
        Closed += (_, _) => _generation?.Cancel();
    }

    private DeveloperSession? Selected => SessionList.SelectedItem as DeveloperSession;

    private void RefreshSessions(Guid? focus = null)
    {
        try
        {
            var selectedId = focus ?? Selected?.Id;
            var sessions = _sessions.List();
            SessionList.ItemsSource = sessions;
            SessionList.SelectedItem = sessions.FirstOrDefault(s => s.Id == selectedId);
            StatusMessage.Text = sessions.Count == 0
                ? "No projects saved yet. Choose a folder to add your first session."
                : "Choose a project, then chat or request read-only file access.";
        }
        catch (Exception ex) when (IsLocalFailure(ex))
        {
            SessionList.ItemsSource = null;
            StatusMessage.Text = "Could not load protected project metadata. Nothing was changed.";
        }
    }

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _readApprovedSessionId = null; // a new session needs a fresh consent
        RelativeFolderBox.Text = string.Empty;
        FilePreviewBox.Clear();
        ProjectItemsList.ItemsSource = null;
        ReloadConversation();
    }

    private void ReloadConversation()
    {
        ConversationBox.Clear();
        if (Selected is not { } session) return;

        try
        {
            var history = _history.Read(session.Id);
            if (history.Count == 0)
            {
                ConversationBox.Text = "No messages yet. Type a question below to start this project conversation.";
                return;
            }

            ConversationBox.Text = string.Join(
                Environment.NewLine + Environment.NewLine,
                history.Select(entry =>
                    $"{(entry.Role == ChatRole.User ? "You" : "Capri")}: {entry.Content}"));
            ConversationBox.ScrollToEnd();
        }
        catch (Exception ex) when (IsLocalFailure(ex))
        {
            ConversationBox.Text = "Protected conversation could not be read.";
            StatusMessage.Text = "Conversation history was not changed.";
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
            RefreshSessions(saved.Id);
            StatusMessage.Text = $"Saved '{saved.DisplayName}'. No project code was opened.";
        }
        catch (Exception ex) when (IsLocalFailure(ex))
        {
            StatusMessage.Text = "Could not safely register the folder. Select an existing non-linked directory.";
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } session)
        {
            StatusMessage.Text = "Choose a project before sending a message.";
            return;
        }
        if (_generation is not null) return;
        var text = MessageInput.Text;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 8000)
        {
            StatusMessage.Text = "Enter a message between 1 and 8000 characters.";
            return;
        }

        // User explicitly requested the provider call by clicking Send.
        using var cts = new CancellationTokenSource();
        _generation = cts;
        SendButton.IsEnabled = false;
        StatusMessage.Text = "Asking your configured AI provider. No project files are attached.";
        try
        {
            await _chat.SendAsync(session.Id, text, cts.Token);
            MessageInput.Clear();
            StatusMessage.Text = "Response saved privately. GUI and CLI share this conversation.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage.Text = "Generation cancelled. Any already-saved user message remains in history.";
        }
        catch (Exception)
        {
            // Async WPF event handlers must not propagate provider exceptions:
            // show only a safe recovery hint, never raw credentials/URLs.
            StatusMessage.Text = "AI reply failed. Check model, cloud consent, connectivity and private storage.";
        }
        finally
        {
            _generation = null;
            SendButton.IsEnabled = true;
            ReloadConversation();
        }
    }

    private void CancelReply_Click(object sender, RoutedEventArgs e) => _generation?.Cancel();

    private void ReloadHistory_Click(object sender, RoutedEventArgs e) => ReloadConversation();

    private bool RequestReadApproval(DeveloperSession session)
    {
        if (_readApprovedSessionId == session.Id) return true;

        var consent = MessageBox.Show(this,
            "Allow Capri to list eligible filenames and preview up to 32 KB of a selected " +
            "source file in this project DURING THIS WINDOW ONLY?\n\n" +
            session.WorkspaceDirectory + "\n\n" +
            "Hidden, linked, common secret files and unsupported formats are blocked. " +
            "No file content is sent to the AI automatically. No code will be edited or run.\n\n" +
            "Allow local read-only previews?",
            "Capri — project reading permission",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (consent != MessageBoxResult.Yes) return false;
        _readApprovedSessionId = session.Id;
        return true;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e) => BrowseFolder();

    private void BrowseFolder()
    {
        if (Selected is not { } session)
        {
            StatusMessage.Text = "Choose a project before browsing.";
            return;
        }
        if (!RequestReadApproval(session))
        {
            StatusMessage.Text = "Folder reading was not authorized.";
            return;
        }
        try
        {
            var items = _files.List(session.Id, RelativeFolderBox.Text, authorized: true);
            ProjectItemsList.ItemsSource = items;
            FilePreviewBox.Clear();
            StatusMessage.Text = $"Listed {items.Count} eligible entries. Nothing has been sent to the AI.";
        }
        catch (Exception ex) when (IsLocalFailure(ex))
        {
            ProjectItemsList.ItemsSource = null;
            FilePreviewBox.Clear();
            StatusMessage.Text = "Folder could not be read under the read-only policy.";
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectItemsList.SelectedItem is not DeveloperWorkspaceItem { IsDirectory: true } directory)
        {
            StatusMessage.Text = "Select a folder from the list.";
            return;
        }
        RelativeFolderBox.Text = directory.RelativePath;
        BrowseFolder();
    }

    private void PreviewFile_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } session ||
            ProjectItemsList.SelectedItem is not DeveloperWorkspaceItem { IsDirectory: false } file)
        {
            StatusMessage.Text = "Select a source file, then click Preview.";
            return;
        }
        if (!RequestReadApproval(session))
        {
            StatusMessage.Text = "File reading was not authorized.";
            return;
        }
        try
        {
            FilePreviewBox.Text = _files.ReadText(session.Id, file.RelativePath, authorized: true);
            FilePreviewBox.ScrollToHome();
            StatusMessage.Text = $"Local preview of {file.Name}. This text was NOT sent to AI.";
        }
        catch (Exception ex) when (IsLocalFailure(ex))
        {
            FilePreviewBox.Clear();
            StatusMessage.Text = "File preview denied or unavailable. Only small UTF-8 source files are supported.";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshSessions();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static bool IsLocalFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidOperationException or
              InvalidDataException or ArgumentException or DecoderFallbackException;
}
