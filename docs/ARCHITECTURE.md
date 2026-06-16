# CP2077 Mac Toolkit — Architecture & Plan

Two projects, sequenced. Save editor first (it teaches us the TweakDBID / node-tree
vocabulary the CET work needs anyway).

---

## Project 1 — Save Editor (current focus)

A native macOS (Apple Silicon) GUI save editor for Cyberpunk 2077: inventory, attributes,
perks/skills, appearance, money, quest facts — everything the save exposes.

### Key decision: reuse the parser, don't reimplement it
WolvenKit's save parser is generic — it reads the node tree using the full RED4 reflection
type system (thousands of generated type classes). Reimplementing that in Python would mean
re-deriving the entire type schema and would break on every game patch. Instead we **reference
the existing, maintained `WolvenKit.RED4` library** and build only a new cross-platform GUI.

### Stack (PROVEN working on this machine, 2026-06-16)
- **.NET 8** SDK — installed at `~/.dotnet`, `osx-arm64`, native Apple Silicon.
- **Parser base**: `_refs/WolvenKit/WolvenKit.RED4/WolvenKit.RED4.csproj`
  - Builds clean on arm64: **0 errors** (`dotnet build -c Release`).
  - Dependency tree (all portable, no Windows/native deps):
    `WolvenKit.RED4` → `K4os.Compression.LZ4` (save is LZ4-compressed) + `WolvenKit.Core`
    → `CommunityToolkit.Mvvm`, `semver`, `System.Reflection.MetadataLoadContext`, `Serilog`.
- **GUI**: Avalonia UI (cross-platform XAML, real native Mac window).
- **Item dictionary**: CyberCAT `items.bin` (29.8 MB) — full TweakDB item list for inventory editing.

### Save format facts (from WolvenKit source)
- File: `sav.dat`, magic check in `CyberpunkSaveReader.ReadFileInfo` (`CyberpunkSaveFile.MAGIC`).
- Header struct: `CyberpunkSaveHeaderStruct` (carries `GameVersion`; gates on >= Patch 2.0).
- Body: LZ4-compressed, parsed into a **node tree** (`NodeEntry`, with `Children`).
- Write-back integrity: `CyberpunkSaveWriter` + `SaveHashHelper` (FNV1A64) — must recompute
  sizes (`CalculateTrueSizes`) and hashes or the game rejects the save.
- ~50 dedicated node parsers in `WolvenKit.RED4/Save/Parser/`, incl.
  `InventoryParser`, `StatsSystemParser`, `StatPoolSystemParser`, `PlayerSystemParser`,
  `FactsDBParser`, `CharacterCustomizationAppearancesParser`, `WardrobeSystemParser`.

### Save location on the Mac build
`~/Library/Application Support/CD Projekt Red/Cyberpunk 2077/` (per-save folders, each with
`sav.dat` + `metadata.9.json` + screenshot). **ALWAYS back up before editing — format is
version-locked; a mismatch can brick a save.**

### Build ladder
1. [x] Scaffold solution: `Core` lib (refs WolvenKit.RED4) + `App` (Avalonia) + a CLI for testing.
       DONE 2026-06-16 — all three build clean (0 errors), CLI runs. Core wraps WolvenKit
       reader/writer (`SaveFile.cs`) + node→JSON dumper (`NodeDump.cs`).
2. [~] Core: load `sav.dat` → node tree → dump to JSON (READ ONLY first). Code written
       (`cli info|dump`); UNTESTED — needs a real save (game installing). Test the moment one exists.
3. [ ] Parse inventory into readable item list (TweakDBID + qty) via `items.bin`.
4. [ ] Write-back: edit a value, re-serialize, fix sizes + hashes. Round-trip test in game.
5. [ ] GUI: tabs for Inventory / Attributes / Perks / Appearance / Facts.
6. [ ] Polish: backups, validation, version detection.

### Status blocker
Need a **real save file** to test parsing/round-trip. Game is installing as of 2026-06-16.

---

## Project 2 — CET on Mac (later; research track)

### Phase 0 verdict: injection wall is mostly DOWN (verified against the shipped binary)
`/Applications/Cyberpunk 2077/Cyberpunk2077.app` — arm64 Mach-O, Developer ID signed
(CD PROJEKT S.A., team `PL47UP47QQ`), hardened runtime ON (`flags=0x10000`). BUT it ships:
- `com.apple.security.cs.allow-dyld-environment-variables` → `DYLD_INSERT_LIBRARIES` is honored.
- `com.apple.security.cs.disable-library-validation` → loads dylibs not signed by CDPR.

Those are exactly the two locks that normally block dylib injection — both left open.
**Likely no SIP-disable needed.** (CDPR set them for their own `libGalaxy`/`libBink` dylibs.)
Renderer is **Metal + MetalFX** (links `Metal.framework`, `QuartzCore`) — overlay must hook
`CAMetalLayer`/`MTLCommandBuffer presentDrawable:`, not DX12.

### Phases
- 0 [gate, ~done on paper]: can we inject? → entitlements say yes; still must test live.
- 1: hello-world dylib logs from inside the process (proves injection works in practice).
- 2: hook Metal present, draw an ImGui overlay (Metal backend).
- 3 [the months-long grind]: find REDengine RTTI bootstrap in the arm64 binary (Hopper/Ghidra).
  Entitlements do NOT help here — this is normal engine RE.
- 4: bind RTTI → Lua VM → console → spawn item using TweakDBIDs learned from the save editor.

---

## Repo layout
- `_refs/` — reference clones (gitignored): WolvenKit, CyberpunkSaveEditor, CyberCAT-SimpleGUI.
- `src/` — our source.
- `fixtures/` — test saves (gitignored; never commit personal saves).
- `docs/` — this.
