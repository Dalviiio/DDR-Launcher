using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using DdrLauncher.Core.Data;
using DdrLauncher.Core.Paks;

namespace DdrLauncher.Core.Tests;

public sealed class LiveryTests
{
    [Fact]
    public void ReadsTheBigMipFromTheUbulkAndTheSmallOneInline()
    {
        // An 8 x 8 red mip in the .ubulk and a 4 x 4 blue mip inside the .uexp, laid out as the game cooks them.
        var red = Bc1(8, 255, 0, 0);
        var blue = Bc1(4, 0, 0, 255);
        var uexp = new List<byte>(new byte[20]);
        var marker = Encoding.ASCII.GetBytes("PF_DXT1\0");
        uexp.AddRange(Int(marker.Length));
        uexp.AddRange(marker);
        uexp.AddRange(Int(0));
        uexp.AddRange(Int(2));
        Mip(uexp, 0x10501, red.Length, 0, null, 8);
        Mip(uexp, 0x48, blue.Length, 1234, blue, 4);
        uexp.AddRange(new byte[16]);

        var layout = TextureDecoder.Layout([.. uexp]);
        Assert.NotNull(layout);
        Assert.Equal([false, true], layout.Mips.Select(m => m.IsInline));

        var big = TextureDecoder.Decode([.. uexp], 8, red);
        Assert.Equal((8, 8), (big!.Width, big.Height));
        Assert.Equal(new byte[] { 0, 0, 255 }, big.Bgra[..3]);

        var small = TextureDecoder.Decode([.. uexp], 4);
        Assert.Equal(new byte[] { 255, 0, 0 }, small!.Bgra[..3]);

        // Without the .ubulk, the big mip is skipped rather than read out of thin air.
        Assert.Equal(4, TextureDecoder.Decode([.. uexp], 8)!.Width);
    }

    [Fact]
    public void NamesTheCopyAfterTheTeamInTheSamePlugin()
    {
        var paint = new LiveryPaint("Dakar22Teams/DT_SubTeamDataDK22", "201", "/Dakar19ArtVehicles/Cars/X/T_Body_d.T_Body_d",
            "Dakar2Game/Plugins/Art/Dakar19ArtVehicles/Content/Cars/X/T_Body_d", "a.png");

        var (package, file) = Liveries.Copy(paint);

        Assert.Equal("/Dakar19ArtVehicles/DalviiioDDR/T_DDR_DT_SubTeamDataDK22_201_T_Body_d", package);
        Assert.Equal("Dakar2Game/Plugins/Art/Dakar19ArtVehicles/Content/DalviiioDDR/T_DDR_DT_SubTeamDataDK22_201_T_Body_d", file);
    }

    [Fact]
    public void DrawsTheWireframeOfTheChosenPartsOnly()
    {
        var model = new CarModel(new float[9], [0, 0, 1, 0, 0, 1], [new CarSection("M_Body", [0, 1, 2]), new CarSection("M_Glass", [0, 1, 2])]);

        var body = model.Wireframe(new HashSet<string> { "M_Body" }, 8, 8);
        var none = model.Wireframe(new HashSet<string> { "M_Fill" }, 8, 8);

        Assert.Equal(255, body.Bgra[3]);
        Assert.Equal(0, body.Bgra[(7 * 8 + 7) * 4 + 3]);
        Assert.All(none.Bgra, b => Assert.Equal(0, b));
    }

    [Fact]
    public void APlanSavedBeforeLiveriesHasNoPaints()
    {
        var plan = JsonSerializer.Deserialize<LineupPlan>("""{"Changes":[],"Vehicles":[],"Pilots":[],"Teams":[]}""");

        Assert.NotNull(plan);
        Assert.Empty(plan.Paints);
        Assert.Equal(LineupPlan.Empty, plan);
        Assert.NotEqual(LineupPlan.Empty, LineupPlan.Empty with { Paints = [new LiveryPaint("T", "1", "/A/B.B", "F", "i.png")] });
    }

    private static byte[] Bc1(int size, byte r, byte g, byte b)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (r, g, b, 255);
        }

        var encoder = new BcEncoder(CompressionFormat.Bc1);
        encoder.OutputOptions.GenerateMipMaps = false;
        return encoder.EncodeToRawBytes(pixels, size, size, PixelFormat.Rgba32)[0];
    }

    private static void Mip(List<byte> uexp, uint flags, int size, long offset, byte[]? inline, int width)
    {
        uexp.AddRange(Int(1));
        uexp.AddRange(Int((int)flags));
        uexp.AddRange(Int(size));
        uexp.AddRange(Int(size));
        var at = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(at, offset);
        uexp.AddRange(at);
        uexp.AddRange(inline ?? []);
        uexp.AddRange(Int(width));
        uexp.AddRange(Int(width));
        uexp.AddRange(Int(1));
    }

    private static byte[] Int(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
