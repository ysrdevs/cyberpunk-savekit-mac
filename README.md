# CP2077 Save Kit

A native macOS (Apple Silicon) save editor for Cyberpunk 2077.

As far as public tooling goes, every known save editor (CyberCAT, CyberCAT-SimpleGUI, PixelRick's
CyberpunkSaveEditor) is a Windows application that Mac players can only run through Wine, CrossOver,
Whisky, or a VM. CP2077 Save Kit runs as a real arm64 macOS app with no compatibility layer. It
reads and writes the same `sav.dat` format the game uses, so edits made here load directly in the
native Mac build of the game.

> Status: v1. Verified in game on patch 2.3 (game version 2310), Apple Silicon.

## What it does

- Edit eddies (money) and any item quantity.
- Add any of 7,552 items: weapons, cyberware, clothing, mods, crafting components, consumables,
  grenades, ammo. Items are picked from a searchable, category-filtered catalog with game style names.
- Edit core attributes (Body, Reflexes, Technical Ability, Intelligence, Cool).
- Edit development points: Attribute Points, Perk Points, and Relic Points (Phantom Liberty).
- Automatic timestamped backup before any overwrite.

Appearance and lifepath editing are intentionally out of scope, because those edits are known to
corrupt saves.

## Why these technologies

The hard part of a save editor is not the UI, it is parsing REDengine's save format correctly and
keeping up with it across game patches. The format is a compressed tree of nodes whose contents are
serialized using REDengine's reflection (RTTI) type system, which spans thousands of generated
types. Reimplementing that from scratch (for example in Python) would mean re-deriving the entire
type schema and rebuilding it on every patch.

So the core decision was to reuse the parser instead of rewriting it:

- **.NET 8.** [WolvenKit](https://github.com/WolvenKit/WolvenKit), the most complete and actively
  maintained CP2077 toolkit, already contains a correct save reader and writer
  (`WolvenKit.RED4.Save`). It targets `net8.0`, which is cross platform and runs natively on Apple
  Silicon. Reusing it means the parsing correctness, including checksum and size recomputation on
  write, is solved and stays current with the project.

- **Avalonia UI.** A cross platform .NET UI framework that renders a real native window on macOS.
  This lets the GUI sit directly on top of the reused parser in the same language and runtime, with
  no interop layer.

- **Native LZ4 and CRC32, no Oodle.** The save body is LZ4 compressed, which `WolvenKit.RED4`
  handles through a managed package. Item names are resolved by hashing candidate names with CRC32
  (`CRC32(name) + (length << 32)`, REDengine's TweakDBID scheme) and matching them against a bundled
  name list. None of this needs Oodle, which matters because WolvenKit's bundled Oodle/Kraken
  decompressor is x86_64 only and will not load in an arm64 process.

The result is a clean split: the proven parser does the dangerous work, and this project adds only a
native Mac GUI, item name resolution, and item construction on top.

## Architecture

```
src/
  CP2077SaveKit.Core   class library: load / inspect / edit / save, name resolution, item building
  CP2077SaveKit.Cli    console harness for testing the Core layer
  CP2077SaveKit.App    Avalonia GUI (the app users run)
docs/ARCHITECTURE.md   design notes and findings
```

- **Core** wraps `WolvenKit.RED4.Save` behind a small API (`SaveFile.Load/Save`, `InventoryReader`,
  `InventoryEditor`, `PlayerDevelopment`) and never leaks WolvenKit types into the GUI.
- **App** is MVVM (CommunityToolkit.Mvvm). Loading and saving run off the UI thread with a progress
  indicator, and name dictionaries are warmed at startup.

### Data files (bundled in `src/CP2077SaveKit.Core/Resources`)

- `items.bin`: a gzip stream of about 1.96 million TweakDB names (from CyberCAT-SimpleGUI). Used to
  resolve item id hashes to code names.
- `aio_catalog.json`: 7,552 spawnable items with friendly names, categories, and tiers, extracted
  from the community "Categorized AIO Command List". Used for the add-item picker and game style
  display names.
- `ItemClasses.json`: per item structure metadata (type, single instance, mod slot parts, from
  CyberCAT-SimpleGUI). Used to construct weapons, clothing, and cyberware with the correct extended
  structure and default mod slots.

Name resolution prefers the curated catalog name, then the TweakDB code name, then the catalog id,
then the raw hash. Coverage on a real save is about 79 percent; the remainder are runtime or
procedurally generated item ids that have no static name anywhere.

## Requirements

- macOS on Apple Silicon (built and verified on arm64).
- .NET 8 SDK.
- A local clone of WolvenKit (the build references it by relative path, see below).

## Build

The Core project references `WolvenKit.RED4` by relative path, so WolvenKit must be present under
`_refs/WolvenKit`.

```sh
# 1. clone this repo, then fetch the parser dependency
git clone https://github.com/WolvenKit/WolvenKit.git _refs/WolvenKit

# 2. install .NET 8 if needed (official script, no sudo, installs to ~/.dotnet)
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"

# 3. build
dotnet build CP2077SaveKit.sln -c Release
```

## Run

### GUI

```sh
export PATH="$HOME/.dotnet:$PATH"
dotnet run --project src/CP2077SaveKit.App -c Release
```

1. Open Save. The picker starts in the Cyberpunk saves folder
   (`~/Library/Application Support/CD Projekt Red/Cyberpunk 2077/saves`).
   Choose a save folder's `sav.dat`.
2. Inventory tab: edit eddies and item quantities, filter by name or hash.
3. Add Item tab: choose a category, search, set a quantity, Add Selected.
4. Attributes and Perks tab: edit attributes and point pools.
5. Save As. The tool writes a timestamped `.bak` of any file it overwrites.
6. Load the edited save in game.

### CLI (for testing)

```sh
SAVE="$HOME/Library/Application Support/CD Projekt Red/Cyberpunk 2077/saves/<folder>/sav.dat"

dotnet run --project src/CP2077SaveKit.Cli -- info       "$SAVE"            # header + node summary
dotnet run --project src/CP2077SaveKit.Cli -- inv        "$SAVE"            # list inventory
dotnet run --project src/CP2077SaveKit.Cli -- attrs      "$SAVE"            # attributes / points / skills
dotnet run --project src/CP2077SaveKit.Cli -- dump       "$SAVE" tree.json  # full node tree to JSON
dotnet run --project src/CP2077SaveKit.Cli -- additem    "$SAVE" out.dat "Items.Preset_Silverhand_3516" 1
dotnet run --project src/CP2077SaveKit.Cli -- setmoney   "$SAVE" out.dat 1000000
dotnet run --project src/CP2077SaveKit.Cli -- roundtrip  "$SAVE" out.dat    # load + rewrite, integrity check
```

## Packaging a distributable app (for release)

`build/package-mac.sh` produces a notarized, double-click-ready `.dmg` and `.zip` that players can
run with no .NET install and no Gatekeeper warnings. It publishes a self-contained arm64 build,
wraps it in a `.app`, signs it with your Developer ID and hardened runtime, notarizes it with Apple,
staples the ticket, and writes both artifacts to `dist/`.

One-time setup (requires an Apple Developer account):

```sh
# 1. Create a "Developer ID Application" certificate in the Apple Developer portal,
#    then find its identity string:
security find-identity -v -p codesigning

# 2. Store notarization credentials once as a keychain profile
#    (app-specific password from https://account.apple.com > Sign-In and Security):
xcrun notarytool store-credentials cp2077notary \
  --apple-id "you@example.com" --team-id "YOURTEAMID" --password "app-specific-password"
```

Then build a release:

```sh
DEV_ID_APP="Developer ID Application: Your Name (YOURTEAMID)" \
NOTARY_PROFILE="cp2077notary" \
VERSION=1.0.0 \
build/package-mac.sh
```

An optional app icon is used if present at `build/AppIcon.icns`.

## Safety

- The save format is version locked. Always keep a backup of a working save before editing.
  The tool creates a timestamped `.bak` before overwriting, but keeping your own copy is wise.
- Edits are written only when you choose Save As. Loading and browsing never touch your saves.
- A bad edit can produce a save that loads but has stuck quests. Test edited saves in a spare slot
  before relying on them.

## How it was validated

Every risky write was confirmed by loading the result in the actual game on patch 2.3:

- An unedited round trip loads cleanly (the writer recompresses with a different LZ4 level than the
  game, which is harmless; every node and count is preserved).
- A money edit appears in game.
- A constructed stackable item appears in game.
- A constructed weapon (Malorian Arms 3516) appears, equips, shows stats, and fires. The game
  finalizes weapon stats on load, so no stat data synthesis is needed.
- An attribute point edit appears on the character screen.

## Limitations

- Name coverage is about 79 percent. Items with no static name show their raw hash.
- Catalog names come from the community spreadsheet, so they read like the game but can carry the
  spreadsheet's quirks. True localized names live in the game's Oodle compressed locale archives,
  which this project does not read.
- Vehicles are managed by a separate garage system and are not in the add-item picker.

## Credits

- [WolvenKit](https://github.com/WolvenKit/WolvenKit) for the save parser and RED type system.
- [CyberCAT-SimpleGUI](https://www.nexusmods.com/cyberpunk2077/mods/718) for `items.bin` and
  `ItemClasses.json`.
- The community "Categorized AIO Command List" for the item catalog.

## License

This is a personal project. The bundled data files and the WolvenKit dependency are the property of
their respective authors and are subject to their own licenses. Not affiliated with CD PROJEKT RED.
```
