using System.Text.Json;

namespace CP2077SaveKit.Core;

public sealed record SlotPartRecord(ulong ItemPartPreset, ulong Slot);
public sealed record ItemClassRecord(string Type, bool IsSingleInstance, IReadOnlyList<SlotPartRecord> SlotParts);

/// <summary>
/// Per-item structure metadata (CyberCAT's ItemClasses.json): item Type, whether it is a single
/// stackable instance, and the default mod-slot parts to attach. Drives correct construction of
/// weapons/clothing/cyberware (which need extended structure + slot parts), not just stackables.
/// </summary>
public sealed class ItemClasses
{
    private readonly Dictionary<ulong, ItemClassRecord> _byHash;
    private ItemClasses(Dictionary<ulong, ItemClassRecord> map) => _byHash = map;

    public ItemClassRecord? Get(ulong hash) => _byHash.TryGetValue(hash, out var r) ? r : null;

    private static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Resources", "ItemClasses.json");
    private static ItemClasses? _shared;
    public static ItemClasses Shared => _shared ??= Load(DefaultPath);

    public static ItemClasses Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var map = new Dictionary<ulong, ItemClassRecord>(doc.RootElement.GetRawText().Length / 80);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!ulong.TryParse(prop.Name, out var hash)) continue;
            var el = prop.Value;
            var type = el.TryGetProperty("Type", out var t) ? t.GetString() ?? "" : "";
            var single = el.TryGetProperty("IsSingleInstance", out var s) && s.GetBoolean();
            var parts = new List<SlotPartRecord>();
            if (el.TryGetProperty("SlotParts", out var sp) && sp.ValueKind == JsonValueKind.Array)
                foreach (var p in sp.EnumerateArray())
                    parts.Add(new SlotPartRecord(
                        p.GetProperty("ItemPartPreset").GetUInt64(),
                        p.GetProperty("Slot").GetUInt64()));
            map[hash] = new ItemClassRecord(type, single, parts);
        }
        return new ItemClasses(map);
    }
}
