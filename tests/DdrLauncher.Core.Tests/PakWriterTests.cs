using System.Text;
using DdrLauncher.Core.Paks;

namespace DdrLauncher.Core.Tests;

public class PakWriterTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static byte[] Pack(Dictionary<string, string> files)
    {
        using var stream = new MemoryStream();
        PakWriter.Write(stream, files.ToDictionary(f => f.Key, f => Encoding.UTF8.GetBytes(f.Value)));
        return stream.ToArray();
    }

    [Fact]
    public void WritesTheSameBytesAsRepak()
    {
        Assert.Equal(Fixture("plain.pak"), Pack(new() { ["Game/small.txt"] = "hello pak" }));
        Assert.Equal(Fixture("tree.pak"), Pack(new() { ["root.txt"] = "root", ["Game/b.txt"] = "beta", ["Game/Data/a.txt"] = "alpha" }));
    }

    [Fact]
    public void WrittenPaksReadBack()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Pack(new() { ["Game/Data/a.txt"] = "alpha", ["Game/b.txt"] = "beta" }));
            using var pak = new PakReader(path);

            Assert.Equal("alpha"u8.ToArray(), pak.Read("Game/Data/a.txt"));
            Assert.Equal("beta"u8.ToArray(), pak.Read("Game/b.txt"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
