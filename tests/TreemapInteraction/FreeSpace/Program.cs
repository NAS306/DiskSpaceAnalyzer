using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using DiskSpaceAnalyzer;
using DiskSpaceAnalyzer.Controls;
using DiskSpaceAnalyzer.Core;
using L = DiskSpaceAnalyzer.Core.Localization;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread] private static int Main(string[] args)
    {
        try { if (args.Contains("--verify-exclusion")) VerifyExclusion(); else Run(); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void VerifyExclusion()
    {
        using var client = new EverythingClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        client.EnsureRunningAsync(null, timeout.Token).GetAwaiter().GetResult();
        var filtered = client.ScanAsync(@"C:\", null, timeout.Token, new AnalysisFilter(true)).GetAwaiter().GetResult();
        var windows = client.ScanAsync(@"C:\Windows", null, timeout.Token).GetAwaiter().GetResult();
        Check(filtered.ExcludedBytes > 0 && Math.Abs(filtered.ExcludedBytes - windows.Root.Bytes) < 10 * 1024 * 1024, "Actual excluded byte total matches the Windows subtree within live-index changes");
        var raw = DriveCapacity.Read(@"C:\")!;
        var adjusted = DriveCapacity.Read(@"C:\", filtered.ExcludedBytes)!;
        Check(adjusted.TotalBytes == raw.TotalBytes - Math.Min(filtered.ExcludedBytes, raw.UsedBytes), "Actual Windows exclusion is removed from the capacity denominator");
        Directory.CreateDirectory("artifacts/free-space");
        File.WriteAllText("artifacts/free-space/exclusion.json", System.Text.Json.JsonSerializer.Serialize(new { Passed = true, filtered.ExcludedBytes, WindowsBytes = windows.Root.Bytes, Raw = raw, Adjusted = adjusted }));
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Run()
    {
        L.SetLanguage("ko", false);
        Check(DriveCapacity.Create(475L << 30, 76L << 30).Label == "399GB/475GB (84%)", "Capacity displays used/total and usage percentage from unrounded bytes");
        var excluded = DriveCapacity.Create(1000, 200, 300);
        Check(excluded.UsedBytes == 500 && excluded.TotalBytes == 700 && excluded.FreeBytes == 200, "Exclusion subtracts from used and total without turning excluded bytes into free space");
        Check(DriveCapacity.Create(1000, 200, 2000).UsedBytes == 0, "Logical excluded sizes exceeding physical usage remain nonnegative");
        Check(!DriveCapacity.IsDriveRoot(@"C:\folder") && !DriveCapacity.IsDriveRoot(@"\\server\share\") && DriveCapacity.IsDriveRoot(@"C:\"), "Only real drive roots have a drive free-space view");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load("src/App.xaml");
        var resources = new XElement(wpf + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), document.Root!.Element(wpf + "Application.Resources")!.Elements().Select(x => new XElement(x)));
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        var index = new DirectoryIndex(@"C:\");
        var parent = index.GetOrCreate(@"C:\Folder A"); parent.OwnBytes = 500; parent.OwnFiles = 1;
        var child = index.GetOrCreate(@"C:\Folder A\Nested"); child.OwnBytes = 250; child.OwnFiles = 1;
        index.Aggregate();
        parent.OwnBytes += 50; index.Aggregate();
        var view = new TreemapView { Entries = [new(parent.Name, parent.Path, parent.Bytes, true, parent.Files, parent)], FolderPath = @"C:\", Capacity = DriveCapacity.Create(1000, 250) };
        RenderTargetBitmap Render() { view.Measure(new Size(1000, 600)); view.Arrange(new Rect(0, 0, 1000, 600)); view.UpdateLayout(); var bitmap = new RenderTargetBitmap(1000, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view); return bitmap; }
        ViewEntry? At(double x, double y) => (ViewEntry?)Call(view, "EntryAt", new Point(x, y));
        var image = Render();
        Check(At(749, 300)?.IsFreeSpace == false && At(751, 300)?.IsFreeSpace == true, "Free area occupies exactly the free/total share of the treemap");
        Check(At(100, 8)?.Path == parent.Path && At(700, 30)?.Path == child.Path, "Nested folders keep their direct navigation targets next to free space");
        Call(view, "UpdateHover", new Point(900, 300));
        var tip = (ToolTip)view.GetType().GetProperty("HoverToolTip", Private)!.GetValue(view)!;
        Check(tip.IsOpen && ((TextBlock)tip.Content).Text.StartsWith("여유 공간\n") && !((TextBlock)tip.Content).Text.Contains("파일 열기"), "Free space has an immediate explanatory tooltip");
        int context = 0; view.EntryContextRequested += _ => context++;
        Call(view, "RequestContextAt", new Point(900, 300));
        Check(context == 0 && view.ContextMenu is null && view.SelectedPath is null, "Free-space right-click never exposes filesystem actions or selects a fake path");
        var pixels = new byte[1000 * 600 * 4]; image.CopyPixels(pixels, 4000, 0); int pixel = (300 * 1000 + 900) * 4;
        Check(pixels[pixel] == 112 && pixels[pixel + 1] == 112 && pixels[pixel + 2] == 112, "Free space stays neutral gray in the normal treemap");
        view.Capacity = null; Render();
        Check(At(900, 300)?.IsFreeSpace == false, "Disabling free space restores the entire treemap to files and folders");

        view.Entries = Enumerable.Range(0, 100_000).Select(i => new ViewEntry($"tiny{i}", $@"C:\tiny{i}", 1, false)).ToArray(); view.Capacity = DriveCapacity.Create(1000, 250); Render();
        Check(At(900, 300)?.IsFreeSpace == true && view.Entries.Sum(e => e.Bytes) == 100_000, "Tiny-item grouping preserves free space and all file totals");
        view.Entries = []; view.Capacity = DriveCapacity.Create(1000, 1000); Render();
        Check(At(1, 1)?.IsFreeSpace == true && At(999, 599)?.IsFreeSpace == true, "An empty drive renders entirely as free space");
        view.Entries = [new("full.bin", @"C:\full.bin", 1000, false)]; view.Capacity = DriveCapacity.Create(1000, 0); Render();
        Check(At(900, 300)?.IsFreeSpace == false, "A full drive has no phantom free-space tile");

        var window = new MainWindow();
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
        Set("index", index); Set("current", index.Root); Set("entries", new List<ViewEntry> { new(parent.Name, parent.Path, parent.Bytes, true, parent.Files, parent) });
        ((Border)window.FindName("EmptyOverlay")).Visibility = Visibility.Collapsed;
        var toggle = (CheckBox)window.FindName("FreeSpaceToggle");
        var map = (TreemapView)window.FindName("Treemap");
        Call(window, "ApplyFilter");
        Check(toggle.IsEnabled && toggle.IsChecked == true && map.Capacity is not null, "Main window defaults free space ON and shows it at the drive root");
        toggle.IsChecked = false;
        Check(map.Capacity is null, "The default-on free-space view can still be switched OFF");
        toggle.IsChecked = true;
        var actual = DriveCapacity.Read(@"C:\")!;
        Check(map.Capacity is not null && ((TextBlock)window.FindName("SizeStat")).Text == actual.Label, "Drive root shows Windows used/total capacity and enables gray free space");
        foreach (string language in new[] { "ko", "en" })
        {
            L.SetLanguage(language, false); app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check((string)toggle.Content == L.T("Space.ShowFree") && ((TextBlock)window.FindName("SizeStat")).ToolTip.ToString()!.Contains(actual.FreeBytes > 0 ? Format.Bytes(map.Capacity!.FreeBytes) : "0 B"), language + " capacity labels and help are translated");
            var root = (FrameworkElement)window.Content; root.Measure(new Size(1440, 860)); root.Arrange(new Rect(0, 0, 1440, 860)); root.UpdateLayout();
            var origin = toggle.TranslatePoint(new Point(), root);
            Check(origin.X >= 0 && origin.X + toggle.ActualWidth <= 1440 && map.ActualHeight > 700, language + " compact controls fit and keep the large treemap");
            var bitmap = new RenderTargetBitmap(1440, 860, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            Directory.CreateDirectory("artifacts/free-space"); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create("artifacts/free-space/main-" + language + ".png"); encoder.Save(output);
        }
        Set("current", child); Call(window, "ApplyFilter");
        Check(!toggle.IsEnabled && toggle.IsChecked == true && map.Capacity is null, "Folder navigation hides drive free space but retains the toggle choice");
        Set("current", index.Root); Call(window, "ApplyFilter");
        Check(toggle.IsEnabled && map.Capacity is not null, "Returning to the drive root restores free space");
        ((TextBox)window.FindName("FilterBox")).Text = "Nested"; Call(window, "ApplyFilter");
        Check(!toggle.IsEnabled && map.Capacity is null, "Name filtering hides free space rather than mixing it into matching files");
        ((TextBox)window.FindName("FilterBox")).Text = "";
        Set("searchResult", new SearchResults("*.zip", [new("test.zip", @"C:\test.zip", 42, false)], 0)); Call(window, "ApplyFilter");
        Check(!toggle.IsEnabled && map.Capacity is null && ((TextBlock)window.FindName("SizeStat")).Text == "42 B", "Search results preserve their matched size and never add drive free space");
        window.Close(); app.Shutdown();
    }
}
