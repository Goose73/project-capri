using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Desktop.Views;

/// <summary>
/// Transparent companion window showing only the Capri character. Draggable; click toggles chat.
/// </summary>
public partial class CharacterWindow : Window, IOverlayController
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const double AvatarBaseHeight = 280d;
    private const double AvatarBaseWidth = 240d;
    private const int DefaultCharacterHeight = 250;
    private const int MinCharacterHeight = 150;
    private const int MaxCharacterHeight = 400;

    private readonly AvatarPresentationService _avatarPresentation;
    private readonly IServiceProvider _serviceProvider;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILogger<CharacterWindow> _logger;
    private readonly DispatcherTimer _saveDebounceTimer;
    private ChatPanelWindow? _chatPanel;
    private bool _allowClose;
    private bool _hasValidPosition;
    private bool _suppressPositionSave;
    private Point _mouseDownScreen;
    private bool _isDragging;
    private bool _mouseDownOnAvatar;
    private double _lastSavedLeft = double.NaN;
    private double _lastSavedTop = double.NaN;
    private int _lastSavedHeight = DefaultCharacterHeight;

    public CharacterWindow(
        AvatarPresentationService avatarPresentation,
        IServiceProvider serviceProvider,
        IAppSettingsStore settingsStore,
        ILogger<CharacterWindow>? logger = null)
    {
        _avatarPresentation = avatarPresentation ?? throw new ArgumentNullException(nameof(avatarPresentation));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _logger = logger ?? NullLogger<CharacterWindow>.Instance;

        InitializeComponent();

        Avatar.State = _avatarPresentation.DisplayState;
        Avatar.ReduceMotion = _avatarPresentation.ReduceMotion;
        _avatarPresentation.PropertyChanged += AvatarPresentation_PropertyChanged;
        _settingsStore.Changed += SettingsStore_Changed;

        _saveDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveDebounceTimer.Tick += SaveDebounceTimer_Tick;

        SourceInitialized += OnSourceInitialized;
        IsVisibleChanged += OnIsVisibleChanged;
        LocationChanged += OnLocationChanged;
        DpiChanged += OnDpiChanged;
        Closed += OnClosed;
    }

    private void AvatarPresentation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AvatarPresentationService.DisplayState))
        {
            Dispatcher.BeginInvoke(() => Avatar.State = _avatarPresentation.DisplayState);
        }
        else if (e.PropertyName == nameof(AvatarPresentationService.ReduceMotion))
        {
            Dispatcher.BeginInvoke(() => Avatar.ReduceMotion = _avatarPresentation.ReduceMotion);
        }
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
            // Non-fatal: tool window style is a usability improvement.
        }

        ApplyUiSettings(_settingsStore.Current);
        RestoreOrPlaceDefault(_settingsStore.Current);
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            if (!_hasValidPosition)
            {
                RestoreOrPlaceDefault(_settingsStore.Current);
            }

            ClampToCurrentMonitor();
        }
        else
        {
            // Hiding the companion hides the chat panel; tray toggle remains coherent.
            _saveDebounceTimer.Stop();
            _chatPanel?.Hide();
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        _chatPanel?.RepositionNear(this);
        if (_suppressPositionSave || !_hasValidPosition)
        {
            return;
        }

        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        ClampToCurrentMonitor();
    }

    private void SettingsStore_Changed(object? sender, AppSettingsChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var previousHeight = Math.Clamp(e.Previous?.Ui.CharacterHeight ?? DefaultCharacterHeight, MinCharacterHeight, MaxCharacterHeight);
            var nextHeight = Math.Clamp(e.Current.Ui.CharacterHeight, MinCharacterHeight, MaxCharacterHeight);
            if (previousHeight != nextHeight)
            {
                ApplyUiSettings(e.Current);
                ClampToCurrentMonitor();
            }
        });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Tick -= SaveDebounceTimer_Tick;
        _settingsStore.Changed -= SettingsStore_Changed;
        _avatarPresentation.PropertyChanged -= AvatarPresentation_PropertyChanged;
    }

    private async void SaveDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _saveDebounceTimer.Stop();
        await SaveCharacterPlacementAsync().ConfigureAwait(true);
    }

    private void ApplyUiSettings(AppSettings settings)
    {
        var height = Math.Clamp(settings?.Ui.CharacterHeight ?? DefaultCharacterHeight, MinCharacterHeight, MaxCharacterHeight);
        _lastSavedHeight = height;
        var avatarHeight = (double)height;
        var avatarWidth = Math.Round(avatarHeight * (AvatarBaseWidth / AvatarBaseHeight), MidpointRounding.AwayFromZero);

        Avatar.Height = avatarHeight;
        Avatar.Width = avatarWidth;
        Width = Math.Max(avatarWidth + 10, 120);
        Height = Math.Max(avatarHeight + 20, 170);
    }

    private void RestoreOrPlaceDefault(AppSettings settings)
    {
        var ui = settings?.Ui;
        if (ui?.CharacterLeft is double left && ui.CharacterTop is double top &&
            double.IsFinite(left) && double.IsFinite(top))
        {
            _suppressPositionSave = true;
            try
            {
                Left = left;
                Top = top;
                _hasValidPosition = true;
                ClampToCurrentMonitor(saveAfterClamp: false);
            }
            finally
            {
                _suppressPositionSave = false;
            }

            _lastSavedLeft = Left;
            _lastSavedTop = Top;
            return;
        }

        PositionBottomRightOfActiveMonitor();
    }

    private void PositionBottomRightOfActiveMonitor()
    {
        _suppressPositionSave = true;
        try
        {
            var workArea = GetCurrentMonitorWorkAreaDip();
            Left = Math.Max(workArea.Left, workArea.Right - Width - 12);
            Top = Math.Max(workArea.Top, workArea.Bottom - Height - 12);
            _hasValidPosition = true;
            ClampToRect(workArea);
        }
        catch
        {
            if (!_hasValidPosition)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                _hasValidPosition = true;
            }
        }
        finally
        {
            _suppressPositionSave = false;
        }
    }

    private void ClampToCurrentMonitor(bool saveAfterClamp = true)
    {
        if (!_hasValidPosition)
        {
            return;
        }

        var workArea = GetCurrentMonitorWorkAreaDip();
        var changed = ClampToRect(workArea);
        if (changed && saveAfterClamp && !_suppressPositionSave)
        {
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }
    }

    private bool ClampToRect(Rect workArea)
    {
        if (Width <= 0 || Height <= 0 || workArea.Width <= 0 || workArea.Height <= 0)
        {
            return false;
        }

        var maxLeft = Math.Max(workArea.Left, workArea.Right - Width);
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - Height);
        var newLeft = Math.Min(Math.Max(workArea.Left, Left), maxLeft);
        var newTop = Math.Min(Math.Max(workArea.Top, Top), maxTop);
        if (double.IsNaN(newLeft) || double.IsNaN(newTop))
        {
            return false;
        }

        var changed = Math.Abs(newLeft - Left) > 0.1 || Math.Abs(newTop - Top) > 0.1;
        if (changed)
        {
            _suppressPositionSave = true;
            try
            {
                Left = newLeft;
                Top = newTop;
            }
            finally
            {
                _suppressPositionSave = false;
            }
        }

        return changed;
    }

    private Rect GetCurrentMonitorWorkAreaDip()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        return DeviceRectToDip(hwnd, info.rcWork);
                    }
                }
            }

            // Fallback: monitor from current/center point.
            var center = new POINT
            {
                X = (int)Math.Round((Left + Width / 2) * GetDpiScaleX(hwnd)),
                Y = (int)Math.Round((Top + Height / 2) * GetDpiScaleY(hwnd))
            };
            var monitorFromPoint = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
            if (monitorFromPoint != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitorFromPoint, ref info))
                {
                    return DeviceRectToDip(hwnd, info.rcWork);
                }
            }
        }
        catch
        {
            // Fall through to primary work area.
        }

        return SystemParameters.WorkArea;
    }

    private Rect DeviceRectToDip(IntPtr hwnd, RECT rect)
    {
        try
        {
            var source = hwnd != IntPtr.Zero ? HwndSource.FromHwnd(hwnd) : PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is { } target)
            {
                var fromDevice = target.TransformFromDevice;
                var topLeft = fromDevice.Transform(new Point(rect.Left, rect.Top));
                var bottomRight = fromDevice.Transform(new Point(rect.Right, rect.Bottom));
                return new Rect(topLeft, bottomRight);
            }
        }
        catch
        {
            // Fall through.
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        var dy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;
        return new Rect(rect.Left / dx, rect.Top / dy, (rect.Right - rect.Left) / dx, (rect.Bottom - rect.Top) / dy);
    }

    private double GetDpiScaleX(IntPtr hwnd)
    {
        try
        {
            var source = hwnd != IntPtr.Zero ? HwndSource.FromHwnd(hwnd) : PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformToDevice.M11;
            }
        }
        catch { }
        return VisualTreeHelper.GetDpi(this).DpiScaleX;
    }

    private double GetDpiScaleY(IntPtr hwnd)
    {
        try
        {
            var source = hwnd != IntPtr.Zero ? HwndSource.FromHwnd(hwnd) : PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformToDevice.M22;
            }
        }
        catch { }
        return VisualTreeHelper.GetDpi(this).DpiScaleY;
    }

    private async Task SaveCharacterPlacementAsync()
    {
        if (!_hasValidPosition || _suppressPositionSave)
        {
            return;
        }

        var left = Left;
        var top = Top;
        var height = Math.Clamp(_settingsStore.Current.Ui.CharacterHeight, MinCharacterHeight, MaxCharacterHeight);
        if (Math.Abs(left - _lastSavedLeft) < 0.5 &&
            Math.Abs(top - _lastSavedTop) < 0.5 &&
            height == _lastSavedHeight)
        {
            return;
        }

        try
        {
            var current = _settingsStore.Current;
            var updated = current with
            {
                Ui = current.Ui with
                {
                    CharacterLeft = left,
                    CharacterTop = top,
                    CharacterHeight = height
                }
            };
            await _settingsStore.UpdateAsync(updated).ConfigureAwait(true);
            _lastSavedLeft = left;
            _lastSavedTop = top;
            _lastSavedHeight = height;
        }
        catch (Exception ex)
        {
            LogPersistPlacementFailed(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Failed to persist companion window placement.")]
    private static partial void LogPersistPlacementFailed(ILogger logger, Exception exception);

    private void Avatar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDownOnAvatar = true;
        _isDragging = false;
        _mouseDownScreen = PointToScreen(e.GetPosition(this));
        // Do not capture/DragMove immediately; wait for movement to distinguish click vs drag.
        e.Handled = true;
    }

    private void Avatar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDownOnAvatar || e.LeftButton != MouseButtonState.Pressed || _isDragging)
        {
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        var dx = Math.Abs(current.X - _mouseDownScreen.X);
        var dy = Math.Abs(current.Y - _mouseDownScreen.Y);
        if (dx > 6 || dy > 6)
        {
            _isDragging = true;
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove can throw in rare states; ignore.
            }
        }
    }

    private void Avatar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mouseDownOnAvatar && !_isDragging)
        {
            ToggleChatPanel();
        }

        _mouseDownOnAvatar = false;
        _isDragging = false;
        e.Handled = true;
    }

    private void ToggleChatPanel()
    {
        EnsureChatPanel();
        if (_chatPanel!.IsVisible)
        {
            _chatPanel.Hide();
        }
        else
        {
            _chatPanel.RepositionNear(this);
            _chatPanel.Show();
        }
    }

    private void EnsureChatPanel()
    {
        if (_chatPanel is not null)
        {
            return;
        }

        _chatPanel = _serviceProvider.GetRequiredService<ChatPanelWindow>();
        _chatPanel.Owner = this;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideOverlay();
            return;
        }

        base.OnClosing(e);
    }

    public void ShowOverlay()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (!_hasValidPosition)
        {
            RestoreOrPlaceDefault(_settingsStore.Current);
        }

        ClampToCurrentMonitor(saveAfterClamp: false);
        // Preserve user foreground focus on programmatic show.
    }

    public void HideOverlay()
    {
        _chatPanel?.Hide();
        if (IsVisible)
        {
            Hide();
        }
    }

    public void ToggleOverlay()
    {
        if (IsOverlayVisible)
        {
            HideOverlay();
        }
        else
        {
            ShowOverlay();
        }
    }

    public bool IsOverlayVisible => IsVisible;

    /// <summary>Allows the window to close during application shutdown.</summary>
    public void AllowShutdownClose()
    {
        _allowClose = true;
        _chatPanel?.AllowShutdownClose();
    }

    /// <summary>Opens the settings window (resolved from DI).</summary>
    public async Task OpenSettingsAsync()
    {
        try
        {
            var window = _serviceProvider.GetRequiredService<SettingsWindow>();
            window.Owner = IsVisible ? this : null;
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

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
