#!/usr/bin/env bash
# Apply the Cyberpunk 2.5 save-format support patch to the local WolvenKit clone.
#
# Why this is needed: 2.5 stores entity stats in a new "optimized" layout
# (gameStatsStateMapStructure.ValuesOptimized) that the pinned WolvenKit commit predates. Reads
# still work, but a write silently drops that data (PersistencySystem2 loses ~88 KB on a real save),
# which would corrupt the player's stats in game.
#
# The patch is not vendored here: it is third-party work derived from WolvenKit (GPL-3.0) published
# by the CP2077 Save Editor project (MIT) at
#   https://github.com/AtfastrSlushyMaker/cyberpunk-2077-save-editor-gui
# so it is fetched at build time instead. Safe to re-run: it detects an already-applied patch.
set -euo pipefail

WK="${WK:-_refs/WolvenKit}"
PATCH_URL="https://raw.githubusercontent.com/AtfastrSlushyMaker/cyberpunk-2077-save-editor-gui/main/patches/wolvenkit-patch-2.5.patch"

if [ ! -d "$WK/.git" ]; then
  echo "error: $WK is not a git clone. Run the clone step from the README first." >&2
  exit 1
fi

tmp="$(mktemp -t wolvenkit-patch-2.5.XXXXXX.patch)"
trap 'rm -f "$tmp"' EXIT

echo "==> fetching $PATCH_URL"
curl -fsSL "$PATCH_URL" -o "$tmp"

if git -C "$WK" apply --reverse --check "$tmp" 2>/dev/null; then
  echo "==> already applied to $WK ($(git -C "$WK" rev-parse --short HEAD))"
  exit 0
fi

echo "==> applying to $WK ($(git -C "$WK" rev-parse --short HEAD))"
git -C "$WK" apply --3way "$tmp"
echo "==> done. Rebuild: dotnet build CP2077SaveKit.sln -c Release"
