using DdrLauncher.Core.Game;
using DdrLauncher.Core.Paks;
using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace DdrLauncher.Core.Data;

/// <summary>One part of a team's livery: a material slot on the car and the colour texture the team puts there.</summary>
/// <param name="Slot">The material slot on the car's model, such as "M_Body".</param>
/// <param name="Texture">The colour texture as the team row names it, such as "/Dakar19ArtVehicles/Cars/X/T_Body_d.T_Body_d".</param>
public sealed record LiveryPart(string Slot, string Texture);

/// <summary>
/// A painted livery part in a profile: the team's <paramref name="Texture"/> is replaced, for that team only,
/// by a copy of it with the pixels of <paramref name="Image"/>.
/// </summary>
/// <param name="File">Where the game texture is, from the game root and without extension.</param>
/// <param name="Image">The painted picture's file name in the launcher's livery folder.</param>
public sealed record LiveryPaint(string Table, string Row, string Texture, string File, string Image);

/// <summary>Reads team liveries and copies their textures out of the game's paks. Nothing is written to the game.</summary>
public static class Liveries
{
    /// <summary>The folder, inside the game's content, where the launcher puts the textures it makes.</summary>
    public const string Folder = "DalviiioDDR";

    private const string Index = "index.txt";

    /// <summary>The parts of a team row's livery, in the row's order.</summary>
    internal static List<LiveryPart> Parts(StructPropertyData row) =>
        row.Get("VehicleDetails").Items().SelectMany(d => d.Get("Materials").Items())
            .Select(m => new LiveryPart(m.Get("MatSlotName").Text(), m.Get("DiffuseTexture").Text()))
            .Where(p => p.Texture != "")
            .ToList();

    /// <summary>"/A/B/T.T" -> "/A/B/T".</summary>
    public static string Package(string softPath) => softPath.Split('.')[0];

    /// <summary>
    /// Copies the textures (soft paths) out of the paks the game loads into <paramref name="folder"/>, if they are not there yet,
    /// and gives where each is in the game (from the game root, no extension). Starts over when a pak changed.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Fetch(GameInstall game, string folder, IReadOnlyCollection<string> textures, string[] oodleFolders)
    {
        var stamp = Path.Combine(folder, "paks.stamp");
        var current = GamePaks.Stamp(game);
        if (!File.Exists(stamp) || File.ReadAllText(stamp) != current)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(stamp, current);
        }

        var index = Path.Combine(folder, Index);
        var known = File.Exists(index)
            ? File.ReadAllLines(index).Select(l => l.Split('\t')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1])
            : [];
        var missing = textures.Where(t => !known.ContainsKey(t)).Distinct().ToList();
        if (missing.Count > 0)
        {
            var gameFiles = GamePaks.List(game, oodleFolders);
            var found = missing.Select(t => (Texture: t, File: Resolve(Package(t), gameFiles))).Where(f => f.File is not null).ToList();
            GamePaks.Read(game, found.SelectMany(f => new[] { ".uasset", ".uexp", ".ubulk" }.Select(e => f.File + e)), oodleFolders, (file, bytes) =>
            {
                var target = Path.Combine(folder, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            });
            foreach (var (texture, file) in found)
            {
                known[texture] = file!;
            }

            File.WriteAllLines(index, known.Select(k => k.Key + "\t" + k.Value));
        }

        return known.Where(k => textures.Contains(k.Key)).ToDictionary();
    }

    /// <summary>A fetched texture's files; a texture with all its mips inline has an empty .ubulk.</summary>
    public static TextureFiles Open(string folder, string file)
    {
        var path = Path.Combine(folder, file);
        return new TextureFiles(File.ReadAllBytes(path + ".uasset"), File.ReadAllBytes(path + ".uexp"),
            File.Exists(path + ".ubulk") ? File.ReadAllBytes(path + ".ubulk") : []);
    }

    /// <summary>The new texture a paint makes: its package path (for the team row) and its file (in the pak).</summary>
    public static (string Package, string File) Copy(LiveryPaint paint)
    {
        var source = Package(paint.Texture);
        var root = source[..source.IndexOf('/', 1)];

        // Ends like the game's name (such as "_d"): a name ending in "_<number>" would be split by the engine.
        var name = $"T_DDR_{paint.Table.Split('/')[^1]}_{paint.Row}_{LiveryTexture.ObjectName(source)}";
        var fileRoot = paint.File[..^(source.Length - root.Length)];
        return ($"{root}/{Folder}/{name}", $"{fileRoot}/{Folder}/{name}");
    }

    /// <summary>Points the row's materials that use the painted texture at the copy, for that team only.</summary>
    internal static void Paint(UAsset table, StructPropertyData row, LiveryPaint paint)
    {
        var (package, _) = Copy(paint);
        var path = package + "." + LiveryTexture.ObjectName(package);
        var count = 0;
        foreach (var material in row.Get("VehicleDetails").Items().SelectMany(d => d.Get("Materials").Items()))
        {
            if (material.Get("DiffuseTexture") is SoftObjectPropertyData soft && soft.Text() == paint.Texture)
            {
                var value = soft.Value;
                value.AssetPath.AssetName = FName.FromString(table, path);
                soft.Value = value;
                count++;
            }
        }

        if (count == 0)
        {
            throw new InvalidDataException($"Team {paint.Row} does not use {LiveryTexture.ObjectName(Package(paint.Texture))}.");
        }

        if (table.SoftPackageReferenceList is { } references && !references.Any(r => r.Value == package))
        {
            references.Add(new FString(package));
        }
    }

    // "/Game/X" is in Dakar2Game/Content; "/Plugin/X" is in a plugin's Content folder, wherever the plugin is.
    private static string? Resolve(string package, HashSet<string> gameFiles)
    {
        var root = package[1..package.IndexOf('/', 1)];
        var rest = package[(root.Length + 2)..];
        if (root == "Game")
        {
            return "Dakar2Game/Content/" + rest;
        }

        var tail = $"/{root}/Content/{rest}.uasset";
        return gameFiles.FirstOrDefault(f => f.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) is { } file ? file[..^".uasset".Length] : null;
    }
}
