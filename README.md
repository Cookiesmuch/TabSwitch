# TabSwitch

A Windows tray app that switches your drawing tablet between the **XP-PEN** driver and **[OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver)** automatically, based on which app is in focus.

- **OTD mode**: closes the XP-PEN tray app, applies your chosen OTD preset and restarts OpenTabletDriver.
- **XP-PEN mode**: starts the XP-PEN app again.
- OpenTabletDriver is kept running in the background.

## Tray icon

| Icon | Meaning |
| --- | --- |
| Green **O** | OpenTabletDriver active |
| Orange **X** | XP-PEN active |
| Blue arrows | Switching, or downloading OpenTabletDriver |
| Red **!** | Error (hover for details) |
| Grey dot in the corner | Automatic switching is off |

- **Single click**: open the active driver's own config window (OTD or XP-PEN)
- **Double click**: open TabSwitch settings
- **Right click**: switch manually, toggle automatic switching, settings, exit

## Settings

The settings window (WinUI 3) controls:

- **App rules**: `krita.exe → XP-PEN`, `osu!.exe → OpenTabletDriver`, and so on. Apps without a rule use the default driver.
- **Switch delay**: ignores brief focus changes such as alt-tab.
- **OTD preset**: picked from OTD's `Presets` folder and applied on every switch to OTD.
- **OTD paths and options**: install folder, opening the OTD UI after a restart, and stopping OTD while in XP-PEN mode.
- **XP-PEN path** and **Start with Windows**.

Settings are stored in `%APPDATA%\TabSwitch\settings.json`.

## Install

Requirements: Windows 10 2004 or later (x64), and the [XP-PEN driver](https://www.xp-pen.com/download) installed.

TabSwitch ships as an MSIX package signed with a self-signed certificate, so Windows has to trust that certificate once.

1. From [Releases](../../releases), download `TabSwitch_<version>_x64.msix` and `TabSwitch.cer`.
2. In an **administrator** PowerShell, trust the certificate:
   ```powershell
   Import-Certificate -FilePath .\TabSwitch.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```
3. Double-click the `.msix` and click **Install**.

On first launch, TabSwitch downloads the latest OpenTabletDriver release from its official GitHub page into `%LOCALAPPDATA%\TabSwitch\OpenTabletDriver`. It sets that copy up in portable mode and carries over any existing OTD settings. The tray icon shows blue while this happens.

Already have OpenTabletDriver somewhere else? Point TabSwitch at it under **Settings → OpenTabletDriver → Install folder**.

To uninstall, remove TabSwitch from **Settings → Apps**. The downloaded OTD folder and `%APPDATA%\TabSwitch` are left in place; delete them by hand if you want them gone.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet build src\TabSwitch -c Release -p:Platform=x64   # unpackaged dev build (bin\x64\Release\...\TabSwitch.exe)
.\build.ps1                                            # signed release package in .\dist
```

`build.ps1` creates a self-signed `CN=TabSwitch` code-signing certificate in your user certificate store on first run. It reuses it after that and exports only the public `TabSwitch.cer`. Bump `Version` in `src\TabSwitch\Package.appxmanifest` before each release.

## Licensing

TabSwitch is licensed under the [GNU GPL v3.0](LICENSE).

TabSwitch does not include OpenTabletDriver or any XP-PEN software. OpenTabletDriver (LGPL-3.0) is downloaded from its official GitHub releases on first run. XP-PEN's driver must be installed separately.
