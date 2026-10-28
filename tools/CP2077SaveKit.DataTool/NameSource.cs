using System.Text.Json;
using CP2077SaveKit.Core;

namespace CP2077SaveKit.DataTool;

/// <summary>
/// Hash -> TweakDB name lookups for the tool. The shipped TweakDB blobs store hashes only, so the
/// bundled `items.bin` list is the base, and any `--extra` file (a `.txt` of names, or JSON holding
/// strings or objects with an `id`) is layered on top. Extra entries win: they are the newer list.
/// </summary>
public sealed class NameSource
{
    private readonly Dictionary<ulong, string> _byHash = new();
    private readonly List<string> _all = new();

    public int BundledCount { get; private set; }
    public int ExtraCount { get; private set; }
    public IReadOnlyList<string> All => _all;

    public string? Resolve(ulong hash) => _byHash.TryGetValue(hash, out var name) ? name : null;

    public int Count => _byHash.Count;

    /// <summary>Adds names that are not in the list yet (the list's own entries win).</summary>
    public void AddMissing(IEnumerable<string> names)
    {
        foreach (var name in names) Add(name);
    }

    public string Describe(ulong hash) => Resolve(hash) ?? $"0x{hash:X16}";

    public static NameSource Load(Cli opts)
    {
        var source = new NameSource();

        var itemsBin = opts.ResourcePath("items.bin");
        if (File.Exists(itemsBin))
        {
            var bundled = TweakDbNames.Load(itemsBin);
            foreach (var name in bundled.AllNames()) source.Add(name);
            source.BundledCount = bundled.Count;
        }

        foreach (var file in opts.Extra)
        {
            var before = source._byHash.Count;
            foreach (var name in ReadNames(file)) source.Add(name, overwrite: true);
            source.ExtraCount += source._byHash.Count - before;
            Console.WriteLine($"extra names from {Path.GetFileName(file)}: +{source._byHash.Count - before:n0}");
        }

        return source;
    }

    private void Add(string name, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return;
        foreach (var c in name)
            if (c < 0x20 || c > 0x7E) return;             // items.bin holds plain ASCII, length-prefixed

        var hash = TweakDbNames.TweakHash(name);
        if (_byHash.TryGetValue(hash, out var existing) && !overwrite) return;
        if (existing is null) _all.Add(name);
        _byHash[hash] = name;
    }

    private static IEnumerable<string> ReadNames(string path)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return File.ReadLines(path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var names = new List<string>();
        switch (doc.RootElement.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String) names.Add(element.GetString()!);
                    else if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        names.Add(id.GetString()!);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in doc.RootElement.EnumerateObject()) names.Add(property.Name);
                break;
        }
        return names;
    }

    /// <summary>Sorted names for a stable items.bin (the bundled list is unordered).</summary>
    public IEnumerable<string> Sorted() => _byHash.Values.OrderBy(n => n, StringComparer.Ordinal);
}
