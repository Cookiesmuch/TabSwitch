using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabSwitch;

public enum Driver { OpenTabletDriver, XpPen }

public class AppRule
{
    public string Exe { get; set; } = "";
    public Driver Driver { get; set; } = Driver.XpPen;

    [JsonIgnore]
    public int DriverIndex
    {
        get => (int)Driver;
        set => Driver = (Driver)value;
    }
}

public class Settings
{
    public bool AutoSwitch { get; set; } = true;
    /// <summary>Driver used when the foreground app matches no rule.</summary>
    public Driver DefaultDriver { get; set; } = Driver.OpenTabletDriver;
    /// <summary>Defaults to the copy TabSwitch downloads itself on first run.</summary>
    public string OtdFolder { get; set; } = OtdInstaller.DefaultFolder;
    public string XpPenExe { get; set; } = @"C:\Program Files\XPPen\XPPenTablet.exe";
    /// <summary>Name of a preset in OTD's Presets folder to apply on every switch to OTD. Empty = keep current settings.</summary>
    public string OtdPreset { get; set; } = "";
    public bool LaunchOtdUi { get; set; } = false;
    public bool StopOtdInXpMode { get; set; } = false;
    public int SwitchDelayMs { get; set; } = 800;
    public List<AppRule> Rules { get; set; } = new();
    public List<string> IgnoredProcesses { get; set; } = new()
    {
        "explorer", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost",
        "LockApp", "TextInputHost", "TabSwitch", "SystemSettings"
    };

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabSwitch");
    public static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch { }
        var s = new Settings();
        s.Save();
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public AppRule? Match(string exeName)
    {
        var n = Normalize(exeName);
        return Rules.FirstOrDefault(r => Normalize(r.Exe) == n);
    }

    public bool IsIgnored(string exeName) =>
        IgnoredProcesses.Any(p => Normalize(p) == Normalize(exeName));

    public static string Normalize(string s)
    {
        s = s.Trim();
        return (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s).ToLowerInvariant();
    }

    /// <summary>OTD keeps its data in a "userdata" folder next to the exe when portable, otherwise in %LOCALAPPDATA%.</summary>
    public static string UserDataDir(string otdFolder)
    {
        var portable = Path.Combine(otdFolder, "userdata");
        return Directory.Exists(portable) ? portable
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenTabletDriver");
    }

    public string PresetsDir => Path.Combine(UserDataDir(OtdFolder), "Presets");
}
