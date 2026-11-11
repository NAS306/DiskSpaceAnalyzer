using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
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
    private sealed class Sample : IFormattable
    {
        public string ToString(string? format, IFormatProvider? provider) => "sample";
        public override string ToString() => "sample";
    }
    [STAThread] private static int Main()
    {
        try { Run(); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static void Run()
    {
        var english = XDocument.Load("src/Resources/Strings.resx").Root!.Elements("data").ToDictionary(x => (string)x.Attribute("name")!, x => x.Element("value")!.Value);
        var korean = XDocument.Load("src/Resources/Strings.ko.resx").Root!.Elements("data").ToDictionary(x => (string)x.Attribute("name")!, x => x.Element("value")!.Value);
        Check(english.Count > 0 && english.Keys.Order().SequenceEqual(korean.Keys.Order()), "All interface and help resources have matching Korean and English keys");
        Check(english.Values.All(x => !Regex.IsMatch(x, "[가-힣]")), "English resources contain no untranslated Korean text");
        foreach (string language in new[] { "ko", "en" })
        {
            L.SetLanguage(language, false);
            var expected = language == "ko" ? korean : english;
            foreach (var pair in expected)
            {
                CheckValue(L.T(pair.Key).Replace("\r\n", "\n") == pair.Value.Replace("\r\n", "\n"), pair.Key + " satellite resource lookup: actual=" + L.T(pair.Key) + " expected=" + pair.Value);
                var markers = Regex.Matches(pair.Value, @"(?<!\{)\{(\d+)(?:[^{}]*)\}");
                int count = markers.Count == 0 ? 0 : markers.Max(x => int.Parse(x.Groups[1].Value)) + 1;
                _ = L.F(pair.Key, Enumerable.Range(0, count).Select(_ => (object)new Sample()).ToArray());
                var other = (language == "ko" ? english : korean)[pair.Key];
                CheckValue(markers.Select(x => x.Groups[1].Value).Order().SequenceEqual(Regex.Matches(other, @"(?<!\{)\{(\d+)(?:[^{}]*)\}").Select(x => x.Groups[1].Value).Order()), pair.Key + " placeholders");
            }
            Check(L.F("Text.7F85BA04CF26", TimeSpan.FromSeconds(61)).Contains("01:01"), language + " startup elapsed time formats correctly");
            Check(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == language, language + " applies the selected UI culture");
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load("src/App.xaml");
        var resources = new XElement(wpf + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), document.Root!.Element(wpf + "Application.Resources")!.Elements().Select(x => new XElement(x)));
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        var window = new MainWindow();
        var field = typeof(MainWindow).GetField("client", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var index = new DirectoryIndex(@"C:\사용자\Documents");
        var node = index.GetOrCreate(@"C:\사용자\Documents\폴더 Korean filename"); node.OwnBytes = 8000; node.OwnFiles = 2; index.Aggregate();
        void Set(string name, object value) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, value);
        Set("index", index); Set("current", index.Root); Set("entries", new List<ViewEntry> { new(node.Name, node.Path, node.Bytes, true, node.Files, node) });
        typeof(MainWindow).GetMethod("ApplyFilter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        foreach (string language in new[] { "ko", "en", "ko" })
        {
            L.SetLanguage(language, false);
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(((Button)window.FindName("CancelButton")).Content as string == L.T("Text.BE876433993A"), language + " live XAML button translation");
            Check(((Button)window.FindName("LanguageButton")).ToolTip as string == L.T("Language.Tooltip"), language + " live help translation");
            Check(((Button)window.FindName("DetailsToggleButton")).Content as string == L.T("Text.856901810098"), language + " programmatic control refresh");
            Check(((TreemapView)window.FindName("Treemap")).Entries.Single().Path == node.Path && window.Title.Contains(index.Root.Path), language + " switching preserves user filenames, root and treemap data");
            Render(window, "main-" + language);
            var search = new AdvancedSearchWindow(null, true);
            Check(search.Title == L.T("Text.FD5B83F7DA1D"), language + " advanced query builder title");
            Render(search, "advanced-" + language); search.Close();
        }
        ((IDisposable)field.GetValue(window)!).Dispose(); window.Close(); app.Shutdown();
        Check(true, "Bilingual main window, query builder render successfully");
    }
    private static void CheckValue(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Render(Window window, string name)
    {
        var element = (FrameworkElement)window.Content;
        double width = name.StartsWith("main-") ? 1440 : double.IsNaN(window.Width) ? 700 : window.Width;
        element.Measure(new Size(width, 860));
        double height = name.StartsWith("main-") ? 860 : Math.Min(860, Math.Max(300, element.DesiredSize.Height));
        element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts/localization"); using var output = File.Create("artifacts/localization/" + name + ".png"); encoder.Save(output);
        if (name.StartsWith("main-"))
        {
            var treemap = (TreemapView)window.FindName("Treemap");
            Check(treemap.ActualHeight > 700, name + " keeps the dense treemap area");
        }
    }
}
