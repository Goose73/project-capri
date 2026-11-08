using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Desktop.Services;

/// <summary>
/// Owns the system tray icon and its context menu.
/// </summary>
public sealed partial class TrayIconService : IDisposable
{
    private readonly IOverlayController _overlay;
    private readonly IServiceProvider _services;
    private readonly ILogger<TrayIconService> _logger;
    private TaskbarIcon? _icon;
    private bool _disposed;

    public TrayIconService(
        IOverlayController overlay,
        IServiceProvider services,
        ILogger<TrayIconService>? logger = null)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? NullLogger<TrayIconService>.Instance;
    }

    public void Initialize()
    {
        if (_icon is not null)
        {
            return;
        }

        var menu = new ContextMenu();

        var showHide = new MenuItem { Header = "Show/Hide Capri" };
        showHide.Click += (_, _) => SafeUi(() => _overlay.ToggleOverlay());
        menu.Items.Add(showHide);

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += async (_, _) => await OpenSettingsAsync().ConfigureAwait(false);
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => SafeUi(RequestExit);
        menu.Items.Add(exit);

        _icon = new TaskbarIcon
        {
            ToolTipText = "Capri",
            ContextMenu = menu,
            NoLeftClickDelay = true
        };

        TryApplyIcon(_icon);

        // H.NotifyIcon v2 raises tray events; LeftClickCommand is also supported in some versions.
        _icon.TrayLeftMouseDown += (_, _) => SafeUi(() => _overlay.ToggleOverlay());
        _icon.ForceCreate();
    }

    private void TryApplyIcon(TaskbarIcon icon)
    {
        try
        {
            // H.NotifyIcon's IconSource conversion is strict across image types/URI forms in v2.x.
            // Generate a real .ico file (PNG-compressed image entry) and load it from disk.
            var iconPath = EnsureTrayIconFile();
            if (string.IsNullOrWhiteSpace(iconPath) || !System.IO.File.Exists(iconPath))
            {
                return;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(iconPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            icon.IconSource = image;
        }
        catch (Exception ex)
        {
            // Icon is cosmetic; avoid crashing the app for tray icon conversion issues.
            LogApplyIconFailed(_logger, ex);
        }
    }

    private static string EnsureTrayIconFile()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Capri");
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "capri-tray.ico");
        if (System.IO.File.Exists(path))
        {
            return path;
        }

        const int size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var drawing = new GeometryDrawing(
                Brushes.DeepSkyBlue,
                new Pen(Brushes.White, 1.5),
                new EllipseGeometry(new Point(size / 2.0, size / 2.0), (size / 2.0) - 2, (size / 2.0) - 2));
            dc.DrawDrawing(drawing);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(rtb));
        using var png = new System.IO.MemoryStream();
        pngEncoder.Save(png);
        var pngBytes = png.ToArray();

        // ICO: header (6) + directory entry (16) + PNG data
        using var ico = new System.IO.MemoryStream(6 + 16 + pngBytes.Length);
        using var bw = new System.IO.BinaryWriter(ico);
        bw.Write((short)0);      // reserved
        bw.Write((short)1);      // type: icon
        bw.Write((short)1);      // count
        bw.Write((byte)size);    // width (32; 256 would be 0)
        bw.Write((byte)size);    // height
        bw.Write((byte)0);       // color count
        bw.Write((byte)0);       // reserved
        bw.Write((short)1);      // planes
        bw.Write((short)32);     // bit count
        bw.Write(pngBytes.Length);
        bw.Write(6 + 16);        // offset
        bw.Write(pngBytes);
        bw.Flush();
        System.IO.File.WriteAllBytes(path, ico.ToArray());
        return path;
    }

    private async Task OpenSettingsAsync()
    {
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (_overlay is Views.CharacterWindow characterWindow)
                {
                    await characterWindow.OpenSettingsAsync();
                }
                else
                {
                    var window = _services.GetRequiredService<Views.SettingsWindow>();
                    await window.InitializeAsync();
                    window.ShowDialog();
                }
            });
        }
        catch (Exception ex)
        {
            LogOpenSettingsFailed(_logger, ex);
        }
    }

    private static void RequestExit()
    {
        if (Application.Current is App app)
        {
            app.RequestShutdown();
        }
        else
        {
            Application.Current?.Shutdown();
        }
    }

    private void SafeUi(Action action)
    {
        try
        {
            if (Application.Current?.Dispatcher is { } dispatcher)
            {
                if (dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    dispatcher.BeginInvoke(action);
                }
            }
            else
            {
                action();
            }
        }
        catch (Exception ex)
        {
            LogTrayActionFailed(_logger, ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _icon?.Dispose();
        }
        catch (Exception ex)
        {
            LogTrayDisposeFailed(_logger, ex);
        }
        finally
        {
            _icon = null;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to open settings from tray.")]
    private static partial void LogOpenSettingsFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Tray UI action failed.")]
    private static partial void LogTrayActionFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Tray icon disposal failed.")]
    private static partial void LogTrayDisposeFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Failed to apply tray icon.")]
    private static partial void LogApplyIconFailed(ILogger logger, Exception exception);
}
