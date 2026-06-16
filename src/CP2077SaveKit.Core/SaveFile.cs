using WolvenKit.RED4.Save;
using WolvenKit.RED4.Save.IO;

namespace CP2077SaveKit.Core;

/// <summary>
/// High-level wrapper over WolvenKit's Cyberpunk save reader/writer.
/// Loads a sav.dat into a node tree, exposes it, and writes it back with
/// correct sizes + hashes (WolvenKit's CyberpunkSaveWriter handles integrity).
/// </summary>
public sealed class SaveFile
{
    public CyberpunkSaveFile Inner { get; }
    public string? SourcePath { get; private set; }

    private SaveFile(CyberpunkSaveFile inner, string? sourcePath)
    {
        Inner = inner;
        SourcePath = sourcePath;
    }

    /// <summary>Load a sav.dat from disk. Throws SaveLoadException on failure.</summary>
    public static SaveFile Load(string path)
    {
        if (!File.Exists(path))
            throw new SaveLoadException($"Save file not found: {path}");

        using var stream = File.OpenRead(path);
        using var reader = new CyberpunkSaveReader(stream);
        var code = reader.ReadFile(out var file);
        if (code != EFileReadErrorCodes.NoError || file is null)
            throw new SaveLoadException($"Failed to parse save ({code}): {path}");

        return new SaveFile(file, path);
    }

    /// <summary>
    /// Write the (possibly edited) save back out. Always writes to a new path or a
    /// backup-protected path — never silently overwrite the original without a backup.
    /// </summary>
    public void Save(string path, bool compress = true)
    {
        using var stream = File.Create(path);
        using var writer = new CyberpunkSaveWriter(stream);
        writer.WriteFile(Inner, compress);
        SourcePath = path;
    }

    /// <summary>Top-level nodes (the save's node tree).</summary>
    public IReadOnlyList<WolvenKit.RED4.Save.NodeEntry> Nodes => Inner.Nodes;

    public ulong GameVersion => Inner.FileHeader.GameVersion;
}

public sealed class SaveLoadException(string message) : Exception(message);
