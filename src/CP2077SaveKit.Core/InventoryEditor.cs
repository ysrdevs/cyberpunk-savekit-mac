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
