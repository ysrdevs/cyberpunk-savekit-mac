using WolvenKit.RED4.Save;
using WolvenKit.RED4.Save.Classes;

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
