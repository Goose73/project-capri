using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Capri.Desktop.Interop;
using Capri.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Capri.Desktop.Views;

public partial class ChatPanelWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly OverlayChatViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private bool _allowClose;

    public ChatPanelWindow(OverlayChatViewModel viewModel, IServiceProvider serviceProvider)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        InitializeComponent();
        DataContext = _viewModel;

        SourceInitialized += OnSourceInitialized;
        _viewModel.Messages.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(ScrollMessagesToEnd);
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try { InputBox.Focus(); } catch { /* focus is best-effort */ }
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                _ = SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            }
        }
        catch
        {
            // Non-fatal.
        }
    }

    /// <summary>Positions the chat panel beside the character window, clamped to the character's monitor work area.</summary>
    public void RepositionNear(Window characterWindow)
    {
        if (characterWindow is null)
        {
            return;
        }

        try
        {
            var workArea = MonitorWorkArea.GetWorkAreaDip(characterWindow);
            var gap = 8.0;
            var leftCandidate = characterWindow.Left - Width - gap;
            var rightCandidate = characterWindow.Left + characterWindow.Width + gap;

            var leftFits = leftCandidate >= workArea.Left - 0.5;
            var rightFits = rightCandidate + Width <= workArea.Right + 0.5;
            var desiredLeft = leftFits ? leftCandidate : (rightFits ? rightCandidate : leftCandidate);

            var desiredTop = characterWindow.Top + characterWindow.Height - Height;
            var maxLeft = Math.Max(workArea.Left, workArea.Right - Width);
            var maxTop = Math.Max(workArea.Top, workArea.Bottom - Height);

            Left = Math.Min(Math.Max(workArea.Left, desiredLeft), maxLeft);
            Top = Math.Min(Math.Max(workArea.Top, desiredTop), maxTop);

            // If clamped position would overlap the character badly, try the other side.
            var overlapsCharacter = Left < characterWindow.Left + characterWindow.Width &&
                                    Left + Width > characterWindow.Left &&
                                    Top < characterWindow.Top + characterWindow.Height &&
                                    Top + Height > characterWindow.Top;
            if (overlapsCharacter)
            {
                var alternate = Math.Abs(desiredLeft - leftCandidate) < 0.1 ? rightCandidate : leftCandidate;
                Left = Math.Min(Math.Max(workArea.Left, alternate), maxLeft);
            }
        }
        catch
        {
            // Best-effort positioning.
        }
    }

    private void ScrollMessagesToEnd()
    {
        try
        {
            MessagesScroll.ScrollToEnd();
        }
        catch
        {
            // Non-fatal UI nicety.
        }
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            e.Handled = true;
            if (_viewModel.SendCommand.CanExecute(null))
            {
                _viewModel.SendCommand.Execute(null);
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        // Placeholder action: mic pipeline wiring is out of Desktop visual scope.
        _viewModel.StatusText = "Mic button (placeholder) — voice input not wired in this UI yet.";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    public void AllowShutdownClose()
    {
        _allowClose = true;
    }

    public async Task OpenSettingsAsync()
    {
        try
        {
            var window = _serviceProvider.GetRequiredService<SettingsWindow>();
            window.Owner = IsVisible ? this : (Owner?.IsVisible == true ? Owner : null);
            await window.InitializeAsync();
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Settings could not be opened. Check logs for details.",
                "Capri",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
