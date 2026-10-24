using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TabSwitch;

public sealed partial class SettingsWindow : Window
{
    const string NoPreset = "(keep OTD's current settings)";

    readonly Switcher _switcher;
    readonly ObservableCollection<AppRule> _rules = new();

    public SettingsWindow(Switcher switcher)
    {
        _switcher = switcher;
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new SizeInt32(780, 900));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "app.ico"));

        RulesList.ItemsSource = _rules;
        Load();
        _switcher.Changed += OnSwitcherChanged;
        Closed += (_, _) => _switcher.Changed -= OnSwitcherChanged;
        UpdateStatus();
    }

    /// <summary>Restore if minimized and pull to the front (Activate alone doesn't from a tray click).</summary>
    public void BringToFront()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized) p.Restore();
        AppWindow.Show();
        Activate();
        SetForegroundWindow(hwnd);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr h);

    void OnSwitcherChanged() => DispatcherQueue.TryEnqueue(UpdateStatus);

    void UpdateStatus()
    {
        var mode = _switcher.State switch
        {
            TrayState.Otd => "OpenTabletDriver is active.",
            TrayState.XpPen => "XP-PEN is active.",
            TrayState.Switching => _switcher.Detail,
            _ => "Error: " + _switcher.Detail
        };
        StatusText.Text = mode + (string.IsNullOrEmpty(_switcher.ForegroundApp) ? "" : "  Focused app: " + _switcher.ForegroundApp);
    }

    void Load()
    {
        var s = _switcher.Settings;
        AutoToggle.IsOn = s.AutoSwitch;
        DefaultCombo.SelectedIndex = (int)s.DefaultDriver;
        DelayBox.Value = s.SwitchDelayMs;
        OtdFolderBox.Text = s.OtdFolder;
        XpExeBox.Text = s.XpPenExe;
        OtdUiToggle.IsOn = s.LaunchOtdUi;
        StopOtdToggle.IsOn = s.StopOtdInXpMode;
        _ = LoadStartupAsync();

        _rules.Clear();
        foreach (var r in s.Rules) _rules.Add(new AppRule { Exe = r.Exe, Driver = r.Driver });
        LoadPresets(s.OtdPreset);
        OtdFolderBox.TextChanged += (_, _) => LoadPresets(null);
    }

    async Task LoadStartupAsync()
    {
        try { StartupToggle.IsOn = await Startup.IsEnabledAsync(); }
        catch { }
    }

    void LoadPresets(string? select)
    {
        var current = select ?? (PresetCombo.SelectedItem as string);
        PresetCombo.Items.Clear();
        PresetCombo.Items.Add(NoPreset);
        try
        {
            var dir = Path.Combine(Settings.UserDataDir(OtdFolderBox.Text), "Presets");
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
                    PresetCombo.Items.Add(Path.GetFileNameWithoutExtension(f));
        }
        catch { }
        if (!string.IsNullOrEmpty(current) && PresetCombo.Items.Contains(current)) PresetCombo.SelectedItem = current;
        else PresetCombo.SelectedIndex = 0;
    }

    void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var exe = NewExeBox.Text.Trim();
        if (exe.Length == 0) return;
        if (!exe.Contains('.')) exe += ".exe";
        var existing = _rules.FirstOrDefault(r => Settings.Normalize(r.Exe) == Settings.Normalize(exe));
        if (existing != null) _rules.Remove(existing);
        _rules.Add(new AppRule { Exe = exe, Driver = (Driver)NewDriverCombo.SelectedIndex });
        NewExeBox.Text = "";
    }

    void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is AppRule r) _rules.Remove(r);
    }

    void PickFlyout_Opening(object? sender, object e)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.MainWindowHandle != IntPtr.Zero && p.Id != Environment.ProcessId) names.Add(p.ProcessName + ".exe"); }
            catch { }
            finally { p.Dispose(); }
        }
        RunningList.ItemsSource = names.ToList();
    }

    void RunningList_ItemClick(object sender, ItemClickEventArgs e)
    {
        NewExeBox.Text = (string)e.ClickedItem;
        PickFlyout.Hide();
    }

    async void BrowseOtd_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var f = await picker.PickSingleFolderAsync();
        if (f != null) OtdFolderBox.Text = f.Path;
    }

    async void BrowseXp_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var f = await picker.PickSingleFileAsync();
        if (f != null) XpExeBox.Text = f.Path;
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _switcher.Settings;
        s.AutoSwitch = AutoToggle.IsOn;
        s.DefaultDriver = (Driver)Math.Max(0, DefaultCombo.SelectedIndex);
        s.SwitchDelayMs = double.IsNaN(DelayBox.Value) ? 800 : (int)DelayBox.Value;
        s.OtdFolder = OtdFolderBox.Text.Trim();
        s.XpPenExe = XpExeBox.Text.Trim();
        s.OtdPreset = PresetCombo.SelectedIndex > 0 ? (string)PresetCombo.SelectedItem : "";
        s.LaunchOtdUi = OtdUiToggle.IsOn;
        s.StopOtdInXpMode = StopOtdToggle.IsOn;
        s.Rules = _rules.Select(r => new AppRule { Exe = r.Exe, Driver = r.Driver }).ToList();
        s.Save();
        _switcher.Reload(s);
        SavedText.Text = "Saved.";

        bool startupOk;
        try { startupOk = await Startup.SetAsync(StartupToggle.IsOn); }
        catch { startupOk = false; }
        if (!startupOk)
        {
            StartupToggle.IsOn = false;
            SavedText.Text = "Saved, but Windows blocked startup. Re-enable TabSwitch in Task Manager > Startup apps.";
        }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
