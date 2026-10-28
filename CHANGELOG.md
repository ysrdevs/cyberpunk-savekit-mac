# Changelog

## Unreleased

### Fixed

- **Writes to 2.5 saves no longer drop the new stats layout.** The pinned WolvenKit predates patch
  2.5, which moved entity stats into a shared, indexed layout (`gameStatsStateMapStructure.ValuesOptimized`
  plus `sharedDataBuffer` slices). Reads worked, so `info`/`inv`/`attrs` looked fine, but saving a
  2.5 save silently dropped that data: an unedited round trip lost 88,510 B from `PersistencySystem2`
  and 3,958 B from `StatsSystem` on a real patch-2.5 save. `build/apply-wolvenkit-2.5-patch.sh`
  applies the upstream fix to the local parser clone; after it, an unedited round trip is
  **byte-for-byte identical after decompression** (verified with `datatool verify`).
- Eddies edits were overwritten on save, and a save could hold the same stackable item in several
  sub-inventories (money appears up to three times). `SetQuantity` now updates every match and
  `GetQuantity` reports the largest, so the wallet entry is the one that changes and the one that is
  displayed (`#3`, @JensPenneman).
- The GUI save loop wrote every inventory row back by hash, which undid the eddies edit and let rows
  for the same item clobber each other. Saving now writes only what changed: the eddies field (when
  it differs) to every money entry, and each edited row to its own entry through the new
  `InventoryEditor.SetQuantityAt(subInventoryId, index, hash, qty)` (`#4`, @jwestb1).
- Add Item flushes pending edits before rebuilding rows.

### Added

- **`cli selftest <sav.dat>`**: in-memory regression check for the quantity semantics above. It
  reproduces the multi-sub-inventory money shape (stale 10,000 duplicates around a 121,994 wallet)
  and asserts reads report the wallet, writes reach every entry, a targeted write moves one entry
  only, and stale locations are refused. 12/12 on a real patch-2.5 save.
- **Save button**: overwrites the loaded `sav.dat` in place, after a `sav.dat.bak_<timestamp>` backup.
- **Inventory Category filter**, fed by `AioCatalog.CategoryOf(hash)` (catalog sheet, else the
  ItemClasses type, else `OTHER`).
- `InventoryItem` carries `SubInventoryId` + `Index`, so a row identifies one exact entry.
- **`tools/CP2077SaveKit.DataTool`**: refreshes the bundled item data from an installed game.
  - `diag` measures the gap between the bundled data and the installed patch.
  - `dump <id|0xhash>` prints every field of a TweakDB record, names resolved.
  - `mapping` / `slots` show how the bundled metadata maps onto the game's own fields.
  - `names` / `classes` / `catalog` regenerate `items.bin`, `ItemClasses.json`, `aio_catalog.json`.
  - `verify <a> <b>` byte-compares two saves after decompression — the check that made the 2.5 write
    bug visible.
- `--extra <file>` for `names`/`classes`/`catalog`: layers an additional name list (a `.txt` of
  names, or JSON holding strings / objects with `id`) on top of the bundled one.
- Catalog entries can be hash-only: an id of `0x…` is parsed as a literal TweakDBID
  (`TweakDbNames.ParseId`), so items no local source names are still addable from the picker.

### Changed

- **Item data regenerated from a patch-2.5 install** (game version 2500):
  - `aio_catalog.json`: 7,552 → 10,191 entries. The picker now covers every item record the game
    has; 2,639 entries were added (1,521 carry a code name, 1,118 are hash-labelled). The file is
    now indented, so future refreshes produce reviewable diffs.
  - `ItemClasses.json`: 10,039 → 10,186 entries. The 147 item records the bundle did not describe
    (2.5 additions) now have structure metadata, so weapons/cyberware/clothing from 2.5 construct
    with the right type and mod-slot parts. The rule that derives them from the game's own fields
    was fitted on, and reproduces, 10,007 of the 10,012 hand-validated bundled types (99.95%).
  - `items.bin`: 1,964,897 → 1,967,852 names (29.9 MB → 9.5 MB, better compression). Names are
    harvested from the installed game's own binaries and kept only when they hash to a key that
    database actually has. Inventory name coverage on a real 2.5 save: 119/125 → 125/125.
- Target framework stays `net8.0` with the pinned WolvenKit commit, where `#4` proposed `net10.0`
  plus a newer parser pin. Only that PR's fixes were taken; the framework migration is a separate
  decision (it also changes what the released bundle ships).

### Known limits

- Item **display** names for post-bundle items are not recoverable from local files: the shipped
  `tweakdb.bin` carries no name table (only CRC hashes) and the localized names live in Oodle
  compressed locale archives, which nothing on arm64 can decompress here. Those items show their
  code id + item type + tier instead (e.g. `Items.Coat_07_basic_01 · Outer Chest · TIER 5`).
  `--extra` accepts an external list when one is available (a 2.5 list of 40,548 code names covers
  2,903 of the 3,047 ids that local sources still call unknown; note it is GPL-3.0 data, so using it
  is a per-user choice and any regenerated `items.bin` inherits that).
- Edited 2.5 saves have not been loaded in game here; that check needs the game.
