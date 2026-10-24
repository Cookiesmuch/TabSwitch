using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace TabSwitch;

/// <summary>Downloads OpenTabletDriver from its official GitHub releases. TabSwitch never bundles it.</summary>
public static class OtdInstaller
{
    const string LatestRelease = "https://api.github.com/repos/OpenTabletDriver/OpenTabletDriver/releases/latest";
    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string DefaultFolder => Path.Combine(LocalAppData, "TabSwitch", "OpenTabletDriver");

    public static bool IsInstalled(string folder) => File.Exists(Path.Combine(folder, "OpenTabletDriver.Daemon.exe"));

    public static bool IsDefault(string folder)
    {
        try
        {
            return string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.GetFullPath(DefaultFolder).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static async Task InstallAsync(string folder)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TabSwitch");

        using var release = JsonDocument.Parse(await http.GetStringAsync(LatestRelease));
        var url = release.RootElement.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString()!.EndsWith("win-x64.zip", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault() ?? throw new InvalidOperationException("No Windows build in the latest OpenTabletDriver release.");

        var tmp = Path.Combine(Path.GetTempPath(), "tabswitch-otd-" + Guid.NewGuid());
        var zip = tmp + ".zip";
        try
        {
            await using (var src = await http.GetStreamAsync(url))
            await using (var dst = File.Create(zip))
                await src.CopyToAsync(dst);
            ZipFile.ExtractToDirectory(zip, tmp);

            // The zip may or may not wrap everything in a top-level folder.
            var root = Directory.EnumerateFiles(tmp, "OpenTabletDriver.Daemon.exe", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName).FirstOrDefault()
                ?? throw new InvalidOperationException("Downloaded OpenTabletDriver zip has no daemon.");
            CopyDir(root!, folder);
        }
        finally
        {
            try { File.Delete(zip); } catch { }
            try { Directory.Delete(tmp, true); } catch { }
        }

        // Portable mode (a "userdata" folder next to the exe), carrying over a normal OTD install's settings.
        var userdata = Path.Combine(folder, "userdata");
        if (!Directory.Exists(userdata))
        {
            var existing = Path.Combine(LocalAppData, "OpenTabletDriver");
            if (Directory.Exists(existing)) CopyDir(existing, userdata);
            else Directory.CreateDirectory(userdata);
        }
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }
}
