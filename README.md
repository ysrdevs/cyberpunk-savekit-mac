# CP2077 Save Kit

A native macOS (Apple Silicon) toolkit for Cyberpunk 2077 — starting with a full
save editor (inventory, attributes, perks, appearance, facts). Built on the
[WolvenKit](https://github.com/WolvenKit/WolvenKit) save parser via .NET 8 + Avalonia.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full plan and findings.

## Layout
- `src/CP2077SaveKit.Core` — load/inspect/save library (wraps `WolvenKit.RED4`).
- `src/CP2077SaveKit.Cli`  — test harness (`info` / `dump` / `roundtrip`).
- `src/CP2077SaveKit.App`  — Avalonia GUI (cross-platform, native Mac window).
- `_refs/` — reference clones (gitignored). **Build depends on these existing.**

## Prereqs
- .NET 8 SDK. Installed here at `~/.dotnet` — add to PATH: `export PATH="$HOME/.dotnet:$PATH"`.
- The `_refs/WolvenKit` clone must be present (Core references it by relative path).

## Build & run
```sh
export PATH="$HOME/.dotnet:$PATH"
dotnet build CP2077SaveKit.sln

# inspect a save (read-only)
dotnet run --project src/CP2077SaveKit.Cli -- info "$HOME/Library/Application Support/CD Projekt Red/Cyberpunk 2077/<SaveFolder>/sav.dat"

# dump full node tree to JSON
dotnet run --project src/CP2077SaveKit.Cli -- dump <sav.dat> tree.json

# integrity round-trip (load + rewrite; then load result in-game to confirm)
dotnet run --project src/CP2077SaveKit.Cli -- roundtrip <sav.dat> /tmp/out.dat

# GUI
dotnet run --project src/CP2077SaveKit.App
```

## ⚠️ Safety
Save format is version-locked. **Always back up the save folder before editing** — a
bad write can brick a save (loads but quests stuck). The tool will refuse to overwrite
an original without a backup.
