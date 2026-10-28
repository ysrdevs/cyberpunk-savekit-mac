using System.Text;
using WolvenKit.RED4.TweakDB;
using WolvenKit.RED4.Types;

namespace CP2077SaveKit.DataTool;

/// <summary>The per-item fields this project needs out of TweakDB, read from the game's own blobs.</summary>
public sealed record ItemRecord(
    ulong Hash,
    string GameClass,                                        // record class, e.g. "WeaponItem"
    bool IsSingleInstance,
    bool IsPart,                                             // parts/clothing attachments vs standalone items
    bool IsGarment,
    ulong ItemType,                                          // TweakDBID -> ItemType.Weapons, ...
    ulong ItemCategory,
    ulong Quality,
    string? LocalizedName,                                   // CString: often a LocKey, sometimes a plain name
    string? FriendlyName,                                    // CString: developer-facing name
    IReadOnlyList<string> Tags,
    IReadOnlyList<(ulong ItemPartPreset, ulong Slot)> SlotParts)
{
    /// <summary>Slot parts are written to ItemClasses.json in this order; stable for diffing.</summary>
    public string SlotPartsKey => string.Join("|", SlotParts.Select(p => $"{p.ItemPartPreset}:{p.Slot}"));
}

/// <summary>
/// Reads `r6/cache/tweakdb.bin` (+ `tweakdb_ep1.bin`, which overlays it) with WolvenKit's reader.
/// Records are enumerated by hash — the shipped TweakDB stores hashes, not names, so naming is a
/// separate concern (see NameSource).
/// </summary>
public sealed class TweakDbSource
{
    private readonly List<TweakDB> _dbs = new();
    private readonly List<string> _paths = new();
    private readonly Dictionary<ulong, (Type Type, int Db)> _records = new();

    public int RecordCount => _records.Count;
    public IReadOnlyList<string> Paths => _paths;

    public static TweakDbSource Load(string gameDir)
    {
        var cache = Path.Combine(gameDir, "r6", "cache");
        var files = new[] { "tweakdb.bin", "tweakdb_ep1.bin" }
            .Select(f => Path.Combine(cache, f))
            .Where(File.Exists)
            .ToArray();

        if (files.Length == 0)
            throw new FileNotFoundException($"No tweakdb blobs under {cache}. Point --game at the Cyberpunk 2077 install folder.");

        var src = new TweakDbSource();
        foreach (var file in files)
        {
            using var fs = File.OpenRead(file);
            var reader = new TweakDBReader(fs);
            var result = reader.ReadFile(out var db);
            if (result != EFileReadErrorCodes.NoError || db is null)
                throw new InvalidDataException($"{file}: {result}");

            var index = src._dbs.Count;
            src._dbs.Add(db);
            src._paths.Add(file);

            // Later files overlay earlier ones.
            foreach (var (id, type) in db.Records)
                src._records[(ulong)id] = (type, index);

            Console.WriteLine($"loaded {Path.GetFileName(file)}: {db.Records.Count:n0} records, {db.Flats.Count():n0} flats");
        }
        return src;
    }

    private static readonly Type ItemBase = typeof(gamedataItem_Record);

    /// <summary>Record type for a hash, or null when the record is absent or of an unknown type.</summary>
    public Type? RecordType(ulong hash) => _records.TryGetValue(hash, out var r) ? r.Type : null;

    public IEnumerable<(ulong Hash, Type Type)> AllRecords() => _records.Select(kv => (kv.Key, kv.Value.Type));

    public bool IsItemRecord(ulong hash) => RecordType(hash) is { } t && ItemBase.IsAssignableFrom(t);

    /// <summary>Every key in the loaded blobs (record ids and flat keys) — used to validate names.</summary>
    public HashSet<ulong> AllKeys()
    {
        var keys = new HashSet<ulong>(_records.Count + (1 << 22));
        foreach (var db in _dbs)
        {
            foreach (var (id, _) in db.Records) keys.Add((ulong)id);
            foreach (var flat in db.Flats.GetRecords()) keys.Add((ulong)flat);
        }
        return keys;
    }

    public static string GameClassOf(Type recordType) =>
        recordType.Name.StartsWith("gamedata", StringComparison.Ordinal) && recordType.Name.EndsWith("_Record", StringComparison.Ordinal)
            ? recordType.Name["gamedata".Length..^"_Record".Length]
            : recordType.Name;

    private gamedataItem_Record? Full(ulong hash)
    {
        if (!_records.TryGetValue(hash, out var entry)) return null;
        var db = _dbs[entry.Db];
        var record = db.GetFullRecord((TweakDBID)hash);
        return record as gamedataItem_Record;
    }

    /// <summary>Every item record in the loaded blobs, with the fields the editor needs.</summary>
    public IEnumerable<ItemRecord> ItemRecords()
    {
        foreach (var (hash, entry) in _records)
        {
            if (!ItemBase.IsAssignableFrom(entry.Type)) continue;
            var rec = Full(hash);
            if (rec is null) continue;

            var slots = new List<(ulong, ulong)>();
            foreach (var presetId in rec.SlotPartListPreset)
            {
                var preset = _dbs[entry.Db].GetFullRecord((TweakDBID)(ulong)presetId) as gamedataSlotItemPartPreset_Record;
                if (preset is null) continue;

                // Entries with an empty part preset are slots without a default part; the bundled
                // metadata (and the constructor) only carries real parts, so they are dropped here.
                if ((ulong)preset.ItemPartPreset == 0) continue;
                slots.Add(((ulong)preset.ItemPartPreset, (ulong)preset.Slot));
            }

            yield return new ItemRecord(
                hash,
                GameClassOf(entry.Type),
                (bool)rec.IsSingleInstance,
                (bool)rec.IsPart,
                (bool)rec.IsGarment,
                (ulong)rec.ItemType,
                (ulong)rec.ItemCategory,
                (ulong)rec.Quality,
                NullIfEmpty((string?)rec.LocalizedName),
                NullIfEmpty((string?)rec.FriendlyName),
                rec.Tags.Select(t => (string)t).ToList(),
                slots);
        }
    }

    /// <summary>Every property of a record, formatted for the `dump` command.</summary>
    public IReadOnlyDictionary<string, string> Dump(ulong hash, Func<ulong, string?> resolveName)
    {
        var result = new Dictionary<string, string>();
        var entry = _records.TryGetValue(hash, out var e) ? e : default;
        var record = _records.ContainsKey(hash) ? _dbs[entry.Db].GetFullRecord((TweakDBID)hash) : null;
        if (record is null) return result;

        var typeInfo = RedReflection.GetTypeInfo(record);
        foreach (var property in typeInfo.PropertyInfos)
        {
            if (property.RedName is null) continue;
            var value = record.GetProperty(property.RedName);
            if (value is null) continue;
            if (typeInfo.SerializeDefault == false && property.IsDefault(value)) continue;
            result[property.RedName] = Format(value, resolveName);
        }
        return result;
    }

    private static string Format(IRedType value, Func<ulong, string?> resolveName)
    {
        switch (value)
        {
            case TweakDBID id:
            {
                var name = resolveName((ulong)id);
                return name is null ? $"0x{(ulong)id:X16}" : $"{name} (0x{(ulong)id:X16})";
            }
            case IRedArray array:
                return "[" + string.Join(", ", array.Cast<IRedType>().Select(v => Format(v, resolveName))) + "]";
            default:
                return value.ToString() ?? "";
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
