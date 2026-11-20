using DdrLauncher.Core.Paks;

namespace DdrLauncher.Core.Tests;

public class PakReaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void ReadsAnUncompressedFile()
    {
        using var pak = new PakReader(Fixture("plain.pak"));

        Assert.Equal("../../../", pak.MountPoint);
        Assert.Equal(["Game/small.txt"], pak.Files);
        Assert.Equal("hello pak"u8.ToArray(), pak.Read("Game/small.txt"));
    }

    [Fact]
    public void ReadsZlibFilesSpanningSeveralBlocks()
    {
        using var pak = new PakReader(Fixture("zlib.pak"));
        var expected = Enumerable.Range(0, 150000).Select(i => (byte)((i * 31 + i / 7) % 256)).ToArray();

        Assert.Equal(expected, pak.Read("Game/Data/big.bin"));
        Assert.Equal("hello pak"u8.ToArray(), pak.Read("game/data/SMALL.txt"));
    }

    [Fact]
    public void RejectsMissingFilesAndNonPaks()
    {
        using (var pak = new PakReader(Fixture("plain.pak")))
        {
            Assert.Throws<FileNotFoundException>(() => pak.Read("Game/nope.txt"));
        }

        var notPak = Path.GetTempFileName();
        File.WriteAllBytes(notPak, new byte[500]);
        try
        {
            Assert.Throws<InvalidDataException>(() => new PakReader(notPak));
        }
        finally
        {
            File.Delete(notPak);
        }
    }
}
