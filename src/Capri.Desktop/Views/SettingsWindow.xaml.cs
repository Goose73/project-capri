using System.Windows;
using Capri.Desktop.ViewModels;

namespace Capri.Desktop.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _initialized;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PendingApiKeyCleared += ViewModel_PendingApiKeyCleared;
        Closed += SettingsWindow_Closed;
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _viewModel.LoadAsync();
        _initialized = true;
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.PendingApiKeyCleared -= ViewModel_PendingApiKeyCleared;
    }

    private void ViewModel_PendingApiKeyCleared(object? sender, EventArgs e)
    {
        // Profile switches may complete on a background continuation; never touch WPF controls off-thread.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ApiKeyBox.Clear));
            return;
        }

        ApiKeyBox.Clear();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // Never bind the secret; pass the transient value to the view model for save/test operations.
        _viewModel.SetPendingApiKey(ApiKeyBox.Password);
    }

    private void ClearStoredKeyButton_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Clear();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SaveCommand.CanExecute(null))
        {
            await _viewModel.SaveCommand.ExecuteAsync(null);
        }

        // Close on successful save message; keep open on failure text.
        if (string.Equals(_viewModel.SaveStatus, "Settings saved.", StringComparison.Ordinal))
        {
            DialogResult = true;
            Close();
        }
    }
}
