using System.IO;

namespace DiskSpaceAnalyzer.Core;

public sealed record DriveCapacity
{
    public long TotalBytes { get; }
    public long FreeBytes { get; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercent => TotalBytes > 0 ? 100.0 * UsedBytes / TotalBytes : 0;
    private DriveCapacity(long total, long free) { TotalBytes = total; FreeBytes = free; }
    public static DriveCapacity Create(long total, long free, long excluded = 0)
    {
        if (total <= 0 || free < 0 || free > total || excluded < 0) throw new ArgumentOutOfRangeException(nameof(total));
        // Exclusion removes bytes from both the used and total capacity, never
        // presents excluded files as free space, and preserves actual free bytes.
        return new(total - Math.Min(excluded, total - free), free);
    }
    public static DriveCapacity? Read(string root, long excluded = 0)
    {
        try
        {
            if (!IsDriveRoot(root)) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? Create(drive.TotalSize, drive.TotalFreeSpace, excluded) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
    public static bool IsDriveRoot(string path) => path.Length == 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
    public string Label => $"{Compact(UsedBytes)}/{Compact(TotalBytes)} ({UsedPercent:0.#}%)";
    private static string Compact(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double number = value; int unit = 0;
        while (number >= 1024 && unit < units.Length - 1) { number /= 1024; unit++; }
        return number.ToString(unit > 0 && number < 10 ? "0.#" : "0") + units[unit];
    }
}
