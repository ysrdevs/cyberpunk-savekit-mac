# Changelog

## Unreleased

### Fixed
- GUI eddies edits were always overwritten on save: after `SetMoney`, the save loop wrote every
  inventory row back by hash, including the money row's stale quantity. Saves now write only
  changed values: the eddies field (if changed) to all money entries, and each edited row to its
  own entry via the new `InventoryEditor.SetQuantityAt(subInventoryId, index, hash, qty)`.
- Money present in multiple sub-inventories: `SetQuantity` updates all matches, `GetQuantity`
  returns the largest (ysrdevs/cyberpunk-savekit-mac#3, @JensPenneman).
- Rows for the same item in different sub-inventories no longer overwrite each other.
- Add Item flushes pending edits before rebuilding rows.

### Added
- **Save** button: overwrites the loaded `sav.dat` in place after a `sav.dat.bak_<timestamp>` backup.
- Inventory tab **Category** filter. `AioCatalog.CategoryOf(hash)` uses the catalog sheet,
  falling back to the ItemClasses type (Weapon/Grenade, Cyberware, Clothing, ItemRecipe), else OTHER.
- `InventoryItem` now carries `SubInventoryId` and `Index`.

### Changed
- Target framework `net8.0` → `net10.0`. **Building requires the .NET 10 SDK.**
- WolvenKit pin `a5d0124` → `b900c6b`. The `WolvenKit.RED4/Save` code is unchanged, and output
  was verified byte-identical to the previous build on a real save.
