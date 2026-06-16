using CP2077SaveKit.Core;

// Tiny test harness for the Core layer. Usage:
//   cli info     <sav.dat>            -> header + node summary
//   cli dump     <sav.dat> [out.json] -> full node tree to JSON
//   cli roundtrip <sav.dat> <out.dat> -> load + write back unchanged (integrity test)

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: cli <info|dump|roundtrip> <sav.dat> [out]");
    return 1;
}

var cmd = args[0];
var path = args[1];

try
{
    switch (cmd)
    {
        case "info":
        {
            var save = SaveFile.Load(path);
            Console.WriteLine($"Loaded OK. GameVersion={save.GameVersion}, top-level nodes={save.Nodes.Count}");
            Console.WriteLine("Node summary (by total bytes):");
            Console.WriteLine(NodeDump.Summary(save.Nodes));
            break;
        }
        case "dump":
        {
            var save = SaveFile.Load(path);
            var json = NodeDump.ToJsonString(save.Nodes);
            if (args.Length >= 3) { File.WriteAllText(args[2], json); Console.WriteLine($"Wrote {args[2]}"); }
            else Console.WriteLine(json);
            break;
        }
        case "inv":
        {
            var save = SaveFile.Load(path);
            var subs = InventoryReader.Read(save);
            var total = subs.Sum(s => s.Items.Count);
            var resolved = subs.SelectMany(s => s.Items).Count(i => i.Name is not null);
            Console.WriteLine($"Sub-inventories: {subs.Count}, items: {total}, names resolved: {resolved}/{total}");
            foreach (var sub in subs)
            {
                Console.WriteLine($"\n== SubInventory 0x{sub.InventoryId:X16} ({sub.Items.Count} items) ==");
                foreach (var it in sub.Items.Take(40))
                    Console.WriteLine($"  x{it.Quantity,-6} {it.Display}");
                if (sub.Items.Count > 40) Console.WriteLine($"  … +{sub.Items.Count - 40} more");
            }
            break;
        }
        case "setmoney":
        {
            if (args.Length < 4) { Console.Error.WriteLine("setmoney <save> <out.dat> <amount>"); return 1; }
            var save = SaveFile.Load(path);
            var before = InventoryEditor.GetQuantity(save, InventoryEditor.MoneyHash);
            var amount = uint.Parse(args[3]);
            if (!InventoryEditor.SetMoney(save, amount)) { Console.Error.WriteLine("no money item found"); return 2; }
            save.Save(args[2]);
            Console.WriteLine($"Eddies {before:n0} -> {amount:n0}. Wrote {args[2]}.");
            break;
        }
        case "roundtrip":
        {
            if (args.Length < 3) { Console.Error.WriteLine("roundtrip needs <out.dat>"); return 1; }
            var save = SaveFile.Load(path);
            save.Save(args[2]);
            var a = new FileInfo(path).Length;
            var b = new FileInfo(args[2]).Length;
            Console.WriteLine($"Round-trip done. original={a:n0} B, rewritten={b:n0} B, delta={b - a:n0} B");
            Console.WriteLine("(Load the rewritten save in-game to confirm it's accepted.)");
            break;
        }
        default:
            Console.Error.WriteLine($"unknown command: {cmd}");
            return 1;
    }
}
catch (SaveLoadException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 2;
}

return 0;
