namespace DiskSpaceAnalyzer.Core;

public sealed record AnalysisFilter(bool ExcludeWindows = false)
{
    public static AnalysisFilter All { get; } = new();
    public bool IsExcluded(ReadOnlySpan<char> path)
    {
        if (!ExcludeWindows) return false;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        // Check the directory boundary; Windows.old and D:\Windows remain included.
        return path.Length >= 10 && (path[0] is 'C' or 'c') && path[1] == ':' &&
            (path[2] is '\\' or '/') && path.Slice(3, 7).Equals("Windows", StringComparison.OrdinalIgnoreCase) &&
            (path.Length == 10 || path[10] is '\\' or '/');
    }
}
