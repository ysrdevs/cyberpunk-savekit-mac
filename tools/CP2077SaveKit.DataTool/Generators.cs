using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CP2077SaveKit.Core;
using WolvenKit.RED4.Save;

namespace CP2077SaveKit.DataTool;

/// <summary>Regenerates the resources bundled in `src/CP2077SaveKit.Core/Resources` from the game.</summary>
public static class Generators
{
    private static readonly UTF8Encoding Utf8 = new(false);

    // ---------------------------------------------------------------- items.bin

    /// <summary>Rebuilds items.bin: the bundled name list plus names harvested from the game files.</summary>
    public static int Names(Cli opts)
    {
        var src = TweakDbSource.Load(opts.Game);
        var names = NameSource.Load(opts);
        var before = names.Count;

        var keys = src.AllKeys();
        Console.WriteLine($"\nTweakDB keys (records + flats): {keys.Count:n0}");
        var harvested = StringHarvester.Harvest(opts.Game, keys, Console.WriteLine).ToList();
        names.AddMissing(harvested);
        Console.WriteLine($"harvested names: {harvested.Count:n0}  (on top of {before:n0} bundled/extra, +{names.Count - before:n0} new)");

        var outPath = opts.Out ?? opts.ResourcePath("items.bin");
        WriteItemsBin(names, opts.ResourcePath("items.bin"), outPath);
        Console.WriteLine($"\nwrote {outPath}: {names.Count:n0} names");
        return 0;
    }

    /// <summary>
    /// items.bin for the loader in <see cref="TweakDbNames"/>: a 4-byte length, then a gzip stream of
    /// a 10-byte legacy header, then length-prefixed ASCII names. The loader starts at the first
    /// "Items." it finds, so those names are written first — names before it would never be read.
    /// </summary>
    private static void WriteItemsBin(NameSource names, string bundledPath, string outPath)
    {
        var legacyHeader = ReadLegacyHeader(bundledPath);

        using var raw = new MemoryStream();
        raw.Write(legacyHeader);
        var ordered = names.Sorted().OrderByDescending(n => n.StartsWith("Items.", StringComparison.Ordinal)).ToList();
        foreach (var name in ordered)
        {
            if (name.Length == 0 || name.Length > 255) continue;
            raw.WriteByte((byte)name.Length);
            raw.Write(Utf8.GetBytes(name));
        }
        raw.WriteByte(0);                       // terminator the loader stops at

        var payload = raw.ToArray();
        using var file = File.Create(outPath + ".tmp");
        using (var writer = new BinaryWriter(file, Utf8, true))
            writer.Write(payload.Length);
        using (var gz = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(payload, 0, payload.Length);
        file.Flush();
        File.Move(outPath + ".tmp", outPath, overwrite: true);
    }

    private static byte[] ReadLegacyHeader(string bundledPath)
    {
        try
        {
            if (File.Exists(bundledPath))
            {
                var data = File.ReadAllBytes(bundledPath);
                using var gz = new GZipStream(new MemoryStream(data, 4, data.Length - 4), CompressionMode.Decompress);
                var header = new byte[10];
                var read = gz.Read(header, 0, header.Length);
                if (read == header.Length && !header.Contains((byte)'I')) return header;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (could not reuse the bundled header: {ex.Message})");
        }
        return new byte[10];
    }

    // ---------------------------------------------------------- ItemClasses.json

    /// <summary>Adds structure metadata for item records the bundled ItemClasses.json does not cover.</summary>
    public static int Classes(Cli opts)
    {
        var src = TweakDbSource.Load(opts.Game);
        var names = NameSource.Load(opts);
        var path = opts.ResourcePath("ItemClasses.json");
        var classes = ItemClasses.Load(path);

        var items = src.ItemRecords().ToList();
        var classifier = TypeClassifier.Fit(items
            .Where(i => classes.Get(i.Hash) is { } c)
            .Select(i => (i, classes.Get(i.Hash)!.Type)));

        // Report how well the fitted rule reproduces the hand-validated entries before trusting it.
        var known = items.Where(i => classes.Get(i.Hash) is not null).ToList();
        var mismatches = known.Where(i => classifier.Classify(i) != classes.Get(i.Hash)!.Type).ToList();
        Console.WriteLine($"\ntype rule: reproduces {known.Count - mismatches.Count:n0}/{known.Count:n0} bundled types" +
                          $" ({100.0 * (known.Count - mismatches.Count) / Math.Max(1, known.Count):F2}%)");
        foreach (var m in mismatches.Take(5))
            Console.WriteLine($"   differs: {names.Describe(m.Hash)} [{m.GameClass}] fitted={classifier.Classify(m)} stored={classes.Get(m.Hash)!.Type}");

        var additions = items
            .Where(i => classes.Get(i.Hash) is null)
            .OrderBy(i => i.Hash)
            .Select(i => new
            {
                i.Hash,
                Type = classifier.Classify(i),
                i.IsSingleInstance,
                i.SlotParts,
            })
            .ToList();

        var outPath = opts.Out ?? path;
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(path)))
        using (var file = File.Create(outPath + ".tmp"))
        using (var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
            foreach (var add in additions)
            {
                writer.WritePropertyName(add.Hash.ToString());
                writer.WriteStartObject();
                writer.WriteString("Type", add.Type);
                writer.WriteBoolean("IsSingleInstance", add.IsSingleInstance);
                writer.WriteNumber("IsItemPlus", 0);
                writer.WritePropertyName("SlotParts");
                writer.WriteStartArray();
                foreach (var (part, slot) in add.SlotParts)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("ItemPartPreset", part);
                    writer.WriteNumber("Slot", slot);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        File.Move(outPath + ".tmp", outPath, overwrite: true);
        Console.WriteLine($"wrote {outPath}: {classes.Count:n0} entries + {additions.Count:n0} new");
        return 0;
    }

    // ----------------------------------------------------------- aio_catalog.json

    /// <summary>Adds the item records the curated catalog is missing to the Add-Item picker.</summary>
    public static int Catalog(Cli opts)
    {
        var src = TweakDbSource.Load(opts.Game);
        var names = NameSource.Load(opts);
        var path = opts.ResourcePath("aio_catalog.json");

        using var existing = JsonDocument.Parse(File.ReadAllBytes(path));
        var existingCount = existing.RootElement.GetArrayLength();
        var known = new HashSet<ulong>();
        foreach (var element in existing.RootElement.EnumerateArray())
            if (element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                known.Add(TweakDbNames.ParseId(id.GetString()!));

        var additions = new List<object>();
        foreach (var item in src.ItemRecords().OrderBy(i => i.Hash))
        {
            if (known.Contains(item.Hash)) continue;
            var type = ItemTypeLabel(item, names);
            additions.Add(new
            {
                id = names.Resolve(item.Hash) ?? $"0x{item.Hash:X16}",
                name = (string?)null,
                type,
                tier = TierLabel(item, names),
                sheet = SheetFor(item, names, type),
            });
        }

        var outPath = opts.Out ?? path;
        using (var file = File.Create(outPath + ".tmp"))
        using (var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (var element in existing.RootElement.EnumerateArray()) element.WriteTo(writer);
            foreach (var add in additions) JsonSerializer.Serialize(writer, add);
            writer.WriteEndArray();
        }
        File.Move(outPath + ".tmp", outPath, overwrite: true);

        var named = additions.Count(a => !((string)a.GetType().GetProperty("id")!.GetValue(a)!).StartsWith("0x", StringComparison.Ordinal));
        Console.WriteLine($"wrote {outPath}: {existingCount:n0} entries + {additions.Count:n0} new = {existingCount + additions.Count:n0}");
        Console.WriteLine($"   of the new entries, {named:n0} carry a code name and {additions.Count - named:n0} are hash-labelled (no local source names them)");
        return 0;
    }

    /// <summary>Item type in the catalog's ALL-CAPS style ("ItemType.Clo_OuterChest" -> "OUTER CHEST").</summary>
    private static string ItemTypeLabel(ItemRecord item, NameSource names)
    {
        var type = names.Resolve(item.ItemType);
        if (type is null)
        {
            return names.Resolve(item.ItemCategory) is { } category
                ? category.Replace("ItemCategory.", "", StringComparison.Ordinal)
                : item.GameClass.ToUpperInvariant();
        }

        type = type.Replace("ItemType.", "", StringComparison.Ordinal);
        var m = System.Text.RegularExpressions.Regex.Match(type, @"^[A-Za-z]{3}_(.+)$");
        if (m.Success) type = m.Groups[1].Value;
        type = System.Text.RegularExpressions.Regex.Replace(type, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return type.ToUpperInvariant();
    }

    /// <summary>Tier label in the catalog's style, from the item's quality (2.0 tiers).</summary>
    private static string? TierLabel(ItemRecord item, NameSource names) => names.Resolve(item.Quality) switch
    {
        "Quality.Common" => "TIER 1",
        "Quality.Uncommon" => "TIER 2",
        "Quality.Rare" => "TIER 3",
        "Quality.Epic" => "TIER 4",
        "Quality.Legendary" => "TIER 5",
        _ => null,
    };

    /// <summary>Category (sheet) for a new catalog entry, matching the picker's existing vocabulary.</summary>
    private static string SheetFor(ItemRecord item, NameSource names, string typeLabel)
    {
        var category = names.Resolve(item.ItemCategory)?.Replace("ItemCategory.", "", StringComparison.Ordinal);
        return category switch
        {
            "Weapon" => "WEAPONS",
            "Cyberware" => "CYBERWARE",
            "Clothing" => "CLOTHES",
            "Mods" => "MODS",
            "Part" => "MODS",
            "Gadget" => typeLabel.Contains("GRENADE", StringComparison.Ordinal) ? "WEAPONS" : "MISC",
            "Consumable" => "MISC",
            "General" => typeLabel == "CRAFTING MATERIAL" ? "CRAFTING" : "MISC",
            _ => item.GameClass switch
            {
                "WeaponItem" => "WEAPONS",
                "Clothing" => "CLOTHES",
                "ItemRecipe" => "CRAFTING",
                "ConsumableItem" => "MISC",
                "Gadget" or "Grenade" => "MISC",
                _ => item.IsSingleInstance ? "MISC" : "MODS",
            },
        };
    }

    // ------------------------------------------------------------------ verify

    /// <summary>
    /// Byte-compares two saves after decompression. A round trip through the editor must leave every
    /// byte of the original body in place; the compressed framing differs because the writer uses a
    /// different LZ4 level, which is harmless.
    /// </summary>
    public static int Verify(Cli opts)
    {
        if (opts.Args.Count < 2) { Console.Error.WriteLine("verify needs <original> <written>"); return 2; }
        using var a = File.OpenRead(opts.Args[0]);
        using var b = File.OpenRead(opts.Args[1]);
        var first = Compression.Decompress(a);
        var second = Compression.Decompress(b);

        Console.WriteLine($"original: {first.Length:n0} B decompressed");
        Console.WriteLine($"written:  {second.Length:n0} B decompressed");
        if (first.AsSpan().SequenceEqual(second))
        {
            Console.WriteLine("IDENTICAL: the written save preserves every decompressed byte.");
            return 0;
        }

        var limit = Math.Min(first.Length, second.Length);
        var differing = 0;
        var firstDiff = -1;
        for (var i = 0; i < limit; i++)
        {
            if (first[i] == second[i]) continue;
            if (firstDiff < 0) firstDiff = i;
            differing++;
        }
        Console.WriteLine($"DIFFERS: {differing:n0} differing bytes in the common {limit:n0} B" +
                          (first.Length != second.Length ? $", plus {Math.Abs(first.Length - second.Length):n0} B length difference" : ""));
        Console.WriteLine($"first difference at offset 0x{firstDiff:X} ({firstDiff:n0})");
        return 1;
    }
}
