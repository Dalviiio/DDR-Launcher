using System.Buffers.Binary;
using System.Text;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace DdrLauncher.Core.Paks;

/// <summary>A decoded picture: width, height and 32-bit BGRA pixels, top row first.</summary>
public sealed record DecodedImage(int Width, int Height, byte[] Bgra);

/// <summary>One mip of a cooked texture: where its bulk header sits in the .uexp, and where its pixels are.</summary>
/// <param name="HeaderAt">Position in the .uexp of the bulk data header (flags, count, size, offset).</param>
/// <param name="Offset">For a mip in the .ubulk, where it starts there; for an inline mip, its position in the whole package.</param>
/// <param name="DataAt">For an inline mip, where its pixels start in the .uexp; -1 when they are in the .ubulk.</param>
public sealed record TextureMip(int Width, int Height, int Size, int HeaderAt, long Offset, int DataAt)
{
    public bool IsInline => DataAt >= 0;
}

/// <summary>The pixel format and mips of a cooked UE 4.27 Texture2D, read from its .uexp.</summary>
public sealed record TextureLayout(string Format, CompressionFormat? Compression, IReadOnlyList<TextureMip> Mips);

/// <summary>
/// Reads the pixels of a cooked UE 4.27 Texture2D: mips stored inside the .uexp, and the bigger ones in the .ubulk when it is given.
/// </summary>
public static class TextureDecoder
{
    private const uint ForceInlinePayload = 0x40;
    private const uint PayloadInSeparateFile = 0x100;

    private static readonly Dictionary<string, CompressionFormat?> Formats = new()
    {
        ["PF_BC7"] = CompressionFormat.Bc7,
        ["PF_DXT1"] = CompressionFormat.Bc1,
        ["PF_DXT5"] = CompressionFormat.Bc3,
        ["PF_BC4"] = CompressionFormat.Bc4,
        ["PF_BC5"] = CompressionFormat.Bc5,
        ["PF_B8G8R8A8"] = null,
    };

    /// <summary>The texture's format and mips, or null when its format is not one the launcher reads.</summary>
    public static TextureLayout? Layout(byte[] uexp)
    {
        // The platform data follows the properties: SizeX, SizeY, packed flags, then the pixel format as an FString.
        foreach (var (name, format) in Formats)
        {
            var marker = Encoding.ASCII.GetBytes(name + "\0");
            var at = uexp.AsSpan().IndexOf(marker);
            if (at < 4 + 12 || BinaryPrimitives.ReadInt32LittleEndian(uexp.AsSpan(at - 4)) != marker.Length)
            {
                continue;
            }

            var position = at + marker.Length;
            var mipCount = Int(uexp, position + 4);
            position += 8;
            var mips = new List<TextureMip>();
            for (var i = 0; i < mipCount && position + 36 <= uexp.Length; i++)
            {
                // Each mip: bCooked, then the bulk data header (flags, element count, size on disk, offset), then the inline pixels if any.
                var header = position + 4;
                var flags = (uint)Int(uexp, header);
                var size = Int(uexp, header + 8);
                var offset = BinaryPrimitives.ReadInt64LittleEndian(uexp.AsSpan(header + 12));
                position += 24;
                var inline = (flags & ForceInlinePayload) != 0 || (flags & PayloadInSeparateFile) == 0;
                var dataAt = inline ? position : -1;
                if (inline)
                {
                    position += size;
                }

                if (size < 0 || position + 12 > uexp.Length)
                {
                    return null;
                }

                mips.Add(new TextureMip(Int(uexp, position), Int(uexp, position + 4), size, header, offset, dataAt));
                position += 12;
            }

            return new TextureLayout(name, format, mips);
        }

        return null;
    }

    /// <summary>
    /// Decodes the largest mip that is at most <paramref name="maxWidth"/> wide and that it has the pixels of;
    /// null when there is none. Mips in the .ubulk are read only when <paramref name="ubulk"/> is given.
    /// </summary>
    public static DecodedImage? Decode(byte[] uexp, int maxWidth = 512, byte[]? ubulk = null)
    {
        if (Layout(uexp) is not { } layout)
        {
            return null;
        }

        foreach (var mip in layout.Mips)
        {
            if (mip.Width > maxWidth || mip.Size <= 0 || mip.Width <= 0 || mip.Height <= 0)
            {
                continue;
            }

            var (source, at) = mip.IsInline ? (uexp, (long)mip.DataAt) : (ubulk, mip.Offset);
            if (source is null || at < 0 || at + mip.Size > source.Length)
            {
                continue;
            }

            return new DecodedImage(mip.Width, mip.Height, Pixels(source.AsSpan((int)at, mip.Size).ToArray(), mip.Width, mip.Height, layout.Compression));
        }

        return null;
    }

    private static byte[] Pixels(byte[] data, int width, int height, CompressionFormat? format)
    {
        if (format is null)
        {
            if (data.Length < width * height * 4)
            {
                throw new InvalidDataException("Texture data is too short.");
            }

            return data[..(width * height * 4)];
        }

        var colors = new BcDecoder().DecodeRaw(data, width, height, format.Value);
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < colors.Length; i++)
        {
            bgra[i * 4] = colors[i].b;
            bgra[i * 4 + 1] = colors[i].g;
            bgra[i * 4 + 2] = colors[i].r;
            bgra[i * 4 + 3] = colors[i].a;
        }

        return bgra;
    }

    private static int Int(byte[] data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
}
