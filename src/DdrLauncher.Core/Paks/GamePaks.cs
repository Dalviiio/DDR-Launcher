using DdrLauncher.Core.Game;

namespace DdrLauncher.Core.Paks;

/// <summary>
/// The paks the game loads, in its order: its own paks, then the mod paks in ~mods by name (a later pak wins).
/// The launcher's own pak is left out: what the launcher builds goes on top of everything else.
/// </summary>
internal static class GamePaks
{
    /// <summary>
    /// Hands over each file in <paramref name="wanted"/> (paths from the game root, such as "Dakar2Game/Content/...")
    /// as the game would see it: from the last pak that holds it. Returns the ones no pak holds.
    /// Mod paks are untrusted input: the reader checks every offset, and Oodle runs in its fuzz-safe mode.
    /// </summary>
    public static IReadOnlyCollection<string> Read(GameInstall game, IEnumerable<string> wanted, string[] oodleFolders,
        Action<string, byte[]> found, CancellationToken cancel = default)
    {
        var missing = wanted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = missing.ToList();
        using var oodle = Oodle.Find(oodleFolders);
        foreach (var pakPath in Ordered(game))
        {
            using var pak = new PakReader(pakPath, oodle);

            // Paks mount at "../../../" (the game root) or below it, such as "../../../Dakar2Game/".
            if (!pak.MountPoint.StartsWith("../../../", StringComparison.Ordinal))
            {
                continue;
            }

            var root = pak.MountPoint["../../../".Length..];
            foreach (var path in all.Where(p => p.StartsWith(root, StringComparison.OrdinalIgnoreCase) && pak.Contains(p[root.Length..])))
            {
                cancel.ThrowIfCancellationRequested();
                found(path, pak.Read(path[root.Length..]));
                missing.Remove(path);
            }
        }

        return missing;
    }

    /// <summary>Every file path (from the game root) in the paks the game loads, mods included.</summary>
    public static HashSet<string> List(GameInstall game, string[] oodleFolders)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var oodle = Oodle.Find(oodleFolders);
        foreach (var pakPath in Ordered(game))
        {
            using var pak = new PakReader(pakPath, oodle);
            if (pak.MountPoint.StartsWith("../../../", StringComparison.Ordinal))
            {
                files.UnionWith(pak.Files.Select(f => pak.MountPoint["../../../".Length..] + f));
            }
        }

        return files;
    }

    /// <summary>A stamp of every pak the game loads: it changes when the game is updated or another mod is added, changed or removed.</summary>
    public static string Stamp(GameInstall game) =>
        string.Join("\n", Ordered(game).Select(p => $"{Path.GetFileName(p)}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p).Ticks}"));

    private static IEnumerable<string> Ordered(GameInstall game)
    {
        var mods = Directory.Exists(game.ModsFolder)
            ? Directory.GetFiles(game.ModsFolder, "*.pak").Where(p => !string.Equals(Path.GetFileName(p), Data.Lineup.PakName, StringComparison.OrdinalIgnoreCase))
            : [];
        return Directory.GetFiles(game.PaksFolder, "*.pak").Order(StringComparer.OrdinalIgnoreCase)
            .Concat(mods.Order(StringComparer.OrdinalIgnoreCase));
    }
}
