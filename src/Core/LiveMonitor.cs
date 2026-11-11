using System.Collections.Concurrent;
using System.IO;

namespace DiskSpaceAnalyzer.Core;

public sealed class LiveMonitor : IDisposable
{
    private FileSystemWatcher? watcher;
    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly string root;
    private volatile bool overflow;
    private long lastEvent;
    private long firstEvent;
    public bool IsWatching => watcher is not null;
    public bool NeedsFullRefresh => overflow;
    public LiveMonitor(string root)
    {
        this.root = DirectoryIndex.Normalize(root);
        try
        {
            watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite, InternalBufferSize = 64 * 1024 };
            watcher.Created += Changed; watcher.Deleted += Changed; watcher.Changed += Changed;
            watcher.Renamed += (_, e) => { QueueParent(e.OldFullPath); QueueParent(e.FullPath); };
            watcher.Error += (_, _) => overflow = true;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { watcher?.Dispose(); watcher = null; overflow = true; }
    }
    private void Changed(object sender, FileSystemEventArgs e) => QueueParent(e.FullPath);
    private void QueueParent(string path)
    {
        string? parent = System.IO.Path.GetDirectoryName(path);
        if (parent is not null && DirectoryIndex.Within(parent, root)) pending[parent] = 0;
        Interlocked.Exchange(ref lastEvent, Environment.TickCount64);
        Interlocked.CompareExchange(ref firstEvent, Environment.TickCount64, 0);
    }
    public string[] Drain(bool forceFull = false)
    {
        if (forceFull || overflow) { overflow = false; pending.Clear(); Interlocked.Exchange(ref firstEvent, 0); return [root]; }
        // Allow Everything's index to settle and collapse bursts of filesystem events.
        if (Environment.TickCount64 - Interlocked.Read(ref lastEvent) < 2500 && Environment.TickCount64 - Interlocked.Read(ref firstEvent) < 5000) return [];
        var paths = pending.Keys.OrderBy(p => p.Length).Take(128).ToArray();
        var selected = new List<string>();
        foreach (string path in paths)
        {
            pending.TryRemove(path, out _);
            selected.Add(path);
        }
        Interlocked.Exchange(ref firstEvent, pending.IsEmpty ? 0 : Environment.TickCount64);
        return selected.ToArray();
    }
    public void Retry(IEnumerable<string> paths) { foreach (string path in paths) pending[path] = 0; Interlocked.Exchange(ref lastEvent, Environment.TickCount64); Interlocked.CompareExchange(ref firstEvent, Environment.TickCount64, 0); }
    public void Dispose() { watcher?.Dispose(); watcher = null; }
}
