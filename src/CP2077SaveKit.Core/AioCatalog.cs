using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP2077SaveKit.Core;

public sealed class CatalogItem
{
    [JsonPropertyName("id")]    public string Id { get; set; } = "";
    [JsonPropertyName("name")]  public string? Name { get; set; }
    [JsonPropertyName("type")]  public string? Type { get; set; }
    [JsonPropertyName("tier")]  public string? Tier { get; set; }
    [JsonPropertyName("sheet")] public string? Sheet { get; set; }

    /// <summary>Best label for a picker: friendly name + type, falling back to the id.</summary>
    public string Label =>
        (Name is { Length: > 0 } ? Name : Id) +
        (Type is { Length: > 0 } ? $"  ·  {Type}" : "") +
        (Tier is { Length: > 0 } and not "-" ? $"  ·  {Tier}" : "");
}

/// <summary>
/// Curated item catalog parsed from the "AIO Command List" (v10.1): ~7.5k spawnable items
/// with friendly names, categories and tiers. Current to patch 2.x. Used to (a) prettify
/// inventory names and (b) power the Add-Item picker.
/// </summary>
public sealed class AioCatalog
{
    public IReadOnlyList<CatalogItem> Items { get; }
    private readonly Dictionary<ulong, string> _friendlyByHash;
    private readonly Dictionary<ulong, string> _idByHash;

    // Sheets whose NAME column holds a real item name. MISC/CRAFTING use the NAME column for
    // command *descriptions* ("+ 500,000 €$"), so we exclude them from name resolution (but keep
    // them in the catalog for the Add-Item picker). items.bin resolves those to clean code-names.
    private static readonly HashSet<string> NameSheets = new(StringComparer.OrdinalIgnoreCase)
        { "WEAPONS", "CYBERWARE", "CLOTHES", "MODS", "VEHICLES" };

    private AioCatalog(List<CatalogItem> items)
    {
        Items = items;
        _friendlyByHash = new Dictionary<ulong, string>(items.Count);
        _idByHash = new Dictionary<ulong, string>(items.Count);
        foreach (var it in items)
        {
            var h = TweakDbNames.TweakHash(it.Id);
            _idByHash.TryAdd(h, it.Id);
            if (string.IsNullOrEmpty(it.Name)) continue;
            if (it.Sheet is null || !NameSheets.Contains(it.Sheet)) continue;
            _friendlyByHash.TryAdd(h, it.Name);
        }
    }

    /// <summary>Friendly name (real item-name sheets only), or null.</summary>
    public string? FriendlyName(ulong hash) => _friendlyByHash.TryGetValue(hash, out var n) ? n : null;

    /// <summary>The TweakDB code id for any catalog item (e.g. "Items.CommonMaterial1").</summary>
    public string? CodeId(ulong hash) => _idByHash.TryGetValue(hash, out var n) ? n : null;

    public IEnumerable<CatalogItem> Search(string term, int limit = 300) => Search(Items, term, limit);

    // Sheets whose items are stackable (quantity-structured) and therefore safe to construct with
    // the current Add-Item path. Weapons/Cyberware/Clothes/Mods need extended structure (mod slots).
    private static readonly HashSet<string> StackableSheets = new(StringComparer.OrdinalIgnoreCase)
        { "MISC", "CRAFTING" };

    private List<CatalogItem>? _stackable;
    public IReadOnlyList<CatalogItem> StackableItems =>
        _stackable ??= Items.Where(i => i.Sheet is not null && StackableSheets.Contains(i.Sheet)).ToList();

    public IEnumerable<CatalogItem> SearchStackable(string term, int limit = 300) =>
        Search(StackableItems, term, limit);

    private static IEnumerable<CatalogItem> Search(IEnumerable<CatalogItem> src, string term, int limit)
    {
        if (string.IsNullOrWhiteSpace(term)) return src.Take(limit);
        return src.Where(i =>
            (i.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            i.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (i.Type?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(limit);
    }

    private static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Resources", "aio_catalog.json");
    private static AioCatalog? _shared;
    public static AioCatalog Shared => _shared ??= Load(DefaultPath);

    public static AioCatalog Load(string path)
    {
        var items = JsonSerializer.Deserialize<List<CatalogItem>>(File.ReadAllText(path)) ?? new();
        return new AioCatalog(items);
    }
}
