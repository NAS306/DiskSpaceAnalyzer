using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using DiskSpaceAnalyzer.Core;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            Check(EverythingService.NeedsRepair(new(false, false, false), false), "Missing service requires repair even when an IPC database responds");
            Check(EverythingService.NeedsRepair(new(true, false, true), false), "An installed but stopped service cannot pass startup checks");
            Check(EverythingService.NeedsRepair(new(true, true, false), false), "Manual or disabled startup is repaired for the next Windows session");
            Check(EverythingService.NeedsRepair(new(true, true, true), true), "A leftover run-as-administrator setting is repaired");
            Check(!EverythingService.NeedsRepair(new(true, true, true), false), "A healthy automatic service is reused without an elevation prompt");
            var state = EverythingService.GetState();
            Check(state.Exists && state.Running && state.Automatic, "Actual indexing service is installed, running and automatic");
            Check(!EverythingService.IsAdministrator, "Integration test runs with a normal user token");
            string engine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "Everything.exe");
            Check(!EverythingService.RunAsAdministratorConfigured(engine), "Everything GUI no longer requires administrator launch");
            nint window = FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION", null);
            Check(window != 0 && SendMessageTimeoutW(window, 0x400, 403, 0, 2, 500, out nint elevated) != 0 && elevated == 0, "Actual Everything GUI runs without administrator privileges");
            if (args.Length == 0) throw new ArgumentException("Pass a self-contained DiskSpaceAnalyzer.exe path");
            string executable = Path.GetFullPath(args[0]);
            var directory = Path.GetFullPath("artifacts/everything-service-fix"); Directory.CreateDirectory(directory);
            for (int run = 0; run < 2; run++)
            {
                string report = Path.Combine(directory, $"normal-start-{run}.json");
                using var process = Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    ArgumentList = { "--check-everything", "--output", report }
                })!;
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await process.WaitForExitAsync(timeout.Token);
                Check(process.ExitCode == 0, $"Normal-user application startup {run + 1} answers an indexed file query");
                using var result = JsonDocument.Parse(await File.ReadAllTextAsync(report));
                Check(result.RootElement.GetProperty("Ready").GetBoolean() && !result.RootElement.GetProperty("Elevated").GetBoolean(), $"Startup {run + 1} succeeds without elevating the analyzer");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string name, string? title);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeoutW(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
}
