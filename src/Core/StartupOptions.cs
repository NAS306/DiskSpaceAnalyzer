using System.IO;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer.Core;

internal static class StartupOptions
{
    public static string? ParseRoot(string[] args)
    {
        if (args.Length == 0) return null;
        if (args.Length != 2 || !args[0].Equals("--root", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(args[1]))
            throw new ArgumentException(I18n.T("Text.7068896C6789"));

        string path = Path.GetFullPath(args[1]);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(I18n.F("Text.CAC8D4B3A175", path));
        return Path.TrimEndingDirectorySeparator(path);
    }
}
