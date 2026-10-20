# Mod presets

Every `*.json` file in this folder appears in the launcher's **Presets** tab for anyone using it.
The launcher reads them from GitHub, so a merged preset reaches everyone without a launcher update.

```json
{
  "format": 1,
  "name": "Rounds With Friends",
  "description": "Shown under the name in the launcher.",
  "exclusive": true,
  "mods": [
    "olavim-RoundsWithFriends-2.2.2",
    "willis81808-UnboundLib"
  ]
}
```

- `mods` uses Thunderstore's `Owner-Name-Version` format. A version pins that exact release, which
  is what you want when everyone in a lobby must match. Leave the version off to always use the
  latest. Dependencies are resolved automatically, but exported presets list them anyway so the
  versions are pinned too.
- `exclusive: true` disables every other Thunderstore mod while the preset is applied. Mods that
  aren't from Thunderstore (local plugins such as ControllerFix, loose dlls) are never touched.
  Use `false` for add-on presets that layer on top of whatever is installed.
- Only Thunderstore mods can be shared. Local plugins are skipped on export.

## Adding one

1. Set up the mods you want in the launcher, then go to **Presets → Save current mods as a preset**,
   and save into this folder. Or from a checkout:
   `dotnet run --project src/RoundsModkit.Launcher -- --export-preset "My preset" presets/my-preset.json`
2. Add a description, then open a pull request.
