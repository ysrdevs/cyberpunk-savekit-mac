using System.IO.Compression;
using System.Text;

namespace CP2077SaveKit.Core;

/// <summary>
/// Resolves TweakDBID hashes -> readable names using CyberCAT's `items.bin`
/// (a 4-byte header + gzip stream of length-prefixed ASCII strings, ~1.96M names).
/// Hash scheme: CRC32(name) + (len &lt;&lt; 32). No Oodle needed.
/// </summary>
public sealed class TweakDbNames
{
    private readonly Dictionary<ulong, string> _byHash;

    private TweakDbNames(Dictionary<ulong, string> byHash) => _byHash = byHash;

    public int Count => _byHash.Count;

    public string? Resolve(ulong hash) => _byHash.TryGetValue(hash, out var n) ? n : null;

    /// <summary>Names matching a substring (case-insensitive), capped. For the "add item" picker.</summary>
    public IEnumerable<string> Search(string term, int limit = 200) =>
        _byHash.Values.Where(v => v.Contains(term, StringComparison.OrdinalIgnoreCase)).Take(limit);

    private static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "items.bin");

    private static TweakDbNames? _shared;
    public static TweakDbNames Shared => _shared ??= Load(DefaultPath);

    public static TweakDbNames Load(string path)
    {
        var data = File.ReadAllBytes(path);
        // 4-byte header, then a gzip stream.
        using var gz = new GZipStream(new MemoryStream(data, 4, data.Length - 4), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        var raw = ms.GetBuffer();
        var n = (int)ms.Length;

        var map = new Dictionary<ulong, string>(1 << 21);
        // Skip the small leading header; first record is the length byte just before "Items."
        int i = IndexOf(raw, n, "Items."u8) - 1;
        if (i < 0) i = 0;
        while (i < n)
        {
            int len = raw[i++];
            if (len == 0 || i + len > n) break;
            var name = Encoding.ASCII.GetString(raw, i, len);
            i += len;
            map.TryAdd(TweakHash(name), name);
        }
        return new TweakDbNames(map);
    }

    public static ulong TweakHash(string name) =>
        Crc32(Encoding.ASCII.GetBytes(name)) + ((ulong)name.Length << 32);

    private static int IndexOf(byte[] hay, int len, ReadOnlySpan<byte> needle)
    {
        for (int i = 0; i <= len - needle.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }

    // Standard CRC-32 (IEEE 802.3), matching zlib.crc32 / WolvenKit's Crc32Algorithm.
    private static readonly uint[] _crcTable = BuildTable();
    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in bytes) c = _crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
