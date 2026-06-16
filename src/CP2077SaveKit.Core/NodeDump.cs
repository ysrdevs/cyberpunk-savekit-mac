using System.Text.Json;
using System.Text.Json.Nodes;
using WolvenKit.RED4.Save;

namespace CP2077SaveKit.Core;

/// <summary>
/// Read-only inspection: walk the node tree into a JSON structure (id/name/size/children).
/// Step 2 of the build ladder — prove we can read a real save before we touch writes.
/// </summary>
public static class NodeDump
{
    public static JsonArray ToJson(IReadOnlyList<NodeEntry> nodes)
    {
        var arr = new JsonArray();
        foreach (var n in nodes)
            arr.Add(NodeToJson(n));
        return arr;
    }

    private static JsonObject NodeToJson(NodeEntry n)
    {
        var obj = new JsonObject
        {
            ["id"] = n.Id,
            ["name"] = n.Name,
            ["size"] = n.Size,
            ["dataSize"] = n.DataSize,
            ["valueType"] = n.Value?.GetType().Name,
        };
        if (n.Children is { Count: > 0 })
        {
            var kids = new JsonArray();
            foreach (var c in n.Children)
                kids.Add(NodeToJson(c));
            obj["children"] = kids;
        }
        return obj;
    }

    public static string ToJsonString(IReadOnlyList<NodeEntry> nodes) =>
        ToJson(nodes).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Flat summary: node name -> count + total bytes. Good first look at a save.</summary>
    public static string Summary(IReadOnlyList<NodeEntry> nodes)
    {
        var counts = new Dictionary<string, (int count, long bytes)>();
        void Walk(NodeEntry n)
        {
            var key = n.Name ?? "<null>";
            var cur = counts.GetValueOrDefault(key);
            counts[key] = (cur.count + 1, cur.bytes + n.Size);
            if (n.Children is { Count: > 0 })
                foreach (var c in n.Children) Walk(c);
        }
        foreach (var n in nodes) Walk(n);

        var lines = counts.OrderByDescending(kv => kv.Value.bytes)
            .Select(kv => $"  {kv.Value.count,5}x  {kv.Value.bytes,12:n0} B  {kv.Key}");
        return string.Join('\n', lines);
    }
}
