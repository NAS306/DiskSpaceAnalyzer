namespace DiskSpaceAnalyzer.Core;

public readonly record struct MapRect(double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
}
public readonly record struct MapTile<T>(T Item, MapRect Bounds);

public static class TreemapLayout
{
    // Squarified treemap. Input is already sorted descending; zero sizes have no area.
    public static List<MapTile<T>> Create<T>(IReadOnlyList<T> items, Func<T, long> weight, MapRect bounds)
    {
        var result = new List<MapTile<T>>(items.Count);
        if (bounds.Width <= 0 || bounds.Height <= 0 || items.Count == 0) return result;
        double total = 0;
        foreach (var item in items) total += Math.Max(0, weight(item));
        if (total == 0) return result;
        double scale = bounds.Area / total;
        int start = 0;
        while (start < items.Count)
        {
            if (weight(items[start]) <= 0) { start++; continue; }
            double side = Math.Min(bounds.Width, bounds.Height);
            if (side < 0.000001) break;
            int end = start + 1;
            double sum = weight(items[start]) * scale;
            double max = sum, min = sum;
            while (end < items.Count && weight(items[end]) > 0)
            {
                double next = weight(items[end]) * scale;
                double nextMin = Math.Min(min, next), nextMax = Math.Max(max, next);
                if (Worst(sum + next, nextMin, nextMax, side) > Worst(sum, min, max, side)) break;
                sum += next; min = nextMin; max = nextMax; end++;
            }
            bool vertical = bounds.Width >= bounds.Height;
            double thickness = sum / side, offset = 0;
            for (int i = start; i < end; i++)
            {
                double span = i == end - 1 ? side - offset : weight(items[i]) * scale / thickness;
                var rect = vertical
                    ? new MapRect(bounds.X, bounds.Y + offset, Math.Min(thickness, bounds.Width), Math.Max(0, span))
                    : new MapRect(bounds.X + offset, bounds.Y, Math.Max(0, span), Math.Min(thickness, bounds.Height));
                result.Add(new(items[i], rect)); offset += span;
            }
            bounds = vertical
                ? new(bounds.X + thickness, bounds.Y, Math.Max(0, bounds.Width - thickness), bounds.Height)
                : new(bounds.X, bounds.Y + thickness, bounds.Width, Math.Max(0, bounds.Height - thickness));
            start = end;
        }
        return result;
    }
    private static double Worst(double sum, double min, double max, double side)
    {
        double squared = sum * sum, sideSquared = side * side;
        return Math.Max(sideSquared * max / squared, squared / (sideSquared * min));
    }
}
