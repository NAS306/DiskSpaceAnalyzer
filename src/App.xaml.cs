using System.IO;
using System.Windows;
using DiskSpaceAnalyzer.Core;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        I18n.Initialize();
        if (e.Args.Contains("--repair-everything-service"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int code = 1;
            try
            {
                int argument = Array.IndexOf(e.Args, "--engine");
                if (argument < 0 || argument + 1 >= e.Args.Length) throw new ArgumentException("Missing Everything path");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                await EverythingService.RepairAsync(e.Args[argument + 1], timeout.Token);
                code = 0;
            }
            catch (Exception ex)
            {
                // Preserve a useful error even when this helper has no visible window.
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskSpaceAnalyzer");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "EverythingServiceRepair.log"), ex.ToString());
            }
            Shutdown(code); return;
        }
        if (e.Args.Contains("--check-everything"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int exitCode = 0;
            string report;
            try
            {
                using var client = new EverythingClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await client.EnsureRunningAsync(null, timeout.Token);
                await client.ProbeAsync(timeout.Token);
                report = System.Text.Json.JsonSerializer.Serialize(new { Ready = true, Language = I18n.Language, Service = EverythingService.GetState(), Elevated = EverythingService.IsAdministrator });
            }
            catch (Exception ex) { exitCode = 1; report = System.Text.Json.JsonSerializer.Serialize(new { Ready = false, Language = I18n.Language, Error = ex.Message }); }
            int outputIndex = Array.IndexOf(e.Args, "--output");
            if (outputIndex >= 0 && outputIndex + 1 < e.Args.Length)
                File.WriteAllText(e.Args[outputIndex + 1], report);
            Shutdown(exitCode); return;
        }
        if (e.Args.Contains("--verify") || e.Args.Contains("--benchmark"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int result = await Verification.RunAsync(e.Args);
            Shutdown(result);
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, I18n.T("Text.162C2AFACDA3"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        try { new MainWindow(StartupOptions.ParseRoot(e.Args)).Show(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, I18n.T("Text.1806DAC3A3E7"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
