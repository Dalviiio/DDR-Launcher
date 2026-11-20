using DdrLauncher.Core.Data;
using DdrLauncher.Core.Game;
using DdrLauncher.Core.Paks;

namespace DdrLauncher.Core.Tests;

public sealed class FileCheckTests : IDisposable
{
    private const string Table = "Dakar2Game/Plugins/Game/DakarGame/Content/Data/DataTables/DT_VehicleData.uasset";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ddr-check-" + Guid.NewGuid().ToString("N"));
    private readonly GameInstall _game;

    public FileCheckTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Dakar2Game", "Content", "Paks", "~mods"));
        _game = new GameInstall(_root);
        Pak(Path.Combine(_game.PaksFolder, "pakchunk0-WindowsNoEditor.pak"), Table);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ReportsReplacedTablesClashesAndBrokenMods()
    {
        Pak(Path.Combine(_game.ModsFolder, "a_mod_P.pak"), Table);
        Pak(Path.Combine(_game.ModsFolder, "b_mod_P.pak"), Table);
        File.WriteAllBytes(Path.Combine(_game.ModsFolder, "broken_P.pak"), new byte[300]);

        var results = FileCheck.Run(_game, [], placedSha256: "something");

        Assert.Contains(results, r => r is { Level: CheckLevel.Ok, Title: "Game files" });
        Assert.Contains(results, r => r.Title == "a_mod_P.pak" && r.Detail.Contains("DT_VehicleData", StringComparison.Ordinal));
        Assert.Contains(results, r => r is { Level: CheckLevel.Warning, Title: "DT_VehicleData" } && r.Detail.Contains("Only b_mod_P.pak counts", StringComparison.Ordinal));
        Assert.Contains(results, r => r is { Level: CheckLevel.Problem, Title: "broken_P.pak" });
        Assert.Contains(results, r => r is { Level: CheckLevel.Warning, Title: "Launcher changes" });
    }

    [Fact]
    public void TheLaunchersPakLastIsNoClash()
    {
        Pak(Path.Combine(_game.ModsFolder, "a_mod_P.pak"), Table);
        var hash = Lineup.Install(PakBytes(Table), _game.ModsFolder, null, () => false);

        var results = FileCheck.Run(_game, [], hash);

        Assert.DoesNotContain(results, r => r.Level != CheckLevel.Ok);
    }

    private static byte[] PakBytes(string file)
    {
        using var stream = new MemoryStream();
        PakWriter.Write(stream, new Dictionary<string, byte[]> { [file] = [1, 2, 3] });
        return stream.ToArray();
    }

    private static void Pak(string path, string file) => File.WriteAllBytes(path, PakBytes(file));
}
