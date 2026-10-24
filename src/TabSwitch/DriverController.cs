using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TabSwitch;

/// <summary>Starts/stops the two tablet drivers' user-mode apps.</summary>
public class DriverController
{
    static readonly string[] XpProcesses = { "XPPenTablet", "PentabletUIService" };
    static readonly string[] OtdProcesses = { "OpenTabletDriver.UX.Wpf", "OpenTabletDriver.Daemon" };
    const string OtdDaemon = "OpenTabletDriver.Daemon";

    readonly Func<Settings> _settings;
    public DriverController(Func<Settings> settings) => _settings = settings;
    Settings S => _settings();

    public bool IsXpRunning() => XpProcesses.Any(IsRunning);
    public bool IsOtdRunning() => IsRunning(OtdDaemon);
    static bool IsRunning(string name)
    {
        var ps = Process.GetProcessesByName(name);
        foreach (var p in ps) p.Dispose();
        return ps.Length > 0;
    }

    /// <summary>Close XP-PEN, apply the OTD preset and restart OTD.</summary>
    public void SwitchToOtd()
    {
        Kill(XpProcesses);
        Kill(OtdProcesses);
        ApplyPreset();
        StartOtd();
    }

    /// <summary>Start the XP-PEN driver app (optionally stopping OTD first).</summary>
    public void SwitchToXp()
    {
        if (S.StopOtdInXpMode) Kill(OtdProcesses);
        if (IsXpRunning()) return;
        var exe = S.XpPenExe;
        if (!File.Exists(exe)) throw new FileNotFoundException("XP-PEN executable not found", exe);
        Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        })?.Dispose();
    }

    public void EnsureOtdRunning()
    {
        if (!IsOtdRunning()) StartOtd();
    }

    void StartOtd()
    {
        var dir = S.OtdFolder;
        var daemon = Path.Combine(dir, OtdDaemon + ".exe");
        if (!File.Exists(daemon)) throw new FileNotFoundException("OpenTabletDriver daemon not found", daemon);
        Process.Start(new ProcessStartInfo(daemon) { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        if (S.LaunchOtdUi)
        {
            var ui = Path.Combine(dir, "OpenTabletDriver.UX.Wpf.exe");
            if (File.Exists(ui))
                Process.Start(new ProcessStartInfo(ui) { WorkingDirectory = dir, UseShellExecute = false })?.Dispose();
        }
    }

    // OTD is already stopped when this runs, so the daemon cannot overwrite the file.
    void ApplyPreset()
    {
        if (string.IsNullOrWhiteSpace(S.OtdPreset)) return;
        var preset = Path.Combine(S.PresetsDir, S.OtdPreset + ".json");
        if (!File.Exists(preset)) return;
        File.Copy(preset, Path.Combine(Settings.UserDataDir(S.OtdFolder), "settings.json"), overwrite: true);
    }

    /// <summary>Brings the driver's config window forward, or starts it if it isn't open.</summary>
    public void OpenConfig(Driver d)
    {
        if (d == Driver.XpPen) OpenWindow("XPPenTablet", S.XpPenExe);
        else OpenWindow("OpenTabletDriver.UX.Wpf", Path.Combine(S.OtdFolder, "OpenTabletDriver.UX.Wpf.exe"));
    }

    static void OpenWindow(string processName, string exe)
    {
        var procs = Process.GetProcessesByName(processName);
        try
        {
            foreach (var p in procs)
            {
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero || !IsWindowVisible(h)) continue;
                ShowWindow(h, SW_RESTORE);
                SetForegroundWindow(h);
                return;
            }
        }
        finally { foreach (var p in procs) p.Dispose(); }

        if (!File.Exists(exe)) throw new FileNotFoundException("Driver config not found", exe);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
    }

    const int SW_RESTORE = 9;
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

    static void Kill(IEnumerable<string> names)
    {
        foreach (var name in names)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(entireProcessTree: true); p.WaitForExit(5000); }
                catch { }
                finally { p.Dispose(); }
            }
    }
}
