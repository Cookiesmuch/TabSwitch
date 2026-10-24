using System.Runtime.InteropServices;
using System.Text;

namespace TabSwitch;

public enum TrayState { Otd, XpPen, Switching, Error }

/// <summary>Watches the foreground app and moves the tablet between drivers.</summary>
public class Switcher : IDisposable
{
    public Settings Settings { get; private set; }
    readonly DriverController _drivers;
    readonly System.Threading.Timer _timer;
    readonly object _gate = new();

    Driver? _pending;
    DateTime _pendingSince;
    DateTime _nextAllowed = DateTime.MinValue;
    volatile bool _busy;
    int _ticking;

    public TrayState State { get; private set; } = TrayState.Otd;
    public string Detail { get; private set; } = "";
    public string ForegroundApp { get; private set; } = "";
    public event Action? Changed;

    public Switcher(Settings settings)
    {
        Settings = settings;
        _drivers = new DriverController(() => Settings);
        _timer = new System.Threading.Timer(_ => Tick(), null, 500, 400);
    }

    public void Reload(Settings s)
    {
        lock (_gate) { Settings = s; _pending = null; _nextAllowed = DateTime.MinValue; }
        Changed?.Invoke();
    }

    public Driver CurrentDriver => _drivers.IsXpRunning() ? Driver.XpPen : Driver.OpenTabletDriver;

    /// <summary>Manual switch from the tray. Turns automatic switching off so the choice sticks.</summary>
    public void SwitchNow(Driver d)
    {
        Settings.AutoSwitch = false;
        Settings.Save();
        _ = Task.Run(() => DoSwitch(d));
    }

    public void SetAuto(bool on)
    {
        Settings.AutoSwitch = on;
        Settings.Save();
        Changed?.Invoke();
    }

    /// <summary>Opens the config window of whichever driver is currently active.</summary>
    public void OpenDriverConfig()
    {
        try { _drivers.OpenConfig(CurrentDriver); }
        catch (Exception ex) { Fail(ex); }
    }

    void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            if (_busy) return;
            var s = Settings;
            var cur = CurrentDriver;
            var fg = GetForegroundExe();
            if (fg != null) ForegroundApp = fg;
            var now = DateTime.UtcNow;

            // First run: fetch OTD if it isn't in the default location yet.
            if (now >= _nextAllowed && !OtdInstaller.IsInstalled(s.OtdFolder) && OtdInstaller.IsDefault(s.OtdFolder))
            {
                DownloadOtd();
                return;
            }

            // Keep OTD alive 24/7 (unless the user opted to stop it in XP-PEN mode).
            if (now >= _nextAllowed && (cur == Driver.OpenTabletDriver || !s.StopOtdInXpMode) && !_drivers.IsOtdRunning())
            {
                try { _drivers.EnsureOtdRunning(); }
                catch (Exception ex) { Fail(ex); return; }
            }

            if (State != TrayState.Error || now >= _nextAllowed)
                SetState(cur == Driver.XpPen ? TrayState.XpPen : TrayState.Otd, "");

            if (!s.AutoSwitch || fg == null || s.IsIgnored(fg)) { _pending = null; return; }

            var want = s.Match(fg)?.Driver ?? s.DefaultDriver;
            if (want == cur) { _pending = null; return; }
            if (_pending != want) { _pending = want; _pendingSince = now; return; }
            if ((now - _pendingSince).TotalMilliseconds < s.SwitchDelayMs || now < _nextAllowed) return;
            _pending = null;
            DoSwitch(want);
        }
        catch (Exception ex) { Fail(ex); }
        finally { Interlocked.Exchange(ref _ticking, 0); }
    }

    void DoSwitch(Driver d)
    {
        lock (_gate)
        {
            _busy = true;
            SetState(TrayState.Switching, d == Driver.XpPen ? "Starting XP-PEN..." : "Restarting OpenTabletDriver...");
            try
            {
                if (d == Driver.XpPen) _drivers.SwitchToXp(); else _drivers.SwitchToOtd();
                // Give XP-PEN a moment to show up as a process before re-evaluating.
                for (int i = 0; i < 25 && d == Driver.XpPen && CurrentDriver != d; i++) Thread.Sleep(200);
                _nextAllowed = DateTime.UtcNow.AddSeconds(2);
                SetState(CurrentDriver == Driver.XpPen ? TrayState.XpPen : TrayState.Otd, "");
            }
            catch (Exception ex) { Fail(ex); }
            finally { _busy = false; }
        }
    }

    void DownloadOtd()
    {
        lock (_gate)
        {
            _busy = true;
            SetState(TrayState.Switching, "Downloading OpenTabletDriver...");
            try
            {
                OtdInstaller.InstallAsync(Settings.OtdFolder).GetAwaiter().GetResult();
                SetState(TrayState.Otd, "");
            }
            catch (Exception ex)
            {
                Fail(ex);
                _nextAllowed = DateTime.UtcNow.AddMinutes(1);
            }
            finally { _busy = false; }
        }
    }

    void Fail(Exception ex)
    {
        _nextAllowed = DateTime.UtcNow.AddSeconds(10);
        SetState(TrayState.Error, ex.Message);
    }

    void SetState(TrayState st, string detail)
    {
        if (State == st && Detail == detail) return;
        State = st; Detail = detail;
        Changed?.Invoke();
    }

    static string? GetForegroundExe()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return null;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == 0) return null;
        var proc = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (proc == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint len = (uint)sb.Capacity;
            return QueryFullProcessImageName(proc, 0, sb, ref len) ? Path.GetFileName(sb.ToString()) : null;
        }
        finally { CloseHandle(proc); }
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref uint size);

    public void Dispose() => _timer.Dispose();
}
