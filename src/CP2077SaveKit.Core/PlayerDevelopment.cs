using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Types;

namespace CP2077SaveKit.Core;

public sealed record AttributeView(string Name, int Value);
public sealed record DevPointsView(string Type, int Spent, int Unspent);
public sealed record ProficiencyView(string Type, int Level, int MaxLevel, int Exp);

/// <summary>
/// Reads/edits the player's attributes, development points and skill proficiencies, which live
/// in the PlayerDevelopmentData chunk inside the ScriptableSystemsContainer RED package.
/// </summary>
public static class PlayerDevelopment
{
    public static PlayerDevelopmentData? Find(SaveFile save)
    {
        var node = FindNode(save.Nodes, Constants.NodeNames.SCRIPTABLE_SYSTEMS_CONTAINER);
        if (node?.Value is not Package pkg || pkg.Content is not RedPackage rp) return null;

        // PlayerDevelopmentData is nested in PlayerDevelopmentSystem.playerData[*] (CHandles).
        foreach (var chunk in rp.Chunks)
        {
            if (chunk is PlayerDevelopmentData direct) return direct;   // (older layout)
            if (chunk is PlayerDevelopmentSystem pds)
            {
                foreach (var handle in pds.PlayerData)
                {
                    if (handle?.Chunk is PlayerDevelopmentData pdd && pdd.Attributes.Count > 0)
                        return pdd;
                }
            }
        }
        return null;
    }

    /// <summary>Diagnostic: list the chunk type names in the scriptable systems package.</summary>
    public static IEnumerable<string> ChunkTypeNames(SaveFile save)
    {
        var node = FindNode(save.Nodes, Constants.NodeNames.SCRIPTABLE_SYSTEMS_CONTAINER);
        if (node?.Value is not Package pkg || pkg.Content is not RedPackage rp) yield break;
        foreach (var chunk in rp.Chunks) yield return chunk.GetType().Name;
    }

    // ---- SaveFile-based API (keeps WolvenKit types out of the GUI) ----
    public static bool HasData(SaveFile save) => Find(save) is not null;
    public static IReadOnlyList<AttributeView> ReadAttributes(SaveFile save) =>
        Find(save) is { } p ? ReadAttributes(p) : Array.Empty<AttributeView>();
    public static IReadOnlyList<DevPointsView> ReadDevPoints(SaveFile save) =>
        Find(save) is { } p ? ReadDevPoints(p) : Array.Empty<DevPointsView>();
    public static IReadOnlyList<ProficiencyView> ReadProficiencies(SaveFile save) =>
        Find(save) is { } p ? ReadProficiencies(p) : Array.Empty<ProficiencyView>();
    public static bool SetAttribute(SaveFile save, string name, int value) =>
        Find(save) is { } p && SetAttribute(p, name, value);
    public static bool SetUnspentPoints(SaveFile save, string type, int unspent) =>
        Find(save) is { } p && SetUnspentPoints(p, type, unspent);

    public static IReadOnlyList<AttributeView> ReadAttributes(PlayerDevelopmentData pdd)
    {
        var list = new List<AttributeView>();
        foreach (var a in pdd.Attributes)
            list.Add(new AttributeView(a.AttributeName.ToString(), (int)a.Value));
        return list;
    }

    public static IReadOnlyList<DevPointsView> ReadDevPoints(PlayerDevelopmentData pdd)
    {
        var list = new List<DevPointsView>();
        foreach (var d in pdd.DevPoints)
            list.Add(new DevPointsView(d.Type.ToString(), (int)d.Spent, (int)d.Unspent));
        return list;
    }

    public static IReadOnlyList<ProficiencyView> ReadProficiencies(PlayerDevelopmentData pdd)
    {
        var list = new List<ProficiencyView>();
        foreach (var p in pdd.Proficiencies)
            list.Add(new ProficiencyView(p.Type.ToString(), (int)p.CurrentLevel, (int)p.MaxLevel, (int)p.CurrentExp));
        return list;
    }

    /// <summary>Set a core attribute (by enum name, e.g. "Strength") to a value.</summary>
    public static bool SetAttribute(PlayerDevelopmentData pdd, string name, int value)
    {
        foreach (var a in pdd.Attributes)
            if (string.Equals(a.AttributeName.ToString(), name, StringComparison.OrdinalIgnoreCase))
            { a.Value = value; return true; }
        return false;
    }

    /// <summary>Set unspent points for a development-point type (e.g. "Attribute", "Primary"=perk).</summary>
    public static bool SetUnspentPoints(PlayerDevelopmentData pdd, string type, int unspent)
    {
        foreach (var d in pdd.DevPoints)
            if (string.Equals(d.Type.ToString(), type, StringComparison.OrdinalIgnoreCase))
            { d.Unspent = unspent; return true; }
        return false;
    }

    private static NodeEntry? FindNode(IReadOnlyList<NodeEntry> nodes, string name)
    {
        foreach (var n in nodes)
        {
            if (n.Name == name) return n;
            if (n.Children is { Count: > 0 })
            {
                var f = FindNode(n.Children, name);
                if (f is not null) return f;
            }
        }
        return null;
    }
}
