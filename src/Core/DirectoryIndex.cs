using System.IO;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer.Core;

public sealed class DirectoryNode(string path, DirectoryNode? parent)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } n ? n : Path;
    public DirectoryNode? Parent { get; internal set; } = parent;
    public Dictionary<string, DirectoryNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public long OwnBytes, OwnFiles, OwnUnknown, Bytes, Files, Folders, Unknown;
    public void Recalculate()
    {
        Bytes = OwnBytes; Files = OwnFiles; Folders = Children.Count; Unknown = OwnUnknown;
        foreach (var child in Children.Values) { Bytes += child.Bytes; Files += child.Files; Folders += child.Folders; Unknown += child.Unknown; }
    }
}

public sealed class DirectoryIndex
{
    public static string Normalize(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string root = System.IO.Path.GetPathRoot(full)!;
        return full.Length > root.Length ? full.TrimEnd('\\') : root;
    }
    public static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    public Dictionary<string, DirectoryNode> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DirectoryNode Root { get; }
    public long Entries { get; internal set; }
    public long ExcludedBytes { get; internal set; }
    public DirectoryIndex(string root) { Root = new(Normalize(root), null); Nodes.Add(Root.Path, Root); }
    public DirectoryNode GetOrCreate(string path)
    {
        path = path.TrimEnd('\\');
        if (path.EndsWith(':')) path += "\\";
        if (Nodes.TryGetValue(path, out var existing)) return existing;
        if (!Within(path, Root.Path)) throw new InvalidOperationException(I18n.T("Text.D7A1D98FA489"));
        var pending = new Stack<string>();
        while (!Nodes.TryGetValue(path, out existing))
        {
            pending.Push(path);
            path = System.IO.Path.GetDirectoryName(path) ?? Root.Path;
        }
        while (pending.TryPop(out string? next))
        {
            var node = new DirectoryNode(next, existing);
            existing.Children.Add(next, node); Nodes.Add(next, node); existing = node;
        }
        return existing;
    }
    public void Aggregate()
    {
        // Parents are inserted before children; reverse insertion order is a postorder.
        foreach (var node in Nodes.Values.Reverse()) node.Recalculate();
    }
    public void ReplaceSubtree(string path, DirectoryIndex? replacement)
    {
        if (!Nodes.TryGetValue(path, out var previous))
        {
            if (replacement is null) return;
            GetOrCreate(path);
            previous = Nodes[path];
        }
        var parent = previous.Parent ?? throw new InvalidOperationException(I18n.T("Text.49DCC1FC22D8"));
        var stack = new Stack<DirectoryNode>(); stack.Push(previous);
        while (stack.TryPop(out var old)) { Nodes.Remove(old.Path); foreach (var c in old.Children.Values) stack.Push(c); }
        parent.Children.Remove(path);
        if (replacement is not null)
        {
            replacement.Root.Parent = parent;
            parent.Children[path] = replacement.Root;
            foreach (var entry in replacement.Nodes) Nodes[entry.Key] = entry.Value;
        }
        for (var node = parent; node is not null; node = node.Parent) node.Recalculate();
        Entries = Root.Files + Root.Folders;
    }
    public void ReconcileDirectory(string path, DirectorySlice slice, IReadOnlyList<DirectoryIndex> added)
    {
        if (!Nodes.TryGetValue(path, out var node)) return;
        var keep = new HashSet<string>(slice.Folders, StringComparer.OrdinalIgnoreCase);
        foreach (var removed in node.Children.Keys.Where(p => !keep.Contains(p)).ToArray()) ReplaceSubtree(removed, null);
        foreach (var child in added) ReplaceSubtree(child.Root.Path, child);
        node.OwnBytes = slice.Bytes; node.OwnFiles = slice.Files; node.OwnUnknown = slice.Unknown;
        for (var n = node; n is not null; n = n.Parent) n.Recalculate();
        Entries = Root.Files + Root.Folders;
    }
}

public sealed record DirectorySlice(long Bytes, long Files, long Unknown, IReadOnlyList<string> Folders);

public sealed record ViewEntry(string Name, string Path, long Bytes, bool IsFolder, long Files = 0, DirectoryNode? Node = null, bool SizeKnown = true)
{
    public long GroupCount { get; init; }
    public bool IsFreeSpace { get; init; }
    public bool IsGroup => GroupCount > 0;
    public string Icon => IsFolder ? "▣" : "▪";
    public string SizeText => SizeKnown ? Format.Bytes(Bytes) : I18n.T("Text.298859A04696");
    public string TypeText => IsFolder ? I18n.F("Text.28C65AEBCA70", Files) : System.IO.Path.GetExtension(Name).ToLowerInvariant() is { Length: > 0 } ext ? ext : I18n.T("Text.0C8354D0E51A");
    public string PercentText { get; init; } = "";
}
public static class Format
{
    public static string Bytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = value; int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{value:N0} B" : $"{size:0.##} {units[unit]}";
    }
}
