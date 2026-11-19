using DdrLauncher.Core.Data;
using DdrLauncher.Core.Safety;

namespace DdrLauncher.Core.Tests;

public sealed class LineupTests : IDisposable
{
    private readonly string _mods = Path.Combine(Path.GetTempPath(), "ddr-lineup-" + Guid.NewGuid().ToString("N"));

    private string Target => Path.Combine(_mods, Lineup.PakName);

    public void Dispose()
    {
        if (Directory.Exists(_mods))
        {
            Directory.Delete(_mods, recursive: true);
        }
    }

    [Fact]
    public void PlacesReplacesAndRemovesItsOwnPak()
    {
        var first = Lineup.Install([1, 2, 3], _mods, null, () => false);
        Assert.Equal(FileHasher.Sha256(Target), first);

        var second = Lineup.Install([4, 5], _mods, first, () => false);
        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(Target));

        Assert.Null(Lineup.Install(null, _mods, second, () => false));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public void NeverTouchesAPakItDidNotPlace()
    {
        Directory.CreateDirectory(_mods);
        File.WriteAllBytes(Target, [9, 9]);

        Assert.Throws<InvalidOperationException>(() => Lineup.Install([1], _mods, null, () => false));
        Assert.Throws<InvalidOperationException>(() => Lineup.Install(null, _mods, "not-its-hash", () => false));
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(Target));
    }

    [Fact]
    public void DoesNothingWhileTheGameRuns()
    {
        Assert.Throws<InvalidOperationException>(() => Lineup.Install([1], _mods, null, () => true));
        Assert.False(File.Exists(Target));
    }
}

public class LineupPlanTests
{
    [Fact]
    public void PlansWithTheSameItemsAreEqual()
    {
        var a = new DdrLauncher.Core.Data.LineupPlan([new("T", "1", "V")], [], [], []);
        var b = new DdrLauncher.Core.Data.LineupPlan([new("T", "1", "V")], [], [], []);

        Assert.Equal(a, b);
        Assert.NotEqual(a, DdrLauncher.Core.Data.LineupPlan.Empty);
        Assert.Equal(DdrLauncher.Core.Data.LineupPlan.Empty, new DdrLauncher.Core.Data.LineupPlan([], [], [], []));
    }

    [Fact]
    public void AtomicWriteReplacesTheFileAndLeavesNoTemporaryFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            DdrLauncher.Core.Safety.AtomicFile.WriteText(path, "one");
            DdrLauncher.Core.Safety.AtomicFile.WriteText(path, "two");

            Assert.Equal("two", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
