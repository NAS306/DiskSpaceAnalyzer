using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DiskSpaceAnalyzer.Core;

namespace DiskSpaceAnalyzer.Interop;

internal sealed class WindowPlacement
{
    private readonly Window window;
    private readonly double preferredMinWidth, preferredMinHeight, initialWidth, initialHeight;
    private HwndSource? source;
    private bool closed, pending;
    public WindowPlacement(Window window)
    {
        this.window = window;
        preferredMinWidth = window.MinWidth; preferredMinHeight = window.MinHeight;
        initialWidth = window.Width; initialHeight = window.Height;
        window.SourceInitialized += SourceInitialized;
        window.Loaded += Loaded;
        window.Closed += Closed;
    }
    private void SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(window).Handle;
        source = HwndSource.FromHwnd(handle); source?.AddHook(WindowMessage);
        nint monitor = GetCursorPos(out var cursor) ? MonitorFromPoint(cursor, 2) : MonitorFromWindow(handle, 2);
        Fit(center: true, monitor);
    }
    private void Loaded(object sender, RoutedEventArgs e)
    {
        window.Loaded -= Loaded;
        // Recheck after WPF completes its initial layout and any initial DPI transition.
        EnsureVisible();
    }
    public void EnsureVisible() => Fit(center: false, 0);
    private void Fit(bool center, nint preferredMonitor)
    {
        if (closed || window.WindowState != WindowState.Normal) return;
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        // Moving a newly created HWND to another monitor can synchronously change its DPI.
        // Re-read DPI and bounds once after that move; all native geometry stays in pixels.
        for (int pass = 0; pass < 2; pass++)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            nint monitor = preferredMonitor != 0 ? preferredMonitor : MonitorFromWindow(handle, 2);
            if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(handle, out var rect)) return;
            var work = info.Work.Bounds;
            if (work.Width <= 0 || work.Height <= 0) return;
            uint dpi = GetDpiForWindow(handle);
            double scale = (dpi == 0 ? 96u : dpi) / 96.0;
            // A fixed minimum larger than the monitor would otherwise defeat fitting at high DPI.
            window.MinWidth = Math.Min(preferredMinWidth, work.Width / scale);
            window.MinHeight = Math.Min(preferredMinHeight, work.Height / scale);
            var requested = center ? new WindowBounds(rect.Left, rect.Top, (int)Math.Round(initialWidth * scale), (int)Math.Round(initialHeight * scale)) : rect.Bounds;
            var target = WindowBounds.Fit(requested, work, center);
            if (target == rect.Bounds) continue;
            window.Width = target.Width / scale; window.Height = target.Height / scale;
            if (!SetWindowPos(handle, 0, target.Left, target.Top, target.Width, target.Height, 0x0004 | 0x0010)) return;
        }
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Let WPF process the message first. Do not interfere with dragging, normal resizing,
        // maximization or the application's borderless fullscreen mode.
        if ((message is 0x02E0 or 0x007E or 0x001A) && !pending && !closed)
        {
            pending = true;
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => { pending = false; EnsureVisible(); });
        }
        return 0;
    }
    private void Closed(object? sender, EventArgs e)
    {
        closed = true;
        if (source is { IsDisposed: false }) source.RemoveHook(WindowMessage);
        window.SourceInitialized -= SourceInitialized; window.Loaded -= Loaded; window.Closed -= Closed;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly WindowBounds Bounds => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
