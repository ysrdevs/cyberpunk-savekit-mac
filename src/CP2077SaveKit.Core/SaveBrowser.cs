namespace CP2077SaveKit.Core;

/// <summary>A save found in the default Cyberpunk saves folder.</summary>
public sealed record SaveEntry(string Folder, string Path, string Type, DateTime Modified, bool IsManual)
{
    /// <summary>Compact one-line label for the picker, e.g. "Manual   ManualSave-34   Jun 16  02:03".</summary>
    public string Display => $"{Type,-8} {Folder,-18} {Modified:MMM d  HH:mm}";
}

/// <summary>
/// Lists Cyberpunk 2077 saves from the default macOS save location so players never have to hunt
/// for sav.dat. Defaults to Manual saves (the safe ones to edit); auto/quick/others on request.
/// </summary>
public static class SaveBrowser
{
    public static string DefaultSavesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "CD Projekt Red", "Cyberpunk 2077", "saves");

    public static IReadOnlyList<SaveEntry> List(bool manualOnly)
    {
        var dir = DefaultSavesDir;
        var list = new List<SaveEntry>();
        if (!Directory.Exists(dir)) return list;

        foreach (var sub in Directory.GetDirectories(dir))
        {
            var sav = Path.Combine(sub, "sav.dat");
            if (!File.Exists(sav)) continue;
            var folder = Path.GetFileName(sub);
            var manual = folder.StartsWith("ManualSave", StringComparison.OrdinalIgnoreCase);
            if (manualOnly && !manual) continue;
            list.Add(new SaveEntry(folder, sav, Classify(folder), File.GetLastWriteTime(sav), manual));
        }
        list.Sort((a, b) => b.Modified.CompareTo(a.Modified)); // newest first
        return list;
    }

    private static string Classify(string folder)
    {
        bool starts(string p) => folder.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        if (starts("ManualSave")) return "Manual";
        if (starts("AutoSave")) return "Auto";
        if (starts("QuickSave")) return "Quick";
        if (starts("PointOfNoReturn")) return "PONR";
        if (starts("EndGame")) return "EndGame";
        return "Other";
    }
}
