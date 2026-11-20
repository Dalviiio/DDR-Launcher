# Dalviiio DDR Launcher

A mod manager and editor for Dakar Desert Rally on Steam. It puts mods in the game, changes who drives what in each team, adds teams and drivers, and lets you paint team liveries. The game's own files are never changed: the launcher's edits go into one mod pak, and removing that pak gives you the original game back.

This is a beta. If something goes wrong, please report it (see below).

## What it does

The Mods page imports `.pak` mods, or `.zip`, `.rar` and `.7z` archives holding them, and turns each one on or off.

The Paddock lists every team of every edition: Dakar 20, 21 and 22, Classics, Snowrunner, and editions that mods add. You can change a team's vehicle, livery, driver and co-driver, add teams and drivers, and keep each setup in its own profile.

A team's livery opens in 3D. Export it to get each part's picture with its wireframe, paint it, then import your picture and apply it to the game.

The launcher speaks the game's nine languages and starts in your Windows language when it has it. You can pick another one in Settings.

## Install

1. Download `DalviiioDDRLauncher-Setup-x.y.z.exe` from the [Releases](https://github.com/Dalviiio/DDR-Launcher/releases) page.
2. Run it. It installs for your Windows user only and needs no administrator rights.

Windows may show "Windows protected your PC" the first time: the installer is not signed with a paid certificate yet. Click **More info**, then **Run anyway**.

The first time you open the Paddock, the launcher needs `oo2core_9_win64.dll` (Oodle), which the game uses to compress its files but does not ship on its own. Click **Download** to get it, or **Locate** if you already have it.

## Updates

When you allow it, the launcher looks for a new version on this page each time it starts. It reads the list of releases and sends nothing about you. Each update is checked against the author's signature before it is installed, then the launcher closes, updates and opens again.

## Uninstall

Use **Installed apps** in Windows. The launcher first takes its pak and the mods it placed out of the game, so the game is left as it ships, then asks whether to delete your profiles, liveries and mod library.

## Reporting a bug

Open an issue on this repository and attach the log: `%LocalAppData%\DalviiioDDRLauncher\Logs\launcher.log`. Say what you did, what you expected and what happened.

## Safety

- The launcher writes only in the game's `~mods` folder and in its own data folder (`%LocalAppData%\DalviiioDDRLauncher`).
- It removes only the files it placed itself, checked by their hash, and changes nothing while the game is running.
- Mod archives are treated as untrusted: their paths are checked before anything is extracted, and mod content is never run.

## Building

.NET 8 SDK on Windows:

```
dotnet build
dotnet test
powershell -File installer\build.ps1
```

The installer needs [Inno Setup 6](https://jrsoftware.org/isinfo.php). Releases are published with `tools\publish-release.ps1`, which signs the installer with the author's private key.

## Licenses

The libraries and the font the launcher uses are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). Dakar Desert Rally belongs to its publisher; this project is not affiliated with it.
