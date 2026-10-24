using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace TabSwitch;

public partial class App : Application
{
    Mutex? _single;
    Settings _settings = null!;
    Switcher _switcher = null!;
    TrayIcon _tray = null!;
    DispatcherQueue _ui = null!;
    SettingsWindow? _window;

    public App()
    {
        InitializeComponent();
        // Tray app: closing the settings window must not end the process.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _single = new Mutex(true, @"Local\TabSwitch.SingleInstance", out bool first);
        if (!first) { Environment.Exit(0); return; }

        _ui = DispatcherQueue.GetForCurrentThread();
        _settings = Settings.Load();
        _switcher = new Switcher(_settings);

        _tray = new TrayIcon
        {
            GetMenuState = () => (_switcher.State, _switcher.Settings.AutoSwitch, Title())
        };
        _tray.Command += OnCommand;
        _tray.Clicked += _switcher.OpenDriverConfig;
        _tray.DoubleClicked += OpenSettings;
        _switcher.Changed += () => _ui.TryEnqueue(RefreshTray);
        RefreshTray();

        if (Environment.GetCommandLineArgs().Contains("--settings")) OpenSettings();
    }

    string Title() => _switcher.State switch
    {
        TrayState.Otd => "OpenTabletDriver active",
        TrayState.XpPen => "XP-PEN active",
        TrayState.Switching => _switcher.Detail,
        _ => "Error: " + _switcher.Detail
    };

    void RefreshTray()
    {
        bool auto = _switcher.Settings.AutoSwitch;
        var tip = "TabSwitch - " + Title() + (auto ? "" : " (auto off)");
        _tray.Show(IconFactory.CreateHIcon(_switcher.State, auto), tip);
    }

    void OnCommand(int cmd)
    {
        switch (cmd)
        {
            case TrayIcon.CmdOtd: _switcher.SwitchNow(Driver.OpenTabletDriver); break;
            case TrayIcon.CmdXp: _switcher.SwitchNow(Driver.XpPen); break;
            case TrayIcon.CmdAuto: _switcher.SetAuto(!_switcher.Settings.AutoSwitch); break;
            case TrayIcon.CmdSettings: OpenSettings(); break;
            case TrayIcon.CmdExit:
                _switcher.Dispose();
                _tray.Dispose();
                Environment.Exit(0);
                break;
        }
    }

    void OpenSettings()
    {
        if (_window == null)
        {
            _window = new SettingsWindow(_switcher);
            _window.Closed += (_, _) => _window = null;
        }
        _window.BringToFront();
    }
}
