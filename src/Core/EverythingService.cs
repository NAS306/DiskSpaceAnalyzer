using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using static DiskSpaceAnalyzer.Interop.EverythingNative;

namespace DiskSpaceAnalyzer.Core;

public sealed record EverythingServiceState(bool Exists, bool Running, bool Automatic);

// Indexing privileges belong to the service. The analyzer and the Everything GUI
// continue to run with the user's normal token after the one-time UAC repair.
public static class EverythingService
{
    private static readonly SemaphoreSlim RepairGate = new(1);
    private const uint NoChange = uint.MaxValue;
    public static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static EverythingServiceState GetState()
    {
        using var manager = OpenSCManagerW(null, null, 1);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var service = OpenServiceW(manager, "Everything", 4);
        if (service.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 1060) return new(false, false, false);
            throw new Win32Exception(error);
        }
        if (!QueryServiceStatus(service, out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Everything");
        return new(true, status.CurrentState == 4, key?.GetValue("Start") is int start && start == 2);
    }

    public static bool NeedsRepair(EverythingServiceState state, bool runAsAdministrator) =>
        !state.Exists || !state.Running || !state.Automatic || runAsAdministrator;

    public static bool RunAsAdministratorConfigured(string engine)
    {
        string ini = Path.Combine(Path.GetDirectoryName(engine)!, "Everything.ini");
        var value = new StringBuilder(32);
        GetPrivateProfileStringW("Everything", "run_as_admin", "0", value, value.Capacity, ini);
        return value.ToString().Trim() == "1";
    }

    public static async Task EnsureRunningAsync(string engine, IProgress<string>? progress, CancellationToken token)
    {
        await RepairGate.WaitAsync(token);
        try
        {
            if (!NeedsRepair(GetState(), RunAsAdministratorConfigured(engine))) return;
            progress?.Report(Localization.T("Service.Repairing"));
            token.ThrowIfCancellationRequested();
            if (IsAdministrator) await RepairAsync(engine, token);
            else
            {
                string executable = Environment.ProcessPath ?? throw new IOException(Localization.T("Service.RepairFailed"));
                if (!Path.GetFileName(executable).Equals("DiskSpaceAnalyzer.exe", StringComparison.OrdinalIgnoreCase))
                    throw new IOException(Localization.T("Service.RepairFailed"));
                var start = new ProcessStartInfo(executable)
                {
                    UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = "--repair-everything-service --engine \"" + engine.Replace("\"", "") + "\""
                };
                try
                {
                    using var helper = Process.Start(start) ?? throw new IOException(Localization.T("Service.RepairFailed"));
                    await helper.WaitForExitAsync(token);
                    if (helper.ExitCode != 0) throw new IOException(Localization.T("Service.RepairFailed"));
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                { throw new IOException(Localization.T("Service.ApprovalRequired"), ex); }
            }
            var state = GetState();
            if (!state.Running || !state.Automatic || RunAsAdministratorConfigured(engine))
                throw new IOException(Localization.T("Service.RepairFailed"));
        }
        finally { RepairGate.Release(); }
    }

    public static async Task RepairAsync(string engine, CancellationToken token)
    {
        if (!IsAdministrator) throw new UnauthorizedAccessException(Localization.T("Service.ApprovalRequired"));
        engine = Path.GetFullPath(engine);
        var info = FileVersionInfo.GetVersionInfo(engine);
        if (!Path.GetFileName(engine).Equals("Everything.exe", StringComparison.OrdinalIgnoreCase) || info.ProductName != "Everything" || info.CompanyName != "voidtools")
            throw new IOException(Localization.T("Service.RepairFailed"));

        // Restart the default instance only when repairing its indexing backend.
        // A pre-existing unprivileged process otherwise keeps its empty database.
        for (int attempt = 0; attempt < 3 && FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION", null) != 0; attempt++)
        {
            await RunEngineAsync(engine, "-exit", token);
            var clock = Stopwatch.StartNew();
            while (FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION", null) != 0 && clock.Elapsed < TimeSpan.FromSeconds(3))
                await Task.Delay(100, token);
        }
        if (FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION", null) != 0)
            throw new IOException(Localization.T("Service.RestartFailed"));

        if (!GetState().Exists) await RunEngineAsync(engine, "-install-service", token);
        using var manager = OpenSCManagerW(null, null, 1);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var service = OpenServiceW(manager, "Everything", 4 | 2 | 0x10);
        if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!ChangeServiceConfigW(service, NoChange, 2, NoChange, null, null, 0, null, null, null, null))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!QueryServiceStatus(service, out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (status.CurrentState != 4 && !StartServiceW(service, 0, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 1056) throw new Win32Exception(error);
        }
        var wait = Stopwatch.StartNew();
        while (!GetState().Running && wait.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(200, token);
        if (!GetState().Running) throw new IOException(Localization.T("Service.RepairFailed"));

        // The global switch can outlive uninstall/reinstall. Back it up before
        // changing it, so a subsequent normal launch does not elevate the GUI.
        if (RunAsAdministratorConfigured(engine))
        {
            string ini = Path.Combine(Path.GetDirectoryName(engine)!, "Everything.ini");
            File.Copy(ini, Path.Combine(Path.GetDirectoryName(engine)!, "Everything_BU.ini"), true);
            if (!WritePrivateProfileStringW("Everything", "run_as_admin", "0", ini))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static async Task RunEngineAsync(string engine, string arguments, CancellationToken token)
    {
        using var command = Process.Start(new ProcessStartInfo(engine, arguments)
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })
            ?? throw new IOException(Localization.T("Service.RepairFailed"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await command.WaitForExitAsync(timeout.Token);
        if (command.ExitCode != 0) throw new IOException(Localization.T("Service.RepairFailed"));
    }

    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus
    { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ServiceHandle OpenSCManagerW(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ServiceHandle OpenServiceW(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatus(ServiceHandle service, out ServiceStatus status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ChangeServiceConfigW(ServiceHandle service, uint type, uint start, uint error, string? path, string? group, nint tag, string? dependencies, string? account, string? password, string? displayName);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartServiceW(ServiceHandle service, uint count, nint arguments);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetPrivateProfileStringW(string section, string key, string fallback, StringBuilder value, int size, string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WritePrivateProfileStringW(string section, string key, string value, string path);
}
