using DdrLauncher.Core.Game;
using DdrLauncher.Core.Paks;

namespace DdrLauncher.Core.Data;

/// <summary>Copies the game's tables, as the game loads them (other mods included), into the launcher's cache; again only when a pak changed.</summary>
internal static class TableCache
{
    private const string FoundList = "found.txt";

    /// <param name="files">Paths from the game root that must exist, such as ".../DT_VehicleData.uasset".</param>
    /// <param name="discover">Picks more files from the paks: tables a mod may add, such as a new edition's teams.</param>
    public static void Refresh(GameInstall game, string cacheFolder, IReadOnlyList<string> files, Func<string, bool> discover, string[] oodleFolders)
    {
        var stamp = GamePaks.Stamp(game) + "\n" + string.Join("\n", files);
        var stampPath = Path.Combine(cacheFolder, "paks.stamp");
        if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
        {
            return;
        }

        if (Directory.Exists(cacheFolder))
        {
            Directory.Delete(cacheFolder, recursive: true);
        }

        var found = GamePaks.List(game, oodleFolders).Where(discover).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var missing = GamePaks.Read(game, files.Concat(found), oodleFolders, (file, bytes) =>
        {
            var target = Path.Combine(cacheFolder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        });
        if (missing.Count > 0)
        {
            throw new FileNotFoundException($"Not found in the game: {string.Join(", ", missing.Select(Path.GetFileName))}");
        }

        File.WriteAllLines(Path.Combine(cacheFolder, FoundList), found);
        File.WriteAllText(stampPath, stamp);
    }

    /// <summary>The discovered files of the last refresh.</summary>
    public static IReadOnlyList<string> Found(string cacheFolder) => File.ReadAllLines(Path.Combine(cacheFolder, FoundList));
}
