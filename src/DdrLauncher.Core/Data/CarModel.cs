using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using DdrLauncher.Core.Game;
using DdrLauncher.Core.Paks;

namespace DdrLauncher.Core.Data;

/// <summary>One material slot of a car's model and its triangles (three vertex indices each).</summary>
public sealed record CarSection(string Slot, int[] Triangles);

/// <summary>A car's 3D model: positions (x, y, z in the game's units, Z up), texture coordinates (u, v) and its parts by material slot.</summary>
public sealed record CarModel(float[] Positions, float[] Uvs, IReadOnlyList<CarSection> Sections)
{
    /// <summary>
    /// The texture layout of the parts in <paramref name="slots"/>: their triangle edges drawn on a transparent picture,
    /// to lay over a livery while painting it.
    /// </summary>
    public DecodedImage Wireframe(IReadOnlySet<string> slots, int width, int height)
    {
        var bgra = new byte[width * height * 4];
        foreach (var section in Sections.Where(s => slots.Contains(s.Slot)))
        {
            var t = section.Triangles;
            for (var i = 0; i + 2 < t.Length; i += 3)
            {
                Line(bgra, width, height, t[i], t[i + 1]);
                Line(bgra, width, height, t[i + 1], t[i + 2]);
                Line(bgra, width, height, t[i + 2], t[i]);
            }
        }

        return new DecodedImage(width, height, bgra);
    }

    private void Line(byte[] bgra, int width, int height, int a, int b)
    {
        // Textures repeat, so a coordinate outside 0..1 lands where the game would draw it; an edge stays on the side of its first end.
        var shift = (MathF.Floor(Uvs[a * 2]), MathF.Floor(Uvs[a * 2 + 1]));
        var (x0, y0) = ((Uvs[a * 2] - shift.Item1) * width, (Uvs[a * 2 + 1] - shift.Item2) * height);
        var (x1, y1) = ((Uvs[b * 2] - shift.Item1) * width, (Uvs[b * 2 + 1] - shift.Item2) * height);
        var steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(x1 - x0), MathF.Abs(y1 - y0)));
        for (var i = 0; i <= steps; i++)
        {
            var f = steps == 0 ? 0 : (float)i / steps;
            var x = (int)(x0 + (x1 - x0) * f);
            var y = (int)(y0 + (y1 - y0) * f);
            if (x >= 0 && y >= 0 && x < width && y < height)
            {
                bgra.AsSpan((y * width + x) * 4, 4).Fill(255);
            }
        }
    }
}

/// <summary>
/// Reads 3D models out of the paks the game loads (mods included). The paks stay open only while a model is read:
/// an open pak in ~mods could not be replaced or moved. Mod paks are untrusted: what cannot be read gives no model.
/// </summary>
public sealed class GameModels : IDisposable
{
    // Fewer vertices are enough to show the car and keep WPF's 3D fast.
    private const int MaxVertices = 80_000;

    private readonly DefaultFileProvider _provider;

    private GameModels(GameInstall game, string[] oodleFolders)
    {
        var oodle = oodleFolders.Select(f => Path.Combine(f, Oodle.LibraryName)).FirstOrDefault(File.Exists)
            ?? throw new OodleMissingException();
        OodleHelper.Initialize(oodle);
        _provider = new DefaultFileProvider(game.PaksFolder, SearchOption.AllDirectories, new VersionContainer(EGame.GAME_UE4_27), StringComparer.OrdinalIgnoreCase);
        _provider.Initialize();
        _provider.Mount();

        // The paks do not list the game's plugins, so "/DakarGame/..." paths are mapped to their folders by hand.
        foreach (var file in _provider.Files.Keys)
        {
            var content = file.IndexOf("/content/", StringComparison.OrdinalIgnoreCase);
            if (content > 0 && file.StartsWith("dakar2game/plugins/", StringComparison.OrdinalIgnoreCase))
            {
                var root = file[..content];
                _provider.VirtualPaths.TryAdd(root[(root.LastIndexOf('/') + 1)..], root);
            }
        }
    }

    /// <summary>The model of the vehicle whose game class is <paramref name="blueprint"/> ("/DakarGame/.../B_X.B_X_C"); null when it cannot be read.</summary>
    public static CarModel? Car(GameInstall game, string[] oodleFolders, string blueprint)
    {
        using var models = new GameModels(game, oodleFolders);
        return models.Car(blueprint);
    }

    private CarModel? Car(string blueprint)
    {
        try
        {
            return Mesh(blueprint.Split('.')[0]) is { } mesh ? Model(mesh) : null;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    public void Dispose() => _provider.Dispose();

    // The skeletal mesh the blueprint (or a blueprint it comes from) gives its vehicle.
    private USkeletalMesh? Mesh(string package)
    {
        for (var depth = 0; depth < 5 && package.Length > 0; depth++)
        {
            var exports = _provider.LoadPackage(package).GetExports().ToList();
            foreach (var export in exports)
            {
                if (export.TryGetValue(out FPackageIndex mesh, "SkeletalMesh") && !mesh.IsNull && mesh.Load<USkeletalMesh>() is { } found)
                {
                    return found;
                }
            }

            package = exports.OfType<UBlueprintGeneratedClass>().FirstOrDefault()?.SuperStruct?.ResolvedObject?.GetPathName().Split('.')[0] ?? "";
            if (package.StartsWith("/Script/", StringComparison.Ordinal))
            {
                return null;
            }
        }

        return null;
    }

    private static CarModel? Model(USkeletalMesh mesh)
    {
        // Cooked games often keep only the first LODs' vertices; the others are empty.
        var lods = mesh.LODModels?.Where(l => l is { NumVertices: > 0, VertexBufferGPUSkin: not null, Indices: not null }).ToList() ?? [];
        if ((lods.FirstOrDefault(l => l.NumVertices <= MaxVertices) ?? lods.FirstOrDefault()) is not { } lod)
        {
            return null;
        }

        var (vertices, indices) = (lod.VertexBufferGPUSkin!, lod.Indices!);

        var count = (int)lod.NumVertices;
        var positions = new float[count * 3];
        var uvs = new float[count * 2];
        for (var i = 0; i < count; i++)
        {
            if (vertices.VertsFloat is { Length: > 0 } full)
            {
                (positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]) = (full[i].Pos.X, full[i].Pos.Y, full[i].Pos.Z);
                (uvs[i * 2], uvs[i * 2 + 1]) = (full[i].UV[0].U, full[i].UV[0].V);
            }
            else
            {
                var half = vertices.VertsHalf[i];
                (positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]) = (half.Pos.X, half.Pos.Y, half.Pos.Z);
                (uvs[i * 2], uvs[i * 2 + 1]) = ((float)BitConverter.UInt16BitsToHalf(half.UV[0].U), (float)BitConverter.UInt16BitsToHalf(half.UV[0].V));
            }
        }

        var all = indices.Indices32 is { Length: > 0 } wide ? wide.Select(i => (int)i).ToArray() : indices.Indices16.Select(i => (int)i).ToArray();
        var sections = lod.Sections
            .Where(s => !s.bDisabled && s.MaterialIndex < mesh.SkeletalMaterials.Length)
            .Select(s => new CarSection(mesh.SkeletalMaterials[s.MaterialIndex].MaterialSlotName.Text, all.AsSpan((int)s.BaseIndex, (int)s.NumTriangles * 3).ToArray()))
            .ToList();
        return new CarModel(positions, uvs, sections);
    }
}
