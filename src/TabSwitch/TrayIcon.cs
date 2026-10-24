using System.Runtime.InteropServices;

namespace TabSwitch;

/// <summary>Minimal Win32 notification-area icon with a native context menu (WinUI 3 has no tray API).</summary>
public sealed class TrayIcon : IDisposable
{
    const int WM_APP_TRAY = 0x8000 + 1;
    const int WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_RBUTTONUP = 0x205, WM_NULL = 0, WM_TIMER = 0x113;
    const uint ClickTimerId = 1;
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const uint MF_STRING = 0, MF_GRAYED = 1, MF_CHECKED = 8, MF_SEPARATOR = 0x800;
    const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100;
    static readonly IntPtr HWND_MESSAGE = new(-3);

    public const int CmdOtd = 1, CmdXp = 2, CmdAuto = 3, CmdSettings = 4, CmdExit = 5;

    public event Action<int>? Command;
    /// <summary>Single click. Fires only after the double-click window passes with no second click.</summary>
    public event Action? Clicked;
    public event Action? DoubleClicked;
    /// <summary>Supplies the menu's current state: (current driver, auto on).</summary>
    public Func<(TrayState State, bool Auto, string Title)>? GetMenuState;

    readonly WndProc _proc;
    readonly IntPtr _hwnd;
    readonly uint _taskbarCreated;
    IntPtr _hIcon;
    string _tip = "TabSwitch";
    bool _ignoreNextUp;

    public TrayIcon()
    {
        _proc = WindowProc;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "TabSwitchTrayWindow"
        };
        RegisterClassEx(ref wc);
        _hwnd = CreateWindowEx(0, wc.lpszClassName, "TabSwitchTray", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
    }

    public void Show(IntPtr hIcon, string tip)
    {
        var old = _hIcon;
        _hIcon = hIcon;
        _tip = tip.Length > 127 ? tip[..127] : tip;
        var d = Data();
        if (!Shell_NotifyIcon(NIM_MODIFY, ref d)) Shell_NotifyIcon(NIM_ADD, ref d);
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    NOTIFYICONDATA Data() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _hIcon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = ""
    };

    IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APP_TRAY)
        {
            int ev = (int)(lParam.ToInt64() & 0xFFFF);
            if (ev == WM_LBUTTONUP)
            {
                // A double click arrives as UP, DBLCLK, UP. The trailing UP must not start a new single click.
                if (_ignoreNextUp) _ignoreNextUp = false;
                else SetTimer(_hwnd, new UIntPtr(ClickTimerId), GetDoubleClickTime(), IntPtr.Zero);
            }
            else if (ev == WM_LBUTTONDBLCLK)
            {
                KillTimer(_hwnd, new UIntPtr(ClickTimerId));
                _ignoreNextUp = true;
                DoubleClicked?.Invoke();
            }
            else if (ev == WM_RBUTTONUP) ShowMenu();
            return IntPtr.Zero;
        }
        if (msg == WM_TIMER && wParam == new IntPtr((int)ClickTimerId))
        {
            KillTimer(_hwnd, new UIntPtr(ClickTimerId));
            Clicked?.Invoke();
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated && _hIcon != IntPtr.Zero)
        {
            var d = Data();
            Shell_NotifyIcon(NIM_ADD, ref d);
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    void ShowMenu()
    {
        var st = GetMenuState?.Invoke() ?? (TrayState.Otd, true, "TabSwitch");
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING | MF_GRAYED, 0, st.Title);
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING | (st.State == TrayState.Otd ? MF_CHECKED : 0), CmdOtd, "Use OpenTabletDriver");
        AppendMenu(menu, MF_STRING | (st.State == TrayState.XpPen ? MF_CHECKED : 0), CmdXp, "Use XP-PEN");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING | (st.Auto ? MF_CHECKED : 0), CmdAuto, "Automatic switching");
        AppendMenu(menu, MF_STRING, CmdSettings, "Settings...");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, CmdExit, "Exit");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd); // required so the menu dismisses on outside click
        int cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.x, pt.y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        if (cmd != 0) Command?.Invoke(cmd);
    }

    public void Dispose()
    {
        var d = Data();
        Shell_NotifyIcon(NIM_DELETE, ref d);
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        DestroyWindow(_hwnd);
    }

    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr m, uint flags, nint id, string? text);
    [DllImport("user32.dll")] static extern int TrackPopupMenu(IntPtr m, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr m);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr h, UIntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr h, UIntPtr id);
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
}
