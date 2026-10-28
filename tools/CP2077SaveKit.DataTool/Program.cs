using CP2077SaveKit.Core;
using CP2077SaveKit.DataTool;

// Data refresh tool: reads the installed game's TweakDB blobs and reports/regenerates the bundled
// resources (item names, item structure metadata, add-item catalog).
//
//   datatool diag    --game "<install dir>" [--resources <dir>]
//   datatool dump    --game "<install dir>" <Items.Name|0xhash>
//   datatool names   --game "<install dir>" [--resources <dir>] [--out items.bin] [--extra <file>…]
//   datatool classes --game "<install dir>" [--resources <dir>] [--out ItemClasses.json]
//
// The game must be installed locally: `tweakdb.bin` and `tweakdb_ep1.bin` are read straight from
// r6/cache. Names are not stored in those blobs (only 32-bit CRC hashes), so `names` merges the
// bundled list with any extra name list passed with --extra.

var opts = Cli.Parse(args);
if (opts is null) return 2;

try
{
    switch (opts.Command)
    {
        case "diag": return Diag(opts);
        case "dump": return Dump(opts);
        case "mapping": return Mapping(opts);
        case "slots": return Slots(opts);
        case "names": return Generators.Names(opts);
        case "classes": return Generators.Classes(opts);
        case "catalog": return Generators.Catalog(opts);
        case "verify": return Generators.Verify(opts);
        default:
            Console.Error.WriteLine($"unknown command: {opts.Command}");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static int Diag(Cli opts)
{
    var src = TweakDbSource.Load(opts.Game);
    var names = NameSource.Load(opts);
    var classes = ItemClasses.Load(opts.ResourcePath("ItemClasses.json"));

    Console.WriteLine($"\nbundled names: {names.BundledCount:n0}  (extra: {names.ExtraCount:n0})");
    Console.WriteLine($"bundled ItemClasses entries: {classes.Count:n0}");

    // 1. Item records by game class, and how many the bundled data can name / describes.
    var byClass = new SortedDictionary<string, (int Total, int Named, int InClasses)>();
    var items = new List<ItemRecord>();
    foreach (var record in src.ItemRecords())
    {
        items.Add(record);
        var named = names.Resolve(record.Hash) is not null;
        var described = classes.Get(record.Hash) is not null;
        byClass.TryGetValue(record.GameClass, out var acc);
        byClass[record.GameClass] = (acc.Total + 1, acc.Named + (named ? 1 : 0), acc.InClasses + (described ? 1 : 0));
    }

    Console.WriteLine($"\nitem records: {items.Count:n0}");
    Console.WriteLine($"{"class",-16}{"total",8}{"named",8}{"in ItemClasses",16}{"missing both",14}");
    foreach (var (cls, a) in byClass)
        Console.WriteLine($"{cls,-16}{a.Total,8:n0}{a.Named,8:n0}{a.InClasses,16:n0}{a.Total - a.Named,14:n0}");

    var unnamed = items.Where(i => names.Resolve(i.Hash) is null).ToList();
    var notDescribed = items.Where(i => classes.Get(i.Hash) is null).ToList();
    var entirelyNew = items.Where(i => names.Resolve(i.Hash) is null && classes.Get(i.Hash) is null).ToList();
    Console.WriteLine($"\nunnamed: {unnamed.Count:n0}   not in ItemClasses: {notDescribed.Count:n0}   neither: {entirelyNew.Count:n0}");
    Console.WriteLine($"newest unnamed item hashes: {string.Join(", ", unnamed.Take(5).Select(i => $"0x{i.Hash:X16}({i.GameClass})"))}");

    // 2. Does the derivation from TweakDB agree with the bundled (hand-validated) metadata?
    var typeMatrix = new SortedDictionary<(string Game, string Stored), int>();
    var singleAgree = new Dictionary<string, (int Agree, int Disagree)>();
    int slotExact = 0, slotDiff = 0, slotMissing = 0;
    foreach (var item in items)
    {
        var stored = classes.Get(item.Hash);
        if (stored is null) continue;
        var key = (item.GameClass, stored.Type);
        typeMatrix[key] = typeMatrix.GetValueOrDefault(key) + 1;

        singleAgree.TryGetValue(item.GameClass, out var acc);
        if (stored.IsSingleInstance == item.IsSingleInstance) acc.Agree++; else acc.Disagree++;
        singleAgree[item.GameClass] = acc;

        var storedKey = string.Join("|", stored.SlotParts.Select(p => $"{p.ItemPartPreset}:{p.Slot}"));
        if (stored.SlotParts.Count == 0 && item.SlotParts.Count == 0) slotExact++;
        else if (storedKey == item.SlotPartsKey) slotExact++;
        else slotDiff++;
    }
    _ = slotMissing;

    Console.WriteLine("\nclass -> stored Type (for records present in the bundled ItemClasses):");
    foreach (var ((game, stored), count) in typeMatrix.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"   {game,-16} -> {stored,-12} {count,6:n0}");

    Console.WriteLine("\nisSingleInstance agreement:");
    foreach (var (cls, a) in singleAgree.OrderBy(k => k.Key))
        Console.WriteLine($"   {cls,-16} agree={a.Agree,6:n0}  disagree={a.Disagree,5:n0}");

    Console.WriteLine($"\nslot parts: exact match {slotExact:n0}, differ {slotDiff:n0}");

    // 3. Recurring item types/qualities, so the catalog generator can label new items.
    Console.WriteLine("\ntop item types (new/unnameable items only):");
    foreach (var g in unnamed.GroupBy(i => i.ItemType).OrderByDescending(g => g.Count()).Take(12))
        Console.WriteLine($"   {names.Describe(g.Key),-34} {g.Count(),6:n0}");
    Console.WriteLine("\ntop qualities (new/unnameable items only):");
    foreach (var g in unnamed.GroupBy(i => i.Quality).OrderByDescending(g => g.Count()).Take(10))
        Console.WriteLine($"   {names.Describe(g.Key),-24} {g.Count(),6:n0}");

    return 0;
}

/// <summary>
/// Shows how the bundled ItemClasses "Type" values relate to fields the game blobs expose, so the
/// regex-free classification used for new records can be fitted to the hand-validated file.
/// </summary>
static int Mapping(Cli opts)
{
    var src = TweakDbSource.Load(opts.Game);
    var names = NameSource.Load(opts);
    var classes = ItemClasses.Load(opts.ResourcePath("ItemClasses.json"));

    var rows = src.ItemRecords().Select(i => (Item: i, Stored: classes.Get(i.Hash))).Where(r => r.Stored is not null).ToList();

    foreach (var group in rows.GroupBy(r => r.Stored!.Type).OrderByDescending(g => g.Count()))
    {
        Console.WriteLine($"\n{group.Key}  ({group.Count():n0})");
        var combos = group.GroupBy(r => (
                r.Item.GameClass,
                Cat: r.Item.ItemCategory == 0 ? "-" : names.Describe(r.Item.ItemCategory),
                Type: r.Item.ItemType == 0 ? "-" : names.Describe(r.Item.ItemType),
                r.Item.IsPart,
                r.Item.IsGarment,
                r.Item.IsSingleInstance))
            .OrderByDescending(g => g.Count());
        foreach (var combo in combos.Take(6))
            Console.WriteLine($"   {combo.Count(),6:n0}  {combo.Key.GameClass,-15} {combo.Key.Cat,-26} {combo.Key.Type,-28} part={combo.Key.IsPart,-5} garment={combo.Key.IsGarment,-5} single={combo.Key.IsSingleInstance}");
    }
    return 0;
}

/// <summary>Lists the records whose slot parts differ between the bundled file and the derived value.</summary>
static int Slots(Cli opts)
{
    var src = TweakDbSource.Load(opts.Game);
    var names = NameSource.Load(opts);
    var classes = ItemClasses.Load(opts.ResourcePath("ItemClasses.json"));

    var diffs = new List<(ItemRecord Item, ItemClassRecord Stored)>();
    foreach (var item in src.ItemRecords())
    {
        var stored = classes.Get(item.Hash);
        if (stored is null) continue;
        var storedKey = string.Join("|", stored.SlotParts.Select(p => $"{p.ItemPartPreset}:{p.Slot}"));
        if (storedKey != item.SlotPartsKey) diffs.Add((item, stored));
    }

    Console.WriteLine($"slot-part differences: {diffs.Count:n0}");
    foreach (var group in diffs.GroupBy(d => (d.Stored.Type, d.Item.GameClass)).OrderByDescending(g => g.Count()))
        Console.WriteLine($"   {group.Count(),6:n0}  stored={group.Key.Type,-12} game={group.Key.GameClass}");

    Console.WriteLine("\nexamples:");
    foreach (var (item, stored) in diffs.Take(12))
    {
        Console.WriteLine($"   {names.Describe(item.Hash)}  [{item.GameClass}]");
        Console.WriteLine($"      stored:  {stored.SlotParts.Count} parts  {string.Join(", ", stored.SlotParts.Select(p => $"{names.Describe(p.ItemPartPreset)}→{names.Describe(p.Slot)}"))}");
        Console.WriteLine($"      derived: {item.SlotParts.Count} parts  {string.Join(", ", item.SlotParts.Select(p => $"{names.Describe(p.ItemPartPreset)}→{names.Describe(p.Slot)}"))}");
    }
    return 0;
}

static int Dump(Cli opts)
{
    var src = TweakDbSource.Load(opts.Game);
    var names = NameSource.Load(opts);
    if (opts.Args.Count == 0) { Console.Error.WriteLine("dump needs an item id (Items.Foo) or 0xhash"); return 2; }

    var arg = opts.Args[0];
    var hash = arg.StartsWith("0x") ? Convert.ToUInt64(arg[2..], 16) : TweakDbNames.TweakHash(arg);
    var type = src.RecordType(hash);
    Console.WriteLine($"{arg} -> 0x{hash:X16}");
    Console.WriteLine($"record type: {type?.Name ?? "(not a record this WolvenKit build knows)"}");
    foreach (var (key, value) in src.Dump(hash, h => names.Resolve(h) ?? names.Describe(h)))
        Console.WriteLine($"  {key,-28} {value}");
    return 0;
}
