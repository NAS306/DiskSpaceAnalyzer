using I18n = DiskSpaceAnalyzer.Core.Localization;
namespace DiskSpaceAnalyzer.Core;

internal static class TreemapGrouping
{
    internal const int MaxTilesPerLevel = 1024;
    // Four by four physical pixels: smaller individual areas provide no readable detail.
    internal static List<ViewEntry> Create(IEnumerable<ViewEntry> entries, MapRect bounds, string parentPath, double pixelScale = 1, int limit = MaxTilesPerLevel)
    {
        var sorted = entries.Where(e => e.Bytes > 0).OrderByDescending(e => e.Bytes).ToList();
        if (sorted.Count <= 1) return sorted;
        double total = sorted.Sum(e => (double)e.Bytes);
        double area = Math.Max(0, bounds.Area) * pixelScale;
        int keep = 0;
        while (keep < sorted.Count && sorted[keep].Bytes / total * area >= 16 && keep < Math.Max(1, limit) - 1) keep++;
        if (keep == sorted.Count || (keep == sorted.Count - 1 && sorted.Count <= limit && sorted[^1].Bytes / total * area >= 16)) return sorted;
        long count = 0, bytes = 0;
        for (int i = keep; i < sorted.Count; i++)
        {
            bytes += sorted[i].Bytes;
            count += sorted[i].IsGroup ? sorted[i].GroupCount : 1;
        }
        sorted.RemoveRange(keep, sorted.Count - keep);
        sorted.Add(new ViewEntry(I18n.F("Text.93E5FFA013D3", count), parentPath, bytes, false) { GroupCount = count });
        sorted.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        return sorted;
    }
}
