using Localization = DiskSpaceAnalyzer.Core.Localization;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using DiskSpaceAnalyzer;
using DiskSpaceAnalyzer.Core;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    private sealed class Harness(string[] args) : Application
    {
        public int Result { get; private set; } = 1;
        protected override async void OnStartup(StartupEventArgs e)
        {
            try { await VerifyAsync(args); Result = 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { Shutdown(Result); }
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--inspect-running")
        {
            string target = Path.GetFullPath(args[1]);
            using var process = Process.GetProcessesByName("DiskSpaceAnalyzer").Single(p => string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase));
            var measurement = Inspect(process.MainWindowHandle);
            string report = JsonSerializer.Serialize(measurement, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.GetFullPath(args[2]), report); Console.WriteLine(report);
            return JsonSerializer.SerializeToElement(measurement).GetProperty("InsideWorkArea").GetBoolean() ? 0 : 1;
        }
        var app = new Harness(args) { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load(Path.Combine(Environment.CurrentDirectory, "src/App.xaml"));
        var resources = new XElement(wpf + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), document.Root!.Element(wpf + "Application.Resources")!.Elements().Select(x => new XElement(x)));
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        app.Run(); return app.Result;
    }
    private static object Inspect(Window window) => Inspect(new WindowInteropHelper(window).Handle);
    private static object Inspect(nint handle)
    {
        if (!GetWindowRect(handle, out var rect)) throw new Exception("Cannot read test window bounds");
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) throw new Exception("Cannot read test monitor work area");
        return new { rect.Left, rect.Top, rect.Right, rect.Bottom, Width = rect.Right - rect.Left, Height = rect.Bottom - rect.Top,
            Work = new { monitor.Work.Left, monitor.Work.Top, monitor.Work.Right, monitor.Work.Bottom }, Dpi = GetDpiForWindow(handle),
            InsideWorkArea = rect.Left >= monitor.Work.Left && rect.Top >= monitor.Work.Top && rect.Right <= monitor.Work.Right && rect.Bottom <= monitor.Work.Bottom };
    }
    private static async Task VerifyAsync(string[] args)
    {
        string output = Path.GetFullPath(args[0]);
        var checks = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new Exception(text); checks.Add(text); Console.WriteLine("PASS " + text); }
        bool Inside(object measurement) => JsonSerializer.SerializeToElement(measurement).GetProperty("InsideWorkArea").GetBoolean();
        foreach (var (name, work, desired) in new (string, WindowBounds, WindowBounds)[]
        {
            ("Existing 1600x852 work area", new(0, 0, 1600, 852), new(80, -34, 1440, 920)),
            ("1366x768 laptop with bottom taskbar", new(0, 0, 1366, 720), new(0, 0, 1440, 920)),
            ("150 percent DPI", new(0, 0, 1920, 1032), new(0, 0, 2160, 1380)),
            ("200 percent DPI", new(0, 0, 1920, 1032), new(0, 0, 2880, 1840)),
            ("Large monitor at 125 percent DPI", new(0, 0, 3840, 2112), new(0, 0, 1800, 1150)),
            ("Monitor left of primary", new(-1920, 0, 1920, 1040), new(0, 0, 1440, 920)),
            ("Monitor above primary", new(0, -1080, 1920, 1040), new(0, 0, 1440, 920)),
            ("Left taskbar", new(48, 0, 1552, 900), new(0, 0, 1440, 920)),
            ("Top taskbar", new(0, 48, 1600, 852), new(0, 0, 1440, 920)),
            ("Work area below design minimum", new(0, 0, 800, 480), new(0, 0, 1440, 920))
        })
        {
            var fit = WindowBounds.Fit(desired, work, true);
            Check(fit.Left >= work.Left && fit.Top >= work.Top && fit.Right <= work.Right && fit.Bottom <= work.Bottom && fit.Width == Math.Min(desired.Width, work.Width) && fit.Height == Math.Min(desired.Height, work.Height), name + " fits without hiding the title bar");
        }
        var area = new WindowBounds(0, 0, 1600, 852);
        var user = new WindowBounds(190, 85, 1000, 600);
        Check(WindowBounds.Fit(user, area, false) == user, "An already visible user position and size are preserved");
        Check(WindowBounds.Fit(new(-4000, -3000, 1440, 920), area, false) == new WindowBounds(0, 0, 1440, 852), "Disconnected-monitor coordinates are brought back to the current work area");
        var window = new MainWindow { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            object initial = Inspect(window);
            Check(Inside(initial), "Real main window is entirely inside the monitor work area at first show");
            window.Width += 1;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            object afterResize = Inspect(window);
            Check(Inside(afterResize), "Ordinary resizing preserves a visible title bar");
            var toggle = typeof(MainWindow).GetMethod("ToggleFullScreen", BindingFlags.Instance | BindingFlags.NonPublic)!;
            toggle.Invoke(window, null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var fullscreen = Inspect(window);
            Check(window.WindowState == WindowState.Maximized && window.WindowStyle == WindowStyle.None, "Work-area fitting leaves borderless fullscreen intact");
            toggle.Invoke(window, null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var restored = Inspect(window);
            Check(window.WindowState == WindowState.Normal && window.WindowStyle == WindowStyle.SingleBorderWindow && Inside(restored), "Leaving fullscreen restores an accessible normal window");
            window.WindowState = WindowState.Maximized;
            toggle.Invoke(window, null); toggle.Invoke(window, null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(window.WindowState == WindowState.Maximized, "Fullscreen also restores a previously maximized window");
            var report = new { Passed = true, Checks = checks, Initial = initial, AfterResize = afterResize, Fullscreen = fullscreen, Restored = restored };
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(File.ReadAllText(output));
        }
        finally { window.Close(); }
    }
}
