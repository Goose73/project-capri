using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Capri.Desktop.Interop;

internal static class MonitorWorkArea
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    public static Rect GetWorkAreaDip(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var hwnd = window.IsLoaded ? new WindowInteropHelper(window).Handle : IntPtr.Zero;
            if (hwnd != IntPtr.Zero)
            {
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (TryGetMonitorRectDip(hwnd, window, monitor, out var rect))
                {
                    return rect;
                }
            }

            var source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget is { } target)
            {
                var centerDip = new Point(window.Left + Math.Max(window.Width, 1) / 2, window.Top + Math.Max(window.Height, 1) / 2);
                var centerPx = target.TransformToDevice.Transform(centerDip);
                var pt = new POINT { X = (int)Math.Round(centerPx.X), Y = (int)Math.Round(centerPx.Y) };
                var monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                if (TryGetMonitorRectDip(hwnd, window, monitor, out var rect))
                {
                    return rect;
                }
            }
        }
        catch
        {
            // Fall back to primary work area.
        }

        return SystemParameters.WorkArea;
    }

    private static bool TryGetMonitorRectDip(IntPtr hwnd, Visual visual, IntPtr monitor, out Rect rect)
    {
        rect = default;
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        rect = DeviceRectToDip(hwnd, visual, info.rcWork);
        return rect.Width > 0 && rect.Height > 0;
    }

    private static Rect DeviceRectToDip(IntPtr hwnd, Visual visual, RECT rect)
    {
        try
        {
            var source = hwnd != IntPtr.Zero ? HwndSource.FromHwnd(hwnd) : PresentationSource.FromVisual(visual);
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

        var dpi = VisualTreeHelper.GetDpi(visual);
        var dx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        var dy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;
        return new Rect(rect.Left / dx, rect.Top / dy, (rect.Right - rect.Left) / dx, (rect.Bottom - rect.Top) / dy);
    }

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
