#!/usr/bin/env bash
# Installs the launcher for the current user and adds it to the desktop menu/search.
# Re-run after changes to update; --uninstall removes it.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="$HOME/.local/opt/RoundsModkit"
ENTRY="$HOME/.local/share/applications/rounds-modkit.desktop"

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -rf "$DEST" "$ENTRY"
  update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
  echo "Removed."
  exit 0
fi

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

dotnet publish "$ROOT/src/RoundsModkit.Launcher" -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
  -o "$ROOT/artifacts/linux-x64" >/dev/null

mkdir -p "$DEST" "$(dirname "$ENTRY")"
install -m 755 "$ROOT/artifacts/linux-x64/RoundsModkit" "$DEST/RoundsModkit"

# Steam installs this icon once ROUNDS has a desktop shortcut; fall back to a theme icon otherwise.
ICON=applications-games
compgen -G "$HOME/.local/share/icons/hicolor/*/apps/steam_icon_1557740.png" >/dev/null && ICON=steam_icon_1557740

cat > "$ENTRY" <<EOF
[Desktop Entry]
Type=Application
Name=ROUNDS Modkit
GenericName=Mod launcher
Comment=Manage BepInEx mods and settings for ROUNDS
Exec="$DEST/RoundsModkit"
Icon=$ICON
Terminal=false
Categories=Game;
Keywords=rounds;mods;bepinex;modkit;launcher;
StartupWMClass=RoundsModkit
EOF

desktop-file-validate "$ENTRY"
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
echo "Installed: $ENTRY"
