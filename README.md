# Krokosha Run Settings Compat

Small BepInEx compatibility patch for Casualties Unknown Demo with KrokoshaCasualtiesMP.

This is the standalone release version of the run-settings part from `KrokoshaHostModifier`. It does not include the experimental client-authority, trader, item reach, or physics-debug patches.

## What It Fixes

- multiplayer hosts using Unlocked Sliders keep the visible extended slider values when MP serializes run settings and generates the world
- vanilla non-slider controls, including spawn/starting item settings, are copied from the visible menu controls before MP starts the run
- `xpgain` is preserved after Krokosha MP applies its multiplayer rules
- optional host/server config overrides can force vanilla RunSettings values for testing or packs

The patch is host/server-side. Clients receive the resulting RunSettings through Krokosha MP's normal start packet.

## Requirements

- .NET SDK, only if building from source
- Casualties Unknown Demo
- BepInEx installed in the game folder
- KrokoshaCasualtiesMP installed
- Unlocked Sliders is optional, but this patch is designed to make it work correctly with KrokoshaCasualtiesMP

## Install

For a normal release install, put the DLL here:

```text
Casualties Unknown Demo\BepInEx\plugins\KrokoshaRunSettingsCompat\KrokoshaRunSettingsCompat.dll
```

For Nexus, the clean package shape is:

```text
BepInEx\plugins\KrokoshaRunSettingsCompat\KrokoshaRunSettingsCompat.dll
```

Do not install this standalone DLL together with `KrokoshaHostModifier` unless you disable the duplicate run-settings features in one of their config files. `KrokoshaHostModifier` already includes this same compatibility patch for the larger experimental pack.

## Build and Install

From this folder, run:

```bat
build-and-install.bat
```

The script tries to find the game from Steam's install and library folders. If it cannot find it, pass the game folder:

```bat
build-and-install.bat "D:\SteamLibrary\steamapps\common\Casualties Unknown Demo"
```

You can also set `CASUALTIES_UNKNOWN_DIR` to the game folder.

The script expects KrokoshaCasualtiesMP at:

```text
BepInEx\plugins\KrokMP\KrokoshaCasualtiesMP.dll
```

If your MP mod is somewhere else, build manually and pass `MPPluginDir`.

## Manual Build

```bat
dotnet build KrokoshaRunSettingsCompat.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Casualties Unknown Demo"
```

With a custom MP plugin folder:

```bat
dotnet build KrokoshaRunSettingsCompat.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Casualties Unknown Demo" -p:MPPluginDir="D:\SteamLibrary\steamapps\common\Casualties Unknown Demo\BepInEx\plugins\KrokMP"
```

Then copy `bin\Release\netstandard2.1\KrokoshaRunSettingsCompat.dll` into the game's `BepInEx\plugins\KrokoshaRunSettingsCompat` folder.

## Config

BepInEx generates the config file at runtime:

```text
BepInEx\config\kef.casualtiesunknown.mprunsettingscompat.cfg
```

The config has these settings:

- `SyncRunSettingsFromMenuControls`
- `PreserveVanillaRunSettingsAfterMPRules`
- `ServerRunSettingOverrides`

`SyncRunSettingsFromMenuControls` is enabled by default. On the host/server, it snapshots the visible vanilla run-settings controls into `PreRunScript.runSettings` before MP serializes settings to clients and before world generation starts. With no slider-unlock mod installed, this should be a no-op because the visible controls already match vanilla settings. With Unlocked Sliders installed, extended slider values are captured through the actual `Slider.value`. It also snapshots bools and dropdowns, so settings such as spawn/starting item options are less likely to go stale before MP sends the start packet.

`PreserveVanillaRunSettingsAfterMPRules` is enabled by default. It restores vanilla `xpgain` after Krokosha MP applies its multiplayer rules, because the MP mod otherwise rewrites `WorldgenPatches.runsettings["xpgain"]` from `XPGainMultiplier`.

`ServerRunSettingOverrides` is empty by default. It is a manual host/server lever. Set it to semicolon/comma/newline-separated vanilla RunSettings overrides only when you want config values to override the visible menu controls:

```ini
ServerRunSettingOverrides = oreamount=3;basetrapdensity=0;debugworld=true;xpgain=8
```

Change config values while the game is closed, then restart the game.

## Source Release

For a source-only upload, include:

- `KrokoshaRunSettingsCompat.cs`
- `KrokoshaRunSettingsCompat.csproj`
- `build-and-install.bat`
- `find-game-dir.ps1`
- `README.md`

Do not include `bin`, `obj`, PDBs, `.deps.json` files, or generated config files.
