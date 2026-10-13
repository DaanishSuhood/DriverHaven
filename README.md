<img src="src/DriverUpdater.App/Assets/DriverHaven-256.png" alt="DriverHaven icon" width="96" align="right" />

# DriverHaven

A Windows desktop app that finds driver updates for your PC and installs the ones you pick, safely.

- **One trusted source.** Updates come only from Windows Update, through the official Windows Update Agent API. Every driver is signed and vetted by Microsoft, and nothing is scraped from vendor websites.
- **Undo is always possible.** Before installing, the app creates a System Restore point and backs up the drivers it is about to replace.
- **You stay in control.** The app lists every update with the current and new versions. You choose which to install, confirm, and watch each step happen.

## Requirements

- Windows 10 or 11 (x64).
- Administrator rights. The app asks through UAC when it starts, because installing drivers and creating restore points need them.
- An internet connection, to reach Windows Update.

## Installing

1. Download `DriverHaven-Setup-<version>.exe` from the [latest release](https://github.com/DaanishSuhood/DriverHaven/releases/latest).
2. Run it and accept the UAC prompt.
3. Choose the install folder, which defaults to `C:\Program Files\DriverHaven`. Keep or untick **Create a Start menu shortcut** and **Create a desktop shortcut**; both are ticked by default.
4. On the last page, tick **Launch DriverHaven** to start it right away.

**No install needed?** The release also has a portable `DriverHaven.exe`. It's a single self-contained file that runs from anywhere, with no .NET install required.

To upgrade, run a newer setup over the existing install; there's no need to uninstall first.

**Uninstalling:** go to **Settings → Apps → Installed apps → DriverHaven → Uninstall**. This removes the program and both shortcuts. The uninstaller then asks whether to delete the logs and driver backups too. The default is **No**, because the backups let you reinstall drivers that DriverHaven replaced.

> **"Windows protected your PC" warning:** DriverHaven isn't code-signed, so SmartScreen may warn about it. Click **More info → Run anyway** to continue. To check that your download is the genuine file, compare it with the release's `SHA256SUMS.txt`: in PowerShell, run `Get-FileHash .\DriverHaven-Setup-<version>.exe` and check that the hash matches.

## Using it

1. Start DriverHaven from the Start menu or desktop and accept the UAC prompt. It scans your devices and searches Windows Update automatically.
2. **Updates available** lists each driver update with the device, current version and new version. A note appears when an update is dated *earlier* than the installed driver, or targets a device that isn't connected.
3. Tick the updates you want, then click **Install selected** and confirm. The app then:
   1. checks whether Windows is waiting for a restart, for example after an update or after uninstalling a device in Device Manager. If it is, the app asks before going on, because installs then often fail; choosing **No** changes nothing and offers to restart;
   2. creates a restore point, and asks you first if Windows can't make one, for example when System Protection is off;
   3. backs up the current drivers of the affected devices;
   4. downloads all the selected updates, then installs them one at a time.
4. A results window shows what happened to each driver. If any driver needs a restart, choose **Restart now** or **Later**.

**Cancel remaining** stops after the current step. A download or install that has already started always finishes, so a driver is never left half-installed. For the same reason, the window can't be closed while an install is running.

**Never want an update?** Tick it and click **Hide selected**. Hiding is stored in Windows Update itself, so Windows Update stops offering it too, not just DriverHaven. The **Hidden updates** tab lists what you've hidden; tick one and click **Unhide selected** to get it back.

The **Update history** tab lists every driver Windows Update has installed on the PC, newest first. It shows who installed each one (DriverHaven, Windows Update, or Windows setting up a new device) and, for failed installs, the reason.

**Newer graphics drivers from NVIDIA, AMD or Intel** (optional, off by default): Windows Update often offers graphics drivers weeks after their makers release them. Tick **Also check NVIDIA, AMD and Intel's websites for newer graphics drivers** at the bottom of the **Updates available** tab to be told when there's a newer one. For NVIDIA cards, DriverHaven asks NVIDIA's servers about the card's model and shows the newest version; AMD and Intel don't publish theirs in a way the app can check, so it links to their driver pages instead. DriverHaven only shows the link: it never downloads or installs drivers from these websites.

The **All devices** tab lists every device with its driver and highlights devices that have problems. It also has tools to create a restore point or back up drivers on demand.

## Where things are kept

| What | Where |
|---|---|
| The program | `C:\Program Files\DriverHaven\DriverHaven.exe` |
| Logs (one file per day, kept for 14 days) | `C:\ProgramData\DriverHaven\Logs\driverhaven-YYYYMMDD.log` |
| Driver backups | `C:\ProgramData\DriverHaven\Backups\<yyyyMMdd-HHmmss>\` |
| Window size and position | `%LocalAppData%\DriverHaven\window.json` |

Each backup folder has one subfolder per exported driver package (e.g. `oem42\`) and a `manifest.json`. The manifest records each device, its old driver version and INF, and whether that driver was exported. Drivers that ship with Windows ("inbox" drivers) are skipped, because Windows always keeps them.

Builds from before the rename used `C:\ProgramData\DriverUpdater` instead. That folder is left as it was, and can be deleted once you no longer need its backups.

## Rolling back a driver

Try these in order:

1. **The Driver backups tab.** It lists every driver DriverHaven has backed up, next to the version each device uses now. Select one and click **Restore selected driver**. DriverHaven creates a restore point, makes Windows install the backed-up driver even though it prefers the newer one, then checks which driver the device ends up using. Windows Update may offer the newer driver again later; hide it on the **Updates available** tab to stop that.
2. **Device Manager → the device → Properties → Driver → Roll Back Driver.** This is the quickest option, when the button is available.
3. **Reinstall the backup by hand.** In an elevated Command Prompt, run:
   ```
   pnputil /add-driver "C:\ProgramData\DriverHaven\Backups\<timestamp>\oem42\*.inf" /install
   ```
   If Windows keeps using the newer driver, go to **Device Manager → Update driver → Browse my computer → Let me pick from a list**, then choose the older version.
4. **System Restore.** Run `rstrui.exe` and pick the restore point named *"DriverHaven - before installing …"*.

## Troubleshooting

- **"Can't reach Windows Update"**: check the internet connection, proxy or firewall. The full error code is in the log.
- **Banner about a policy excluding drivers**: the *"Do not include drivers with Windows Updates"* group policy is set. Windows Update may then offer no drivers at all.
- **"No restore point"**: System Protection is turned off for drive C:. Turn it on under *System Properties → System Protection*, or continue with only the driver backup.
- **Managed PCs**: the app asks Microsoft's Windows Update servers directly, even when the PC normally uses WSUS. An organization's policy may still block that.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build DriverUpdater.sln
dotnet test tests\DriverUpdater.Core.Tests
# The app requires admin, so this shows a UAC prompt:
Start-Process src\DriverUpdater.App\bin\Debug\net10.0-windows\DriverHaven.exe
```

The code keeps its original project and namespace names (`DriverUpdater.*`). Only the product name shown to users is DriverHaven, and it is defined once in `ProductInfo.Name`.

### Sample mode (Debug builds only)

Sample mode lets you try the install flow on a PC that has no pending driver updates:

```powershell
Start-Process src\DriverUpdater.App\bin\Debug\net10.0-windows\DriverHaven.exe -ArgumentList '--sample'
```

- **What it does:** shows made-up updates for a few real devices. The restore point, backup, download, install, restart, hiding and driver restore are all simulated, and **nothing on the PC changes**. The update history is a few made-up entries plus what you install in the session, and the Driver backups tab shows made-up older drivers for a few real devices.
- **Planned results:** one success that needs a restart, two plain successes, one failed install and one failed download.
- **Restore point failure:** start with `--sample-no-restore-point` to simulate System Protection being off.
- **Pending restart:** start with `--sample-restart-pending` to simulate Windows waiting for a restart.
- **Release builds:** the sample code isn't compiled into them.

### Publishing and the installer

The installer is built with [Inno Setup 6](https://jrsoftware.org/isinfo.php). Install it once with `winget install --id JRSoftware.InnoSetup -e`.

```powershell
.\publish.ps1                  # app + installer
.\publish.ps1 -SkipInstaller   # app only
```

The script runs the tests, then writes:
- `artifacts\publish\DriverHaven.exe`: one self-contained file (about 62 MB) that needs no .NET install on the target PC. Trimming is deliberately off, because the Windows Update code uses COM through `dynamic`.
- `artifacts\installer\DriverHaven-Setup-<version>.exe`: built from `installer\DriverHaven.iss`. The version comes from `<Version>` in `src\DriverUpdater.App\DriverUpdater.App.csproj`. Never change the script's `AppId`, because upgrades rely on it.

### Icon

`tools\icon\make_icon.py` draws the icon in code: a shield holding a chip and an upward arrow. Sizes 32 px and below drop the chip, so they stay crisp. To regenerate `src\DriverUpdater.App\Assets\DriverHaven.ico` (16–256 px) and the PNG preview, run:

```powershell
python -I tools\icon\make_icon.py
```

This needs Python with Pillow.

## Project layout

```
src/DriverUpdater.Core/    Models and services, no UI: device inventory (WMI), Windows Update search,
                           download and install (WUA COM), restore points, pnputil backups, InstallOrchestrator
src/DriverUpdater.App/     WPF app (MVVM with CommunityToolkit.Mvvm, DI, Serilog); Sample/ is Debug-only
tests/DriverUpdater.Core.Tests/   xUnit tests for Core
installer/                 Inno Setup script
tools/icon/                Icon generator
```

## Contributing

Bug reports, ideas and pull requests are welcome. Please open an issue first for larger changes. Before sending a pull request, run `dotnet build` (it should have no warnings) and `dotnet test`.

## License

DriverHaven is released under the [MIT License](LICENSE).
