using WolvenKit.RED4.Save;
using WolvenKit.RED4.Types;

namespace CP2077SaveKit.Core;

/// <summary>A single item in a sub-inventory, surfaced for the UI/CLI.</summary>
public sealed record InventoryItem(ulong IdHash, string? Name, uint Quantity, byte Flags)
{
    public string Display => Name ?? $"<unresolved 0x{IdHash:X16}>";
}

public sealed record SubInventoryView(ulong InventoryId, IReadOnlyList<InventoryItem> Items);

/// <summary>Reads the parsed `inventory` node into a flat, UI-friendly view.</summary>
public static class InventoryReader
{
    public static IReadOnlyList<SubInventoryView> Read(SaveFile save)
    {
        var node = FindInventoryNode(save.Nodes)
            ?? throw new SaveLoadException("No `inventory` node found in save.");

        if (node.Value is not Inventory inv)
            throw new SaveLoadException($"`inventory` node did not parse to Inventory (got {node.Value?.GetType().Name ?? "null"}).");

        var result = new List<SubInventoryView>();
        foreach (var sub in inv.SubInventories)
        {
            var items = new List<InventoryItem>(sub.Items.Count);
            foreach (var item in sub.Items)
            {
                var tdbid = item.ItemInfo.ItemId.Id;
                ulong hash = tdbid;                       // TweakDBID -> ulong (implicit)
                // friendly name (curated) -> items.bin code-name -> AIO code id (current items) -> hash
                string? name = AioCatalog.Shared.FriendlyName(hash)
                             ?? TweakDbNames.Shared.Resolve(hash)
                             ?? AioCatalog.Shared.CodeId(hash);
                items.Add(new InventoryItem(hash, name, item.Quantity, (byte)item.Flags));
            }
            result.Add(new SubInventoryView(sub.InventoryId, items));
        }
        return result;
    }

    private static NodeEntry? FindInventoryNode(IReadOnlyList<NodeEntry> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.Name == Constants.NodeNames.INVENTORY) return n;
            if (n.Children is { Count: > 0 })
            {
                var found = FindInventoryNode(n.Children);
                if (found is not null) return found;
            }
        }
        return null;
    }
}
