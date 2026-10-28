using System.Text;
using CP2077SaveKit.Core;

namespace CP2077SaveKit.DataTool;

/// <summary>
/// Pulls TweakDB names out of the game's own binaries. The shipped `tweakdb.bin` stores keys as
/// 32-bit CRC hashes, but the string tables it still carries (flat values of type `String`/`CName`)
/// and the compiled scripts name a few thousand of them — enough to name item ids that were added
/// after the bundled list was built. Candidates are only kept when they hash to a key the loaded
/// TweakDB actually has, which filters out the incidental ASCII in those files.
/// </summary>
public static class StringHarvester
{
    /// <summary>Game files worth scanning for TweakDB names.</summary>
    public static IEnumerable<string> DefaultSources(string gameDir)
    {
        yield return Path.Combine(gameDir, "r6", "cache", "tweakdb.bin");
        yield return Path.Combine(gameDir, "r6", "cache", "tweakdb_ep1.bin");
        yield return Path.Combine(gameDir, "r6", "cache", "final.redscripts");
    }

    public static IEnumerable<string> Harvest(string gameDir, ISet<ulong> knownKeys, Action<string>? progress = null)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in DefaultSources(gameDir))
        {
            if (!File.Exists(file)) continue;
            var before = found.Count;
            foreach (var candidate in Candidates(File.ReadAllBytes(file)))
            {
                if (candidate.Length > 255) continue;
                if (!knownKeys.Contains(TweakDbNames.TweakHash(candidate))) continue;
                found.Add(candidate);
            }
            progress?.Invoke($"{Path.GetFileName(file)}: +{found.Count - before:n0} names");
        }
        return found;
    }

    /// <summary>Printable ASCII runs, split further into TweakDB-name shaped tokens.</summary>
    private static IEnumerable<string> Candidates(byte[] data)
    {
        var sb = new StringBuilder(128);
        for (var i = 0; i <= data.Length; i++)
        {
            var b = i < data.Length ? data[i] : (byte)0;
            if (b >= 0x20 && b <= 0x7E)
            {
                sb.Append((char)b);
                continue;
            }

            if (sb.Length >= 4 && TryName(sb, out var name)) yield return name;
            sb.Clear();
        }
    }

    private static bool TryName(StringBuilder sb, out string name)
    {
        name = "";
        var dot = false;
        for (var i = 0; i < sb.Length; i++)
        {
            var c = sb[i];
            if (c == '.')
            {
                dot = true;
                continue;
            }
            var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '[' or ']' or ':';
            if (!ok) return false;
        }

        if (!dot || sb.Length > 120) return false;
        var text = sb.ToString();
        if (text.StartsWith('.') || text.EndsWith('.') || text.Contains("..")) return false;
        name = text;
        return true;
    }
}
