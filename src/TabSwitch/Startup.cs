using Microsoft.Win32;
using Windows.ApplicationModel;

namespace TabSwitch;

/// <summary>"Start with Windows": MSIX startup task when packaged, HKCU Run key for unpackaged dev builds.</summary>
public static class Startup
{
    const string TaskId = "TabSwitchStartup";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "TabSwitch";

    static bool IsPackaged
    {
        get
        {
            try { return Package.Current != null; }
            catch { return false; }
        }
    }

    public static async Task<bool> IsEnabledAsync()
    {
        if (IsPackaged)
        {
            var state = (await StartupTask.GetAsync(TaskId)).State;
            return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunValue) != null;
    }

    /// <summary>Returns false when Windows refuses, e.g. the user disabled TabSwitch in Task Manager's Startup tab.</summary>
    public static async Task<bool> SetAsync(bool on)
    {
        if (IsPackaged)
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (!on) { task.Disable(); return true; }
            var state = await task.RequestEnableAsync();
            return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (k == null) return false;
        if (on) k.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(RunValue, throwOnMissingValue: false);
        return true;
    }
}
