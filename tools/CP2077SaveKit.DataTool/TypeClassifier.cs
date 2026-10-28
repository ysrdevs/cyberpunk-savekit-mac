using CP2077SaveKit.Core;

namespace CP2077SaveKit.DataTool;

/// <summary>
/// Reproduces the bundled ItemClasses "Type" labels for records the bundle does not cover yet.
/// The labels are not the game's record classes (the shipped TweakDB groups most items under
/// `gamedataItem_Record`), so the rule is fitted from the entries that are already in the bundle:
/// the item's `itemCategory` decides, with the record class as the fallback. Fitting instead of
/// hardcoding keeps this working when a later patch renames a category.
/// </summary>
public sealed class TypeClassifier
{
    private readonly Dictionary<(string GameClass, ulong Category), string> _byPair = new();
    private readonly Dictionary<ulong, string> _byCategory = new();
    private readonly Dictionary<string, string> _byClass = new();

    public static TypeClassifier Fit(IEnumerable<(ItemRecord Item, string StoredType)> known)
    {
        var pairs = new Dictionary<(string, ulong), Dictionary<string, int>>();
        var categories = new Dictionary<ulong, Dictionary<string, int>>();
        var classes = new Dictionary<string, Dictionary<string, int>>();

        foreach (var (item, type) in known)
        {
            Tally(pairs, (item.GameClass, item.ItemCategory), type);
            Tally(categories, item.ItemCategory, type);
            Tally(classes, item.GameClass, type);
        }

        var classifier = new TypeClassifier();
        foreach (var (key, counts) in pairs) classifier._byPair[key] = Dominant(counts);
        foreach (var (key, counts) in categories) classifier._byCategory[key] = Dominant(counts);
        foreach (var (key, counts) in classes) classifier._byClass[key] = Dominant(counts);
        return classifier;

        static void Tally<TKey>(Dictionary<TKey, Dictionary<string, int>> map, TKey key, string type)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out var counts)) map[key] = counts = new Dictionary<string, int>();
            counts[type] = counts.GetValueOrDefault(type) + 1;
        }

        static string Dominant(Dictionary<string, int> counts) =>
            counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
    }

    /// <summary>Type label for a record: category + class first, then category, then class.</summary>
    public string Classify(ItemRecord item)
    {
        if (_byPair.TryGetValue((item.GameClass, item.ItemCategory), out var byPair)) return byPair;
        if (_byCategory.TryGetValue(item.ItemCategory, out var byCategory)) return byCategory;
        if (_byClass.TryGetValue(item.GameClass, out var byClass)) return byClass;
        return "General";
    }

    public string? CategoryName(ulong itemCategory, NameSource names)
    {
        var resolved = names.Resolve(itemCategory);
        if (resolved is null) return null;
        return resolved.StartsWith("ItemCategory.", StringComparison.Ordinal) ? resolved["ItemCategory.".Length..] : resolved;
    }
}
