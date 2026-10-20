#!/usr/bin/env bash
# Decompiles the installed ROUNDS game code into decompiled/<buildid>/ as browsable C# projects,
# and writes publicized reference assemblies into lib/ for building mods.
# Output stays local (gitignored): it is Landfall's code, not ours to redistribute.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME_DIR="${ROUNDS_DIR:-$HOME/.local/share/Steam/steamapps/common/ROUNDS}"
MANAGED="$GAME_DIR/ROUNDS_Data/Managed"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$HOME/.dotnet/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# ilspycmd targets net6; the SDK installed for the launcher is net8.
export DOTNET_ROLL_FORWARD=Major

[[ -f "$MANAGED/Assembly-CSharp.dll" ]] || { echo "ROUNDS not found at $GAME_DIR (set ROUNDS_DIR)"; exit 1; }

MANIFEST="$(dirname "$(dirname "$GAME_DIR")")/appmanifest_1557740.acf"
BUILD="$(grep -m1 '"buildid"' "$MANIFEST" 2>/dev/null | grep -o '[0-9]\+' || echo unknown)"
BRANCH="$(grep -m1 '"BetaKey"' "$MANIFEST" 2>/dev/null | sed 's/.*"BetaKey"[[:space:]]*"\([^"]*\)".*/\1/' || true)"
[[ "$BRANCH" == "old-rounds-for-mods" ]] || echo "warning: installed branch is '${BRANCH:-public}', not old-rounds-for-mods"

OUT="$ROOT/decompiled/$BUILD"
mkdir -p "$OUT" "$ROOT/lib"

command -v ilspycmd >/dev/null || dotnet tool install -g ilspycmd --version 8.2.0.7535
for asm in Assembly-CSharp Assembly-CSharp-firstpass; do
  echo "Decompiling $asm -> $OUT/$asm"
  rm -rf "${OUT:?}/$asm"
  ilspycmd -p -o "$OUT/$asm" -r "$MANAGED" --nested-directories "$MANAGED/$asm.dll"
done

command -v assembly-publicizer >/dev/null || dotnet tool install -g BepInEx.AssemblyPublicizer.Cli
for asm in Assembly-CSharp Assembly-CSharp-firstpass; do
  assembly-publicizer "$MANAGED/$asm.dll" -o "$ROOT/lib/$asm.dll" --overwrite
done

echo "$BUILD ${BRANCH:-public}" > "$OUT/SOURCE"
echo "Done: $OUT"
