using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer.Interop;

internal static class ExplorerIntegration
{
    internal const string VerbName = "DiskSpaceAnalyzer.Analyze";
    internal static string MenuLabel => I18n.T("Text.23A49597A25B");
    private static readonly (string Key, string Argument)[] Targets =
    [
        (@"Directory\shell\" + VerbName, "%1"),
        (@"Directory\Background\shell\" + VerbName, "%V")
    ];

    public static string ExecutablePath => Environment.ProcessPath is { } path &&
        Path.GetFileName(path).Equals("DiskSpaceAnalyzer.exe", StringComparison.OrdinalIgnoreCase)
        ? path : throw new InvalidOperationException(I18n.T("Text.A3E06219272B"));

    // Appending \. keeps drive roots and other trailing backslashes away from the closing
    // quote: Windows command-line parsing would otherwise turn the quote into a literal.
    internal static string BuildCommand(string executable, string argument) => $"\"{executable}\" --root \"{argument}\\.\"";

    public static (bool Any, bool Current) GetState()
    {
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes");
        return classes is null ? (false, false) : GetState(classes, ExecutablePath);
    }

    internal static (bool Any, bool Current) GetState(RegistryKey classes, string executable)
    {
        bool any = false, current = true;
        foreach (var target in Targets)
        {
            using var key = classes.OpenSubKey(target.Key);
            using var command = key?.OpenSubKey("command");
            any |= key is not null;
            current &= command?.GetValue("") is string value &&
                value.Equals(BuildCommand(executable, target.Argument), StringComparison.OrdinalIgnoreCase);
        }
        return (any, current);
    }

    public static void Register()
    {
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", true);
        Register(classes, ExecutablePath);
        NotifyShell();
    }

    internal static void Register(RegistryKey classes, string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) || executable.Contains('"'))
            throw new ArgumentException(I18n.T("Text.76E84276D466"), nameof(executable));
        foreach (var target in Targets)
        {
            using var key = classes.CreateSubKey(target.Key, true);
            key.SetValue("", MenuLabel);
            key.SetValue("Icon", $"\"{executable}\",0");
            key.SetValue("MultiSelectModel", "Single");
            using var command = key.CreateSubKey("command", true);
            command.SetValue("", BuildCommand(executable, target.Argument));
        }
    }

    public static void Unregister()
    {
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true);
        if (classes is not null) Unregister(classes);
        NotifyShell();
    }

    internal static void Unregister(RegistryKey classes)
    {
        // Delete only this app's two verb keys; preserve all other Explorer commands.
        foreach (var target in Targets) classes.DeleteSubKeyTree(target.Key, false);
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
