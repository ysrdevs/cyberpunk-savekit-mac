using CP2077SaveKit.Core;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Save.Classes;
using WolvenKit.RED4.Types;

namespace CP2077SaveKit.Cli;

/// <summary>
/// In-memory regression check for the quantity-editing semantics (sav.dat is never written).
/// It reproduces the shape of a real save where the same stackable item sits in several
/// sub-inventories — money being the usual case — and asserts that reads report the wallet
/// entry, that writes touch every entry, and that a targeted write moves one entry only.
/// </summary>
public static class SelfTest
{
    public static int Run(string path)
    {
        var save = SaveFile.Load(path);
        var inv = FindInventory(save.Nodes) ?? throw new SaveLoadException("No `inventory` node found in save.");

        var checks = new List<(bool Ok, string Message)>();
        void Check(bool ok, string msg) => checks.Add((ok, msg));

        var money = AllMoney(inv);
        Check(money.Count >= 1, $"money entry present ({money.Count} found)");
        if (money.Count == 0) return Report(checks);

        // Reproduce the reported shape: the first entry is a stale placeholder, the wallet with
        // the real amount lives in another sub-inventory.
        const uint stale = 10_000;
        const uint wallet = 121_994;
        var first = money[0];
        first.Item.Quantity = stale;
        var others = inv.SubInventories.Where(s => s != first.Sub).ToList();
        CloneInto(others[0], first.Item, wallet);
        CloneInto(others[^1], first.Item, stale);
        Check(AllMoney(inv).Count == 3, $"reproduced 3 money entries across sub-inventories (got {AllMoney(inv).Count})");

        // 1. Reads report the wallet, not the first (stale) match.
        var read = InventoryEditor.GetQuantity(save, InventoryEditor.MoneyHash);
        Check(read == wallet, $"GetQuantity reports the wallet entry ({read:n0}); first-match would report {stale:n0}");

        // 2. Writes reach every entry.
        Check(InventoryEditor.SetQuantity(save, InventoryEditor.MoneyHash, 1_000_000), "SetQuantity reports the item as found");
        var afterSet = AllMoney(inv).Select(m => m.Item.Quantity).ToList();
        Check(afterSet.All(q => q == 1_000_000), $"SetQuantity updated all entries ({string.Join(", ", afterSet)})");

        // 3. A targeted write moves exactly one entry.
        var target = AllMoney(inv)[1];
        Check(InventoryEditor.SetQuantityAt(save, target.Sub.InventoryId, target.Index, InventoryEditor.MoneyHash, 42),
            "SetQuantityAt writes the entry it points at");
        var afterAt = AllMoney(inv).Select(m => m.Item.Quantity).ToList();
        Check(afterAt.Count(q => q == 42) == 1 && afterAt.Count(q => q == 1_000_000) == 2,
            $"SetQuantityAt left the other entries alone ({string.Join(", ", afterAt)})");

        // 4. Stale locations and hash mismatches are refused rather than mis-written.
        Check(!InventoryEditor.SetQuantityAt(save, target.Sub.InventoryId, target.Index + 9_999, InventoryEditor.MoneyHash, 7),
            "SetQuantityAt refuses an out-of-range index");
        Check(!InventoryEditor.SetQuantityAt(save, target.Sub.InventoryId, target.Index, TweakDbNames.TweakHash("Items.NotMoney"), 7),
            "SetQuantityAt refuses a hash mismatch");

        // 5. Absent items behave as before.
        var absent = TweakDbNames.TweakHash("Items.DefinitelyNotInThisSave");
        Check(InventoryEditor.GetQuantity(save, absent) == 0, "absent item reads 0");
        Check(!InventoryEditor.SetQuantity(save, absent, 5), "absent item reports not found");

        // 6. Reading is still faithful after all that.
        Check(InventoryEditor.GetQuantity(save, InventoryEditor.MoneyHash) == 1_000_000,
            "effective balance follows the last write");

        return Report(checks);
    }

    private sealed record MoneyEntry(SubInventory Sub, int Index, ItemData Item);

    private static List<MoneyEntry> AllMoney(Inventory inv)
    {
        var list = new List<MoneyEntry>();
        foreach (var sub in inv.SubInventories)
            for (var i = 0; i < sub.Items.Count; i++)
                if ((ulong)sub.Items[i].ItemInfo.ItemId.Id == InventoryEditor.MoneyHash)
                    list.Add(new MoneyEntry(sub, i, sub.Items[i]));
        return list;
    }

    private static void CloneInto(SubInventory sub, ItemData template, uint quantity) => sub.Items.Add(new ItemData
    {
        ItemInfo = new ItemInfo
        {
            ItemId = new gameItemID
            {
                Id = InventoryEditor.MoneyHash,
                RngSeed = template.ItemInfo.ItemId.RngSeed,
                UniqueCounter = (ushort)(sub.Items.Count + 1),
            },
            ItemStructure = template.ItemInfo.ItemStructure,
        },
        Flags = template.Flags,
        Quantity = quantity,
    });

    private static Inventory? FindInventory(IReadOnlyList<NodeEntry> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.Value is Inventory inv) return inv;
            if (n.Children is { Count: > 0 })
            {
                var found = FindInventory(n.Children);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static int Report(List<(bool Ok, string Message)> checks)
    {
        Console.WriteLine($"selftest: {checks.Count(c => c.Ok)}/{checks.Count} passed");
        foreach (var (ok, msg) in checks)
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {msg}");
        return checks.All(c => c.Ok) ? 0 : 1;
    }
}
