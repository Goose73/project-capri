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
    private string? _previewSourcePath;
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
        ClearSelectedSource();
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

        DeveloperSourceContext? approvedSource = null;
        if (AttachSourceCheckbox.IsChecked == true)
        {
            if (_previewSourcePath is not { } relativePath ||
                _readApprovedSessionId != session.Id)
            {
                StatusMessage.Text = "Preview a file again before attaching it.";
                return;
            }

            // A preview does not authorize transmission. Explicit Yes/No
            // consent is required immediately before re-reading the file.
            var choice = MessageBox.Show(this,
                "Send the contents of this ONE source file with your next AI question?\n\n" +
                relativePath + "\n\n" +
                "Your configured provider will receive the text. If it is a cloud provider, " +
                "the source may leave this PC. Source files can contain passwords, keys " +
                "or other sensitive data that filename filtering cannot detect. " +
                "Only send code you are comfortable sharing.\n\n" +
                "The file is read-only; no commands or edits will be performed. " +
                "No source content is saved in chat history.\n\n" +
                "Send this file with the question?",
                "Capri - confirm source sharing",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (choice != MessageBoxResult.Yes)
            {
                StatusMessage.Text = "Source sharing cancelled. No message sent.";
                return;
            }

            try
            {
                var source = _files.ReadText(session.Id, relativePath, authorized: true);
                if (source.Length > 8000)
                {
                    StatusMessage.Text = "This source file exceeds the 8,000-character AI attachment limit.";
                    return;
                }
                approvedSource = new DeveloperSourceContext(session.Id, relativePath, source);
            }
            catch (Exception ex) when (IsLocalFailure(ex))
            {
                StatusMessage.Text = "Source file could not be read safely. No message sent.";
                return;
            }
        }

        // User explicitly requested the provider call by clicking Send.
        using var cts = new CancellationTokenSource();
        _generation = cts;
        SendButton.IsEnabled = false;
        StatusMessage.Text = approvedSource is null
            ? "Asking your configured AI provider. No project files are attached."
            : "Asking your configured AI provider with one approved source snapshot.";
        try
        {
            await _chat.SendWithSourceAsync(session.Id, text, approvedSource, cts.Token);
            if (Selected?.Id == session.Id)
            {
                MessageInput.Clear();
                ClearSelectedSource();
                StatusMessage.Text = "Response saved. Source snapshot was not stored in chat history.";
            }
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
            ClearSelectedSource();
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
            ClearSelectedSource();
            FilePreviewBox.Text = _files.ReadText(session.Id, file.RelativePath, authorized: true);
            FilePreviewBox.ScrollToHome();
            if (FilePreviewBox.Text.Length <= 8000)
            {
                _previewSourcePath = file.RelativePath;
                AttachSourceCheckbox.IsEnabled = true;
                SelectedSourceText.Text = $"Selected for possible AI sharing: {file.RelativePath} (not sent yet)";
            }
            else
                SelectedSourceText.Text = "Preview only: this file exceeds the 8,000-character AI attachment limit.";
            StatusMessage.Text = $"Local preview of {file.Name}. This text was NOT sent to AI.";
        }
        catch (Exception ex) when (IsLocalFailure(ex))
        {
            ClearSelectedSource();
            FilePreviewBox.Clear();
            StatusMessage.Text = "File preview denied or unavailable. Only small UTF-8 source files are supported.";
        }
    }

    private void ClearSelectedSource()
    {
        _previewSourcePath = null;
        AttachSourceCheckbox.IsChecked = false;
        AttachSourceCheckbox.IsEnabled = false;
        SelectedSourceText.Text = "No source file selected. Preview a file in the project files tab first.";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshSessions();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static bool IsLocalFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidOperationException or
              InvalidDataException or ArgumentException or DecoderFallbackException;
}
