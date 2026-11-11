using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using static DiskSpaceAnalyzer.Interop.EverythingNative;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer.Core;

// All SDK access is serialized on one STA with its own hidden IPC window.
// Asynchronous queries keep the UI responsive and support cancellation/timeouts.
public sealed class EverythingClient : IDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HwndSource? source;
    private DispatcherFrame? frame;
    private bool replied;
    private uint replyId;
    private bool disposed;
    public EverythingClient()
    {
        var thread = new Thread(() =>
        {
            try
            {
                source = new HwndSource(new HwndSourceParameters("DiskSpace.Everything.IPC") { Width = 0, Height = 0, WindowStyle = 0 });
                source.AddHook(OnMessage);
                ready.TrySetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
                Everything_Reset(); source.Dispose();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "Everything IPC" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x4A && frame is not null && Everything_IsQueryReply((uint)message, wParam, lParam, replyId))
        {
            replied = true; frame.Continue = false; handled = true; return 1;
        }
        return 0;
    }
    public async Task EnsureRunningAsync(IProgress<string>? progress, CancellationToken token)
    {
        var dispatcher = await ready.Task;
        string engine = FindEngine();
        await EverythingService.EnsureRunningAsync(engine, progress, token);
        if (EverythingWindowExists())
        {
            progress?.Report(I18n.T("Text.C725A9EA3DB6"));
        }
        else
        {
            progress?.Report(I18n.T("Text.4F8CA5E7283A"));
            bool bundled = engine.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase);
            string args = "-startup -first-instance";
            if (bundled)
            {
                // Writable app data; no changes to an installed Everything configuration.
                string data = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskSpaceAnalyzer", "Everything");
                Directory.CreateDirectory(data);
                string ini = System.IO.Path.Combine(data, "Everything.ini");
                if (!File.Exists(ini)) File.WriteAllText(ini, "[Everything]\r\napp_data=0\r\nrun_as_admin=0\r\nindex_size=1\r\nshow_tray_icon=0\r\n");
                args += $" -config \"{ini}\" -db \"{System.IO.Path.Combine(data, "Everything.db")}\"";
            }
            var start = new ProcessStartInfo(engine, args) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            Process.Start(start);
        }
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromMinutes(3))
        {
            token.ThrowIfCancellationRequested();
            bool loaded = await dispatcher.InvokeAsync(DatabaseReady);
            if (loaded && await ExecuteAsync(() => ProbeIndex(token), token)) return;
            progress?.Report(I18n.F("Text.7F85BA04CF26", watch.Elapsed));
            await Task.Delay(750, token);
        }
        throw new TimeoutException(I18n.T("Text.03004B886C9F"));
    }
    public static bool EverythingWindowExists() => FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION", null) != 0;
    private static bool DatabaseReady()
    {
        nint hwnd = FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION", null);
        // SDK IsDBLoaded uses an unbounded SendMessage; bound this startup probe.
        return hwnd != 0 && SendMessageTimeoutW(hwnd, 0x400, 401, 0, 2, 500, out nint result) != 0 && result != 0;
    }
    private static string FindEngine()
    {
        var candidates = new List<string>();
        using var preferences = Registry.CurrentUser.OpenSubKey(@"Software\DiskSpaceAnalyzer");
        if (preferences?.GetValue("EverythingPath") is string saved) candidates.Add(saved);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var registry = RegistryKey.OpenBaseKey(hive, view);
            using var appPath = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Everything.exe");
            if (appPath?.GetValue("") is string path) candidates.Add(path.Trim('"'));
        }
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Everything.exe");
        if (key?.GetValue("") is string registered) candidates.Add(registered);
        candidates.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "Everything.exe"));
        candidates.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Everything", "Everything.exe"));
        candidates.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "Everything", "Everything.exe"));
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException(I18n.T("Text.CE6651ACF8D6"));
    }
    private bool ProbeIndex(CancellationToken token)
    {
        try
        {
            Query("file:", token, 1, 5000);
            return Everything_GetNumResults() > 0;
        }
        catch (TimeoutException) { return false; } // Keep the overall three-minute startup deadline.
    }
    public Task ProbeAsync(CancellationToken token) => ExecuteAsync(() =>
    {
        Query("file:", token, 1);
        if (Everything_GetNumResults() == 0) throw new IOException(I18n.T("Service.EmptyIndex"));
        return true;
    }, token);
    private void Query(string search, CancellationToken token, uint max = uint.MaxValue, int timeoutMilliseconds = 180000)
    {
        token.ThrowIfCancellationRequested();
        Everything_Reset(); Everything_SetSearchW(search); Everything_SetRequestFlags(0x1 | 0x2 | 0x10);
        Everything_SetMax(max); Everything_SetReplyWindow(source!.Handle); Everything_SetReplyID(++replyId);
        replied = false; var localFrame = new DispatcherFrame(); frame = localFrame;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var clock = Stopwatch.StartNew();
        timer.Tick += (_, _) => { if (token.IsCancellationRequested || clock.Elapsed.TotalMilliseconds > timeoutMilliseconds) localFrame.Continue = false; };
        try
        {
            timer.Start();
            if (!Everything_QueryW(false)) throw new IOException(I18n.F("Text.2F3E9D51BEB0", Everything_GetLastError()));
            if (!replied) Dispatcher.PushFrame(localFrame);
            token.ThrowIfCancellationRequested();
            if (!replied) throw new TimeoutException(I18n.T("Text.356E6D1698C0"));
        }
        finally { timer.Stop(); frame = null; }
    }
    private async Task<T> ExecuteAsync<T>(Func<T> action, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(token);
        try
        {
            var dispatcher = await ready.Task;
            return await dispatcher.InvokeAsync(() =>
            {
                try { token.ThrowIfCancellationRequested(); return action(); }
                finally { Everything_Reset(); }
            });
        }
        finally { gate.Release(); }
    }
    public Task<DirectoryIndex> ScanAsync(string root, IProgress<(long Done, long Total)>? progress, CancellationToken token, AnalysisFilter? filter = null) => ExecuteAsync(() => ReadIndex(root, progress, token, filter ?? AnalysisFilter.All), token);
    private unsafe DirectoryIndex ReadIndex(string root, IProgress<(long Done, long Total)>? progress, CancellationToken token, AnalysisFilter filter)
    {
        var index = new DirectoryIndex(root);
        if (filter.IsExcluded(index.Root.Path)) return index;
        Query($"\"{index.Root.Path.TrimEnd('\\')}\\\"", token);
        uint total = Everything_GetNumResults();
        var lookup = index.Nodes.GetAlternateLookup<ReadOnlySpan<char>>();
        string prefix = index.Root.Path.TrimEnd('\\') + "\\";
        for (uint i = 0; i < total; i++)
        {
            if ((i & 8191) == 0) { token.ThrowIfCancellationRequested(); progress?.Report((i, total)); }
            var pointer = (char*)Everything_GetResultPathW(i);
            if (pointer == null) continue;
            ReadOnlySpan<char> path = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(pointer);
            if (filter.IsExcluded(path))
            {
                if (!Everything_IsFolderResult(i) && Everything_GetResultSize(i, out long excluded) && excluded > 0)
                    index.ExcludedBytes += excluded;
                continue;
            }
            if (!path.Equals(index.Root.Path.AsSpan(), StringComparison.OrdinalIgnoreCase) && !path.StartsWith(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;
            // .NET 9 alternate dictionary lookup avoids allocating a path string per file.
            if (!lookup.TryGetValue(path, out var folder)) folder = index.GetOrCreate(path.ToString());
            if (Everything_IsFolderResult(i))
            {
                string name = Marshal.PtrToStringUni(Everything_GetResultFileNameW(i)) ?? "";
                string child = System.IO.Path.Combine(folder.Path, name);
                if (!filter.IsExcluded(child)) index.GetOrCreate(child);
            }
            else
            {
                folder.OwnFiles++;
                if (Everything_GetResultSize(i, out long bytes) && bytes >= 0) folder.OwnBytes += bytes;
                else folder.OwnUnknown++;
            }
        }
        index.Aggregate(); index.Entries = index.Root.Files + index.Root.Folders; progress?.Report((total, total)); return index;
    }
    public Task<SearchResults> SearchAsync(string search, IProgress<(long Done, long Total)>? progress, CancellationToken token, AnalysisFilter? filter = null) => ExecuteAsync(() =>
    {
        filter ??= AnalysisFilter.All;
        if (string.IsNullOrWhiteSpace(search)) throw new ArgumentException(I18n.T("Text.647E0844C8DD"), nameof(search));
        // Preserve Everything syntax, including OR/grouping, paths, regex: and size:.
        Query(search, token);
        uint total = Everything_GetNumResults();
        var files = new List<ViewEntry>();
        long folders = 0;
        for (uint i = 0; i < total; i++)
        {
            if ((i & 8191) == 0) { token.ThrowIfCancellationRequested(); progress?.Report((i, total)); }
            string parent = Marshal.PtrToStringUni(Everything_GetResultPathW(i)) ?? "";
            if (filter.IsExcluded(parent)) continue;
            string name = Marshal.PtrToStringUni(Everything_GetResultFileNameW(i)) ?? "";
            string path = System.IO.Path.Combine(parent, name);
            if (filter.IsExcluded(path)) continue;
            if (Everything_IsFolderResult(i)) { folders++; continue; }
            bool known = Everything_GetResultSize(i, out long size) && size >= 0;
            files.Add(new(name, path, known ? size : 0, false, SizeKnown: known));
        }
        token.ThrowIfCancellationRequested();
        var snapshot = new SearchResults(search, files, folders);
        token.ThrowIfCancellationRequested(); progress?.Report((total, total));
        return snapshot;
    }, token);
    public Task<DirectorySlice> ReadDirectoryAsync(string path, CancellationToken token, AnalysisFilter? filter = null) => ExecuteAsync(() =>
    {
        filter ??= AnalysisFilter.All;
        path = DirectoryIndex.Normalize(path);
        if (filter.IsExcluded(path)) return new DirectorySlice(0, 0, 0, []);
        Query($"parent:\"{path}\"", token);
        long bytes = 0, files = 0, unknown = 0;
        var folders = new List<string>();
        for (uint i = 0; i < Everything_GetNumResults(); i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            string parent = Marshal.PtrToStringUni(Everything_GetResultPathW(i)) ?? "";
            if (!parent.TrimEnd('\\').Equals(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
            if (Everything_IsFolderResult(i))
            {
                string name = Marshal.PtrToStringUni(Everything_GetResultFileNameW(i)) ?? "";
                string child = System.IO.Path.Combine(parent, name);
                if (!filter.IsExcluded(child)) folders.Add(child);
            }
            else
            {
                files++;
                if (Everything_GetResultSize(i, out long size) && size >= 0) bytes += size; else unknown++;
            }
        }
        return new DirectorySlice(bytes, files, unknown, folders);
    }, token);
    public Task<List<ViewEntry>> ReadFilesAsync(string folder, CancellationToken token, AnalysisFilter? filter = null) => ExecuteAsync(() =>
    {
        filter ??= AnalysisFilter.All;
        folder = DirectoryIndex.Normalize(folder);
        if (filter.IsExcluded(folder)) return new List<ViewEntry>();
        Query($"file: parent:\"{folder}\"", token);
        var files = new List<ViewEntry>((int)Everything_GetNumResults());
        for (uint i = 0; i < Everything_GetNumResults(); i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            string path = Marshal.PtrToStringUni(Everything_GetResultPathW(i)) ?? "";
            if (!string.Equals(path.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
            string name = Marshal.PtrToStringUni(Everything_GetResultFileNameW(i)) ?? "";
            bool known = Everything_GetResultSize(i, out long size) && size >= 0;
            files.Add(new(name, System.IO.Path.Combine(path, name), Math.Max(0, size), false, SizeKnown: known));
        }
        return files;
    }, token);
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (ready.Task.IsCompletedSuccessfully) ready.Task.Result.BeginInvokeShutdown(DispatcherPriority.Background);
    }
}
