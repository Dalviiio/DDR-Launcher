using System.Buffers.Binary;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using DdrLauncher.Core.Paks;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace DdrLauncher.Core.Data;

/// <summary>The three files of a cooked texture.</summary>
public sealed record TextureFiles(byte[] Uasset, byte[] Uexp, byte[] Ubulk);

/// <summary>
/// Makes a new texture from one of the game's: same format, size and settings, under its own name, with new pixels.
/// The game's texture is left as it is, so every other team that wears it keeps it.
/// </summary>
public static class LiveryTexture
{
    /// <param name="source">The game's texture.</param>
    /// <param name="sourcePackage">Its package path, such as "/Dakar19ArtVehicles/Cars/X/Textures/Body/201/t_X_Body_d".</param>
    /// <param name="package">The new package path; its last part is the new object name.</param>
    /// <param name="image">The new pixels, the size of the texture's biggest mip.</param>
    public static TextureFiles Copy(TextureFiles source, string sourcePackage, string package, DecodedImage image)
    {
        var layout = TextureDecoder.Layout(source.Uexp) ?? throw new InvalidDataException("This texture's format is not supported.");
        if (layout.Compression is not { } compression)
        {
            throw new InvalidDataException("Only compressed textures can be painted.");
        }

        var top = layout.Mips[0];
        if (image.Width != top.Width || image.Height != top.Height)
        {
            throw new InvalidDataException($"The picture must be {top.Width} x {top.Height}.");
        }

        var uexp = (byte[])source.Uexp.Clone();
        var ubulk = (byte[])source.Ubulk.Clone();
        var encoder = new BcEncoder(compression);
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.MaxMipMapLevel = layout.Mips.Count;
        encoder.OutputOptions.Quality = CompressionQuality.Fast;
        var mips = encoder.EncodeToRawBytes(image.Bgra, image.Width, image.Height, PixelFormat.Bgra32);
        for (var i = 0; i < layout.Mips.Count; i++)
        {
            var mip = layout.Mips[i];

            // The game keeps a whole block for mips under 4 x 4; the encoder's last mips are those same blocks.
            var data = i < mips.Length ? mips[i] : mips[^1];
            if (data.Length != mip.Size)
            {
                throw new InvalidDataException($"Mip {i} does not have the size the game expects.");
            }

            var (target, at) = mip.IsInline ? (uexp, (long)mip.DataAt) : (ubulk, mip.Offset);
            if (at < 0 || at + data.Length > target.Length)
            {
                throw new InvalidDataException($"Mip {i} points outside its file.");
            }

            data.CopyTo(target, at);
        }

        var uasset = Rename(source.Uasset, source.Uexp, sourcePackage, package);

        // Inline mips are found by their position in the whole package (.uasset then .uexp), which moved with the new name.
        foreach (var mip in layout.Mips.Where(m => m.IsInline))
        {
            BinaryPrimitives.WriteInt64LittleEndian(uexp.AsSpan(mip.HeaderAt + 12), uasset.Length + mip.DataAt);
        }

        return new TextureFiles(uasset, uexp, ubulk);
    }

    /// <summary>The object name of a package path: its last part.</summary>
    public static string ObjectName(string package) => package[(package.LastIndexOf('/') + 1)..];

    // Renames the package and its one object in the name map; the rest of the header follows.
    private static byte[] Rename(byte[] uasset, byte[] uexp, string sourcePackage, string package)
    {
        var folder = Directory.CreateTempSubdirectory("ddr-texture-");
        try
        {
            var path = Path.Combine(folder.FullName, "texture.uasset");
            File.WriteAllBytes(path, uasset);
            File.WriteAllBytes(Path.ChangeExtension(path, ".uexp"), uexp);
            var asset = new UAsset(path, EngineVersion.VER_UE4_27);
            var names = asset.GetNameMapIndexList();
            var renamed = 0;
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i].Value;
                if (name == sourcePackage || name == ObjectName(sourcePackage))
                {
                    asset.SetNameReference(i, new FString(name == sourcePackage ? package : ObjectName(package)));
                    renamed++;
                }
            }

            if (renamed != 2)
            {
                throw new InvalidDataException($"{sourcePackage} is not the texture's own name.");
            }

            var output = Path.Combine(folder.FullName, "copy.uasset");
            asset.Write(output);
            return File.ReadAllBytes(output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
