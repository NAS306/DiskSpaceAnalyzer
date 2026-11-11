using Localization = DiskSpaceAnalyzer.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiskSpaceAnalyzer.Controls;
using DiskSpaceAnalyzer.Core;
using DiskSpaceAnalyzer.Interop;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

internal static class TreemapInteractionTests
{
    [STAThread]
    private static int Main()
    {
        try { Localization.SetLanguage("ko", false); Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Run()
    {
        CheckExplorerIntegration();
        CheckSearchResults();
        CheckSearchOptions();


        var index = new DirectoryIndex(@"C:\fixture");
        var parent = index.GetOrCreate(@"C:\fixture\parent");
        var deep = index.GetOrCreate(@"C:\fixture\parent\deep");
        var leaf = index.GetOrCreate(@"C:\fixture\parent\deep\leaf");
        parent.OwnBytes = 1_000_000; deep.OwnBytes = 1_000_000; leaf.OwnBytes = 2_000_000;
        index.Aggregate();
        var view = new TreemapView { PreviewDepth = 2, Entries = [new(parent.Name, parent.Path, parent.Bytes, true, Node: parent)] };
        void Render()
        {
            view.Measure(new Size(1000, 800)); view.Arrange(new Rect(0, 0, 1000, 800)); view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1000, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        }
        void Expect(Point point, string? path, string description)
        {
            string? actual = view.EntryAt(point)?.Path;
            if (actual != path) throw new Exception($"{description}: expected {path}, got {actual}");
            Console.WriteLine("PASS " + description);
        }
        Render();
        Expect(new Point(20, 8), parent.Path, "Parent header opens parent");
        Expect(new Point(25, 22), deep.Path, "Nested header opens the second level directly");
        Expect(new Point(80, 120), leaf.Path, "Deep nested tile opens the third level directly");
        Expect(new Point(950, 300), parent.Path, "Parent file group opens its containing folder");
        Expect(new Point(650, 700), deep.Path, "Nested file group opens its containing folder");
        Expect(new Point(0, 0), parent.Path, "Tiles reach the outer edge without margins");
        Expect(new Point(-1, -1), null, "Outside the drawn region has no target");
        void Hover(Point point, string name)
        {
            view.UpdateHover(point);
            if (!view.HoverToolTip.IsOpen || view.HoverToolTip.Content is not TextBlock text || !text.Text.StartsWith(name + "\n"))
                throw new Exception("Tooltip was not immediately opened/updated for " + name);
            Console.WriteLine("PASS Immediate tooltip: " + name);
        }
        Hover(new Point(80, 120), leaf.Name);
        Hover(new Point(25, 22), deep.Name);
        Hover(new Point(20, 8), parent.Name);
        view.UpdateHover(new Point(-1, -1));
        if (view.HoverToolTip.IsOpen) throw new Exception("Tooltip remains open outside tiles");
        Console.WriteLine("PASS Tooltip closes outside tiles");
        ViewEntry? contextEntry = null;
        int leftClicks = 0;
        view.EntryContextRequested += entry => contextEntry = entry;
        view.EntryClicked += _ => leftClicks++;
        view.UpdateHover(new Point(80, 120));
        view.RequestContextAt(new Point(80, 120));
        if (contextEntry?.Path != leaf.Path || view.SelectedPath != leaf.Path || leftClicks != 0 || view.HoverToolTip.IsOpen)
            throw new Exception("Nested right-click must select the precise folder, hide tooltip, and avoid navigation");
        Console.WriteLine("PASS Nested context targets exact folder without left-click navigation");
        var contextTarget = contextEntry;
        ViewEntry? revealed = null;
        var invoked = new List<EntryContextMenu.Command>();
        var contextMenu = EntryContextMenu.Create(contextTarget, (entry, command) => { revealed = entry; invoked.Add(command); });
        if (contextMenu.Items.Count != 4 || contextMenu.Items[0] is not MenuItem contextItem || (string)contextItem.Header != "파일 탐색기에 표시")
            throw new Exception("Context commands missing");
        view.RequestContextAt(new Point(20, 8));
        foreach (MenuItem item in contextMenu.Items)
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (revealed?.Path != leaf.Path) throw new Exception("Context command target changed after another selection");
        }
        if (!invoked.SequenceEqual(Enum.GetValues<EntryContextMenu.Command>())) throw new Exception("Context commands were routed incorrectly");
        Console.WriteLine("PASS All four commands retain original right-clicked path and route correctly");
        contextEntry = null; view.RequestContextAt(new Point(-1, -1));
        if (contextEntry is not null) throw new Exception("Empty space must not request an old context target");
        Console.WriteLine("PASS Empty-space right-click has no stale target");
        view.PreviewDepth = 1; view.InvalidateVisual(); Render();
        Expect(new Point(80, 120), deep.Path, "Reducing preview depth removes hidden leaf targets");
        view.PreviewDepth = 0; view.InvalidateVisual(); Render();
        Expect(new Point(80, 120), parent.Path, "One-level display targets the visible parent");
        view.Entries = [new("file.bin", @"C:\fixture\file.bin", 500, false)]; Render();
        Expect(new Point(80, 120), @"C:\fixture\file.bin", "Real file tiles remain selectable");
        Hover(new Point(80, 120), "file.bin");
        view.RequestContextAt(new Point(80, 120));
        if (contextEntry?.Path != @"C:\fixture\file.bin" || contextEntry.IsFolder || view.HoverToolTip.IsOpen)
            throw new Exception("File context did not target the actual file");
        Console.WriteLine("PASS File context targets file and closes tooltip");
        view.Entries = []; Render();
        Expect(new Point(80, 120), null, "Replacing data clears old nested targets");
        if (view.HoverToolTip.IsOpen) throw new Exception("Tooltip remains open after replacing data");
        Console.WriteLine("PASS Replacing data closes tooltip");
        view.FolderPath = @"C:\fixture";
        view.Entries = new[] { new ViewEntry("large.bin", @"C:\fixture\large.bin", 10_000_000, false) }
            .Concat(Enumerable.Range(0, 100_000).Select(i => new ViewEntry($"tiny{i}.bin", $@"C:\fixture\tiny{i}.bin", 1, false))).ToArray();
        Render();
        var group = view.EntryAt(new Point(995, 400));
        if (group?.IsGroup != true || group.GroupCount != 100_000 || group.Bytes != 100_000 || view.VisibleTileCount != 2)
            throw new Exception("100,000 tiny files were not grouped with exact totals");
        Console.WriteLine("PASS 100,000 tiny files render as one group with exact count and size");
        int builds = view.SceneBuildCount;
        Hover(new Point(100, 100), "large.bin"); Render();
        if (view.SceneBuildCount != builds) throw new Exception("Hover rebuilt the complete scene");
        Console.WriteLine("PASS Hover reuses cached drawing without sorting or rebuilding scene");
        contextEntry = null; view.ContextMenu = contextMenu;
        view.RequestContextAt(new Point(995, 400));
        if (contextEntry is not null || view.ContextMenu is not null) throw new Exception("Synthetic group exposed real filesystem actions");
        Console.WriteLine("PASS Synthetic groups do not expose filesystem actions");
        view.Entries = []; Render();
    }

    private static void CheckSearchOptions()
    {
        void Check(bool result, string message) { if (!result) throw new Exception(message); Console.WriteLine("PASS " + message); }
        var filter = new AnalysisFilter(true);
        Check(new[] { @"C:\Windows", @"c:\WINDOWS\System32\file.dll", "C:/Windows/Temp/a.zip", @"\\?\C:\Windows\file.bin" }.All(p => filter.IsExcluded(p)), "Windows exclusion includes the directory and every child, case/slash insensitive");
        Check(new[] { @"C:\Windows.old\file.bin", @"C:\Windows2", @"C:\Windows.txt", @"D:\Windows\file.bin", @"C:\Users\Windows\file.bin", @"\\server\C\Windows\file.bin", @"C:\" }.All(p => !filter.IsExcluded(p)), "Windows exclusion preserves similarly named directories, other drives and UNC paths");
        Check(!AnalysisFilter.All.IsExcluded(@"C:\Windows\System32"), "Disabled exclusion includes Windows again");
        string query = SearchQueryBuilder.Build(new("*.ZIP, .7z;zip", ">=", "1.5", "GB", @"D:\한글 자료"));
        Check(query == "file: \"D:\\한글 자료\\\" <*.zip|*.7z> size:>=1.5gb", "Builder combines deduplicated extensions, decimal size and an exact folder boundary");
        Check(SearchQueryBuilder.Build(new("*", "", "invalid", "MB")) == "file:", "Builder supports all files without an active size condition");
        Check(SearchQueryBuilder.Build(new("", "=", "0", "B", @"C:\")) == "file: \"C:\\\" size:0b", "Builder supports zero-byte files and drive-root scope");
        foreach (var invalid in new[] { new SearchQueryOptions("zip|exe"), new("zip", ">", "-1"), new("zip", ">", "NaN"), new("zip", ">", "999999999999999999999999999", "TB"), new("zip", ">", "2", "bad"), new("zip", ScopePath: "relative-folder") })
        {
            bool rejected = false; try { SearchQueryBuilder.Build(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Builder rejects invalid extension, size, unit or scope: " + invalid);
        }
    }
    private static void CheckSearchResults()
    {
        var files = new List<ViewEntry>
        {
            new("same.zip", @"D:\other\same.zip", 1000, false),
            new("same.zip", @"C:\자료\same.zip", 2000, false),
            new("zero.zip", @"C:\zero.zip", 0, false),
            new("unknown.zip", @"C:\unknown.zip", 0, false, SizeKnown: false)
        };
        var search = new SearchResults("*.zip", files, 3);
        if (search.Bytes != 3000 || search.Unknown != 1 || search.Files.Count != 4 || search.ExcludedFolders != 3 || search.Files[0].Bytes != 2000)
            throw new Exception("Search aggregation/sorting lost matched files or added folder bytes");
        Console.WriteLine("PASS Search preserves duplicate names across drives, zero/unknown size and exact file totals");
        var identical = new SearchResults("*.zip", files.ToList(), 3);
        var resized = new SearchResults("*.zip", files.Select(f => f.Path.StartsWith("D:") ? f with { Bytes = 999 } : f).ToList(), 3);
        var removed = new SearchResults("*.zip", files.Skip(1).ToList(), 3);
        if (!search.SameResults(identical) || search.SameResults(resized) || search.SameResults(removed))
            throw new Exception("Search refresh comparison missed a size/removal change");
        Console.WriteLine("PASS Search refresh detects size/removal changes and skips unchanged redraws");
        var view = new TreemapView { Entries = search.Files };
        view.Measure(new Size(1000, 800)); view.Arrange(new Rect(0, 0, 1000, 800)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1000, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var paths = new HashSet<string>();
        for (int x = 50; x < 1000; x += 100) for (int y = 50; y < 800; y += 100) if (view.EntryAt(new Point(x, y)) is { } file) paths.Add(file.Path);
        if (!paths.SetEquals(search.Files.Where(f => f.Bytes > 0).Select(f => f.Path))) throw new Exception("Flat search treemap did not retain original file paths");
        Console.WriteLine("PASS Flat search treemap uses the matched file paths without parent-folder expansion");
    }
    private static void CheckExplorerIntegration()
    {
        void Check(bool result, string description)
        {
            if (!result) throw new Exception(description);
            Console.WriteLine("PASS " + description);
        }
        string fixture = Path.Combine(Path.GetTempPath(), "DiskSpace-tests-" + Guid.NewGuid().ToString("N"));
        string registryPath = @"Software\DiskSpaceAnalyzer.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            string folder = Path.Combine(fixture, "한글 자료 & (폴더)");
            Directory.CreateDirectory(folder);
            Check(StartupOptions.ParseRoot([]) is null, "Ordinary startup does not force a root");
            Check(StartupOptions.ParseRoot(["--root", folder]) == folder, "Startup accepts spaces, Korean and shell punctuation");
            bool rejected = false;
            try { StartupOptions.ParseRoot(["--root"]); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Missing root argument is rejected");
            rejected = false;
            try { StartupOptions.ParseRoot(["--root", Path.Combine(fixture, "missing")]); } catch (DirectoryNotFoundException) { rejected = true; }
            Check(rejected, "Nonexistent startup root is rejected");
            rejected = false;
            try { StartupOptions.ParseRoot(["--root", folder, "--extra"]); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Unexpected startup arguments are rejected");

            string executable = Path.Combine(folder, "DiskSpaceAnalyzer.exe");
            File.WriteAllBytes(executable, []);
            foreach (string selectedPath in new[] { folder, folder + "\\", Path.GetPathRoot(folder)! })
            {
                foreach (string placeholder in new[] { "%1", "%V" })
                {
                    string command = ExplorerIntegration.BuildCommand(executable, placeholder).Replace(placeholder, selectedPath);
                    string[] arguments = SplitCommand(command);
                    Check(arguments.Length == 3 && arguments[0] == executable &&
                        StartupOptions.ParseRoot(arguments[1..]) == Path.TrimEndingDirectorySeparator(selectedPath),
                        $"Windows command-line round trip: {placeholder}, trailing slash={selectedPath.EndsWith('\\')}, drive root={selectedPath == Path.GetPathRoot(folder)}");
                }
            }
            using var testClasses = Registry.CurrentUser.CreateSubKey(registryPath, true);
            using (var sentinel = testClasses.CreateSubKey(@"Directory\shell\OtherApp")) sentinel.SetValue("", "preserve");
            Check(ExplorerIntegration.GetState(testClasses, executable) == (false, false), "Unregistered Explorer state is detected");
            ExplorerIntegration.Register(testClasses, executable);
            Check(ExplorerIntegration.GetState(testClasses, executable) == (true, true), "Folder and background registration both target the executable");
            using (var folderKey = testClasses.OpenSubKey(@"Directory\shell\" + ExplorerIntegration.VerbName))
                Check((string?)folderKey?.GetValue("") == ExplorerIntegration.MenuLabel && (string?)folderKey?.GetValue("MultiSelectModel") == "Single", "Explorer menu label and single-folder selection are registered");
            string movedExecutable = Path.Combine(fixture, "DiskSpaceAnalyzer.exe");
            File.WriteAllBytes(movedExecutable, []);
            Check(ExplorerIntegration.GetState(testClasses, movedExecutable) == (true, false), "Moved executable is detected as an outdated registration");
            ExplorerIntegration.Register(testClasses, movedExecutable);
            Check(ExplorerIntegration.GetState(testClasses, movedExecutable) == (true, true), "Re-register updates both commands after moving the app");
            testClasses.DeleteSubKeyTree(@"Directory\Background\shell\" + ExplorerIntegration.VerbName);
            Check(ExplorerIntegration.GetState(testClasses, movedExecutable) == (true, false), "Partially removed registration is detected");
            ExplorerIntegration.Register(testClasses, movedExecutable);
            Check(ExplorerIntegration.GetState(testClasses, movedExecutable).Current, "Registration repairs a missing Explorer entry");
            ExplorerIntegration.Unregister(testClasses);
            ExplorerIntegration.Unregister(testClasses);
            using var preserved = testClasses.OpenSubKey(@"Directory\shell\OtherApp");
            Check(ExplorerIntegration.GetState(testClasses, movedExecutable) == (false, false) && (string?)preserved?.GetValue("") == "preserve", "Repeated removal preserves unrelated Explorer commands");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryPath, false);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
        }
    }

    private static string[] SplitCommand(string command)
    {
        IntPtr pointer = CommandLineToArgvW(command, out int count);
        if (pointer == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(pointer); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
