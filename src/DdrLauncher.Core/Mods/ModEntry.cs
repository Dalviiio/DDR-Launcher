namespace DdrLauncher.Core.Mods;

/// <summary>One file of a mod, as it was imported into the library.</summary>
public sealed record ModFile(string Name, long Size, string Sha256);

/// <summary>A mod in the library. Its files live in the library folder; they are copied into the game only while it is enabled.</summary>
public sealed class ModEntry
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    public DateTime AddedUtc { get; init; }

    public bool Enabled { get; set; }

    /// <summary>A UE4SS Lua mod: its files go into the game's UE4SS mods folder instead of ~mods.</summary>
    public bool Ue4ss { get; init; }

    public required List<ModFile> Files { get; init; }

    public long TotalSize => Files.Sum(f => f.Size);
}
