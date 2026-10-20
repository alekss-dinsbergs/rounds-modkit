# ROUNDS Modkit

A cross-platform (Linux + Windows) launcher and mod-development kit for **ROUNDS** on the
`old-rounds-for-mods` Steam branch.

- **Launcher** (`src/RoundsModkit.Launcher`, .NET 8 + Avalonia): finds the game, checks the branch,
  installs a hash-verified BepInEx 5.4.23.5, browses and installs Thunderstore mods with their
  dependencies, applies shareable mod presets, enables/disables/imports mods (zips or loose dlls),
  edits BepInEx `.cfg` files with typed controls, and launches through Steam.
- **Presets** (`presets/`): shared mod lists with pinned versions that show up in every launcher.
  See [presets/README.md](presets/README.md).
- **Decompiled reference** (`scripts/decompile.*`): ILSpy C# projects of `Assembly-CSharp` in
  `decompiled/<buildid>/`, plus publicized reference assemblies in `lib/` for building mods.
- **Controller fix** (`mods/ControllerFix`): makes Xbox controllers work in ROUNDS under Proton.
  See [Controllers on Linux](#controllers-on-linux-proton).
- **Example mod** (`mods/ExampleMod`): a BepInEx + Harmony plugin with config entries, built
  against the decompiled game types.

Decompiled code and game binaries are gitignored on purpose. They are Landfall's code, for local
reference and interop only, so don't publish them.

## Why the official build is used on Linux, not a recompiled one

ROUNDS ships only a Windows Unity 2018.4.34f1 Mono player, with D3D11 shaders and Windows-serialized
data. Decompiling the C# doesn't produce a Linux build. So on Linux the official build runs under
Proton, and BepInEx is loaded through its `winhttp.dll` proxy. That needs one Steam launch option:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

(Steam → ROUNDS → Properties → General → Launch Options. The launcher detects whether it's set and
has a Copy button.) Without it, Wine's builtin winhttp wins and the game starts unmodded with no
error.

## Setup

Requires the .NET 8 SDK. The branch is set in Steam → ROUNDS → Properties → Betas → `old-rounds-for-mods`.

```bash
# Linux (user-local SDK, no root):
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$HOME/.dotnet/tools:$PATH

scripts/decompile.sh                       # decompiled/<build>/ + lib/  (ROUNDS_DIR=... to override)
dotnet run --project src/RoundsModkit.Launcher
```

On Windows, run `scripts\decompile.ps1 [-RoundsDir <path>]`, then the same `dotnet run`.

To add it to the app menu and search:

- **Linux:** `scripts/install-desktop.sh` installs the launcher to `~/.local/opt/RoundsModkit` and
  adds a "ROUNDS Modkit" menu entry.
- **Windows:** `powershell -ExecutionPolicy Bypass -File scripts\install-startmenu.ps1` installs
  it to `%LOCALAPPDATA%\Programs\RoundsModkit` and adds a Start Menu shortcut.

Re-run either one to update; `--uninstall` / `-Uninstall` removes it.

Headless status check (Linux; the Windows exe is a GUI app, so it prints nothing there):

```bash
dotnet run --project src/RoundsModkit.Launcher -- --status
```

## Using the launcher

1. **Game** tab → *Install / repair BepInEx*. On Linux, set the launch option above.
2. *Launch ROUNDS* once. BepInEx then writes `BepInEx/config/*.cfg`, and the **Config** tab fills in.
3. **Browse** tab: search the ROUNDS mods on Thunderstore and press *Install*. Missing dependencies
   (usually `UnboundLib` and `MMHook`) are installed first, and disabled ones are re-enabled. The
   button says how many will be added. Installed mods show *Update* when Thunderstore has a newer
   version. Packages go into `BepInEx/plugins/<Author>-<Name>/`, the same layout r2modman uses.
   `BepInExPack_ROUNDS` dependencies are skipped because the launcher's BepInEx replaces that pack
   (it's BepInEx 5.4.19 with stock settings). NSFW-flagged packages are hidden.
4. **Presets** tab: apply a shared setup in one click, or save your current mods as a preset. Presets
   from this repo's `presets/` folder are listed for everyone; your own are kept in the launcher's
   data folder until you add them to the repo. Exclusive presets switch off other Thunderstore mods,
   which makes it easy to swap between setups.
5. **Mods** tab: turn mods on or off (unchecked ones move to `BepInEx/plugins-disabled/`), or
   *Import .zip / .dll…* for mods that aren't on Thunderstore. Missing dependencies are flagged.
6. Removing BepInEx deletes only the loader and `BepInEx/core`. Plugins and configs are kept.
   Steam → *Verify integrity* restores a pristine game.

The mod list is cached for an hour (*Reload list* forces a refresh), and the cached copy is used if
Thunderstore is unreachable. Launcher settings, the mod list, icons and downloaded packages live in `~/.local/share/RoundsModkit`
(or `%LOCALAPPDATA%\RoundsModkit` on Windows).

## Controllers on Linux (Proton)

Without a fix, an Xbox controller is detected in ROUNDS under Proton but nothing in the menu
reacts. The game's InControl input library starts its native HID module (with its XInput path
switched off in the game's settings). Wine presents the pad as "Controller (Xbox One For Windows)",
which matches no InControl profile, so the native module only sees an `Unknown Device` with
unmapped buttons. Because a working native module stops InControl from loading its other backends,
nothing else picks the pad up.

`mods/ControllerFix` is a BepInEx plugin that switches InControl to its XInput backend when it detects
Wine, and leaves Windows untouched. The pad then shows up as a mapped `XInput Controller`. Install it with:

```bash
dotnet build mods/ControllerFix -c Release -p:Deploy=true
```

It appears in the launcher's Mods tab, and its `Backend` setting (`Auto`, `GameDefault`, `XInput`,
`Native`, `UnityJoystick`) is editable on the Config tab. With `LogDevices` on, it writes what the
game detected to `BepInEx/LogOutput.log`, e.g.
`devices (1): 'XInput Controller' [Controller, XInput Device #0]`.

## Building a mod

```bash
dotnet build mods/ExampleMod -c Release                 # needs lib/ and BepInEx installed in the game
dotnet build mods/ExampleMod -c Release -p:Deploy=true  # also copies into BepInEx/plugins/
```

Pass `-p:RoundsDir=...` if the game isn't in the default Steam library. Browse
`decompiled/<build>/Assembly-CSharp` to find hook points (e.g. `CardChoice.StartPick`, `Player`,
`CardInfo`).

## Tests and release builds

```bash
dotnet test                                    # core parsers, mod import, headless UI
RMK_NETWORK_TESTS=1 dotnet test                # also real BepInEx + Thunderstore installs
RMK_SCREENSHOT_DIR=/tmp/shots dotnet test tests/RoundsModkit.Launcher.UiTests   # keep UI renders

dotnet publish src/RoundsModkit.Launcher -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/linux-x64
dotnet publish src/RoundsModkit.Launcher -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

Both targets publish from either OS.
