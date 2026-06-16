using WolvenKit.RED4.Save;
using WolvenKit.RED4.Save.Classes;
using WolvenKit.RED4.Types;

namespace CP2077SaveKit.Core;

/// <summary>Mutations on the parsed inventory tree. Edits the live WolvenKit objects;
/// SaveFile.Save then re-serializes with correct sizes/hashes.</summary>
public static class InventoryEditor
{
    public const ulong MoneyHash = 0x0000000bf5e188ec; // Items.money

    /// <summary>Set the quantity of the first item matching idHash. Returns true if found.</summary>
    public static bool SetQuantity(SaveFile save, ulong idHash, uint quantity)
    {
        foreach (var item in EnumerateItems(save))
        {
            if ((ulong)item.ItemInfo.ItemId.Id == idHash)
            {
                item.Quantity = quantity;
                return true;
            }
        }
        return false;
    }

    public static bool SetMoney(SaveFile save, uint amount) => SetQuantity(save, MoneyHash, amount);

    /// <summary>Read current quantity for an item hash (0 if absent).</summary>
    public static uint GetQuantity(SaveFile save, ulong idHash)
    {
        foreach (var item in EnumerateItems(save))
            if ((ulong)item.ItemInfo.ItemId.Id == idHash)
                return item.Quantity;
        return 0;
    }

    /// <summary>
    /// Add a stackable item (money/ammo/components/consumables/grenades) by TweakDBID hash.
    /// If it already exists, just sets the quantity. Clones an existing quantity-structured
    /// item's header so seed/counter conventions match the save. Returns false if no inventory.
    /// NOTE: stackable items only — weapons/clothing with mod slots need extended structure.
    /// </summary>
    public static bool AddOrSetStackable(SaveFile save, ulong idHash, uint quantity, ulong subInvId = 1)
    {
        var node = FindNode(save.Nodes, Constants.NodeNames.INVENTORY);
        if (node?.Value is not Inventory inv || inv.SubInventories.Count == 0) return false;

        var sub = inv.SubInventories.FirstOrDefault(s => s.InventoryId == subInvId) ?? inv.SubInventories[0];

        var existing = sub.Items.FirstOrDefault(i => (ulong)i.ItemInfo.ItemId.Id == idHash);
        if (existing is not null) { existing.Quantity = quantity; return true; }

        // template header from any existing quantity-structured item (e.g. ammo/money)
        var tmpl = inv.SubInventories.SelectMany(s => s.Items)
                      .FirstOrDefault(i => i.ItemInfo.ItemStructure == ItemStructure.Quantity);

        var item = new ItemData
        {
            ItemInfo = new ItemInfo
            {
                ItemId = new gameItemID
                {
                    Id = idHash,
                    RngSeed = tmpl?.ItemInfo.ItemId.RngSeed ?? 2u,
                    UniqueCounter = 0,
                    Flags = 0,
                },
                ItemStructure = ItemStructure.Quantity,
            },
            Flags = 0,
            CreationTime = 0,
            Quantity = quantity,
        };
        sub.Items.Add(item);
        return true;
    }

    private static readonly Random _rng = new();

    /// <summary>
    /// Add ANY item (weapons/clothing/cyberware/mods/stackables) by TweakDBID hash, building the
    /// correct structure from ItemClasses metadata (mirrors CyberCAT). Stackables stack/set qty;
    /// extended items (gear) get a fresh instance with default mod-slot parts. Returns the item Type
    /// added, or null if no inventory. Note: stat data is NOT synthesized — the game fills weapon/
    /// clothing stats on load (verify in-game).
    /// </summary>
    public static string? AddItem(SaveFile save, ulong idHash, uint quantity, ulong subInvId = 1)
    {
        var node = FindNode(save.Nodes, Constants.NodeNames.INVENTORY);
        if (node?.Value is not Inventory inv || inv.SubInventories.Count == 0) return null;
        var sub = inv.SubInventories.FirstOrDefault(s => s.InventoryId == subInvId) ?? inv.SubInventories[0];

        var rec = ItemClasses.Shared.Get(idHash);
        var type = rec?.Type ?? "";
        var single = rec?.IsSingleInstance ?? true;   // unknown -> treat as stackable
        int counter = NextUniqueCounter(inv);

        var item = new ItemData
        {
            ItemInfo = new ItemInfo
            {
                ItemId = new gameItemID { Id = idHash, UniqueCounter = (ushort)counter++ },
            },
        };

        if (type == "Grenade")
        {
            item.ItemInfo.ItemStructure = ItemStructure.Quantity | ItemStructure.Extended;
            item.ItemInfo.ItemId.RngSeed = 2;
            item.Quantity = quantity;
            item.ItemAdditionalInfo = new ItemAdditionalInfo();
            item.ItemSlotPart = EmptySlotPart();
        }
        else if (single)
        {
            // stackable (consumable / component / ammo / money): if present, just set quantity
            var existing = sub.Items.FirstOrDefault(i => (ulong)i.ItemInfo.ItemId.Id == idHash);
            if (existing is not null) { existing.Quantity = quantity; return type; }
            item.ItemInfo.ItemStructure = ItemStructure.None;
            item.ItemInfo.ItemId.RngSeed = 2;
            item.Quantity = quantity;
        }
        else
        {
            // weapon / clothing / cyberware / mod: extended structure
            item.ItemInfo.ItemStructure = ItemStructure.None;
            item.ItemInfo.ItemId.RngSeed = UniqueSeed();
            item.ItemAdditionalInfo = new ItemAdditionalInfo();
            item.ItemSlotPart = EmptySlotPart();
        }

        if (item.HasExtendedData() && rec is { SlotParts.Count: > 0 })
        {
            item.ItemSlotPart = MakeSlotPart(rec.SlotParts[0], ref counter);
            if (rec.SlotParts.Count > 1)
            {
                var children = new List<ItemSlotPart>();
                for (int i = 1; i < rec.SlotParts.Count; i++)
                    children.Add(MakeSlotPart(rec.SlotParts[i], ref counter));
                item.ItemSlotPart.Children = children;
            }
        }

        sub.Items.Add(item);
        return type;
    }

    private static ItemSlotPart EmptySlotPart() => new()
    {
        ItemInfo = new ItemInfo { ItemId = new gameItemID() },
        AppearanceName = "None",
        ItemAdditionalInfo = new ItemAdditionalInfo(),
    };

    private static ItemSlotPart MakeSlotPart(SlotPartRecord sp, ref int counter) => new()
    {
        ItemInfo = new ItemInfo
        {
            ItemId = new gameItemID { Id = sp.ItemPartPreset, RngSeed = UniqueSeed(), UniqueCounter = (ushort)counter++ },
        },
        AppearanceName = "None",
        AttachmentSlotTdbId = sp.Slot,
        ItemAdditionalInfo = new ItemAdditionalInfo { RequiredLevel = float.MaxValue },
    };

    private static uint UniqueSeed()
    {
        uint s;
        do { s = (uint)_rng.Next(3, int.MaxValue); } while (s == 2);
        return s;
    }

    private static int NextUniqueCounter(Inventory inv)
    {
        int max = 1;
        foreach (var sub in inv.SubInventories)
            foreach (var it in sub.Items)
                if (it.ItemInfo.ItemId.UniqueCounter > max) max = it.ItemInfo.ItemId.UniqueCounter;
        return max + 1;
    }

    private static IEnumerable<ItemData> EnumerateItems(SaveFile save)
    {
        var node = FindNode(save.Nodes, Constants.NodeNames.INVENTORY);
        if (node?.Value is not Inventory inv) yield break;
        foreach (var sub in inv.SubInventories)
            foreach (var item in sub.Items)
                yield return item;
    }

    private static NodeEntry? FindNode(IReadOnlyList<NodeEntry> nodes, string name)
    {
        foreach (var n in nodes)
        {
            if (n.Name == name) return n;
            if (n.Children is { Count: > 0 })
            {
                var f = FindNode(n.Children, name);
                if (f is not null) return f;
            }
        }
        return null;
    }
}
