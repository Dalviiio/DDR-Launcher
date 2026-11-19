using System.Buffers.Binary;
using System.Text;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace DdrLauncher.Core.Paks;

/// <summary>A decoded picture: width, height and 32-bit BGRA pixels, top row first.</summary>
public sealed record DecodedImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// Reads the pixels of a cooked UE 4.27 Texture2D from its .uexp, for the formats the game's UI pictures use.
/// Only mips stored inside the .uexp are read.
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
        ["PF_B8G8R8A8"] = null,
    };

    /// <summary>Decodes the largest mip that is at most <paramref name="maxWidth"/> wide; null when the texture holds none it can read.</summary>
    public static DecodedImage? Decode(byte[] uexp, int maxWidth = 512)
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
            for (var i = 0; i < mipCount && position + 36 <= uexp.Length; i++)
            {
                var flags = (uint)Int(uexp, position + 4);
                var size = Int(uexp, position + 12);
                position += 24;
                var inline = (flags & ForceInlinePayload) != 0 || (flags & PayloadInSeparateFile) == 0;
                var dataAt = position;
                if (inline)
                {
                    position += size;
                }

                if (position + 12 > uexp.Length)
                {
                    return null;
                }

                var width = Int(uexp, position);
                var height = Int(uexp, position + 4);
                position += 12;
                if (!inline || width > maxWidth || size <= 0 || width <= 0 || height <= 0 || dataAt + size > uexp.Length)
                {
                    continue;
                }

                return new DecodedImage(width, height, Pixels(uexp.AsSpan(dataAt, size).ToArray(), width, height, format));
            }

            return null;
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
