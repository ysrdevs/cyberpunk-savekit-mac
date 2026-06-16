namespace CP2077SaveKit.Core;

/// <summary>
/// Maps the game's internal enum names to the labels players actually see in-game.
/// (The save stores e.g. "Strength" but the UI shows "Body".)
/// </summary>
public static class GameLabels
{
    // The 5 editable core attributes (internal gamedataStatType name -> in-game label).
    private static readonly Dictionary<string, string> AttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"]         = "Body",
        ["Reflexes"]         = "Reflexes",
        ["TechnicalAbility"] = "Technical Ability",
        ["Intelligence"]     = "Intelligence",
        ["Cool"]             = "Cool",
    };

    // gamedataDevelopmentPointType -> in-game label.
    private static readonly Dictionary<string, string> DevPointNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Attribute"] = "Attribute Points",
        ["Primary"]   = "Perk Points",
        ["Espionage"] = "Relic Points",            // Phantom Liberty Relic tree
        ["Secondary"] = "Perk Points (Secondary)",
    };

    public static bool IsCoreAttribute(string internalName) => AttributeNames.ContainsKey(internalName);

    public static string AttributeLabel(string internalName) =>
        AttributeNames.TryGetValue(internalName, out var n) ? n : internalName;

    /// <summary>Player-facing dev-point label. Returns null for pools that shouldn't be shown.</summary>
    public static string? DevPointLabel(string internalType) =>
        DevPointNames.TryGetValue(internalType, out var n) ? n : null;
}
