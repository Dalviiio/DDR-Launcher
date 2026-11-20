using DdrLauncher.Core.Game;
using DdrLauncher.Core.Paks;
using DdrLauncher.Core.Safety;

namespace DdrLauncher.Core.Data;

public enum CheckLevel
{
    Ok,
    Warning,
    Problem,
}

/// <summary>One line of the file check: what was looked at, how it went, and why.</summary>
/// <param name="Text">The explanation in English, with <paramref name="Values"/> in "{0}" places, so the launcher can translate it.</param>
public sealed record CheckResult(CheckLevel Level, string Title, string Text, params object[] Values)
{
    public string Detail => string.Format(System.Globalization.CultureInfo.InvariantCulture, Text, Values);
}

/// <summary>
/// Looks at the files the game loads: its paks, the mods in ~mods (readable, what they replace, which ones clash)
/// and the launcher's own pak. Reads only; nothing is changed.
/// </summary>
public static class FileCheck
{
    private const string DataFolders = "Dakar2Game/Plugins/Game/DakarGame/Content/Data/";
    private const string TextFolder = "Dakar2Game/Content/Database/";

    /// <param name="placedSha256">Hash of the pak the launcher placed last time; null when it placed none.</param>
    public static IReadOnlyList<CheckResult> Run(GameInstall game, string[] oodleFolders, string? placedSha256)
    {
        var results = new List<CheckResult>();
        using var oodle = Oodle.Find(oodleFolders);

        // The game's own paks
        var gamePaks = Directory.GetFiles(game.PaksFolder, "*.pak").Order(StringComparer.OrdinalIgnoreCase).ToList();
        var gameFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var broken = new List<string>();
        foreach (var path in gamePaks)
        {
            if (Open(path, oodle) is { } files)
            {
                gameFiles.UnionWith(files);
            }
            else
            {
                broken.Add(Path.GetFileName(path));
            }
        }

        results.Add(broken.Count == 0
            ? new(CheckLevel.Ok, "Game files", "{0} game paks read fine.", gamePaks.Count)
            : new(CheckLevel.Problem, "Game files", "Cannot be read: {0}. Verify the game files in Steam.", string.Join(", ", broken)));

        // Mods, in the order the game loads them
        var ours = Path.Combine(game.ModsFolder, Lineup.PakName);
        var mods = Directory.Exists(game.ModsFolder) ? Directory.GetFiles(game.ModsFolder, "*.pak").Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
        var replacedBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in mods)
        {
            var name = Path.GetFileName(path);
            if (Open(path, oodle) is not { } files)
            {
                results.Add(new(CheckLevel.Problem, name, "This mod cannot be read: the game may fail to load it. Remove or replace it."));
                continue;
            }

            // Data tables and texts a mod brings replace the game's whole file: the last pak in the order wins.
            var tables = files.Where(f => (f.StartsWith(DataFolders, StringComparison.OrdinalIgnoreCase) && f.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                || f.StartsWith(TextFolder, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var table in tables)
            {
                (replacedBy.TryGetValue(table, out var list) ? list : replacedBy[table] = []).Add(name);
            }

            if (!path.Equals(ours, StringComparison.OrdinalIgnoreCase))
            {
                var replaced = string.Join(", ", tables.Where(gameFiles.Contains).Select(Path.GetFileNameWithoutExtension));
                var added = string.Join(", ", tables.Where(t => !gameFiles.Contains(t)).Select(Path.GetFileNameWithoutExtension));
                results.Add((replaced, added) switch
                {
                    ("", "") => new(CheckLevel.Ok, name, "Reads fine and changes no game table."),
                    (_, "") => new(CheckLevel.Ok, name, "Reads fine. It replaces {0}.", replaced),
                    ("", _) => new(CheckLevel.Ok, name, "Reads fine. It adds {0}.", added),
                    _ => new(CheckLevel.Ok, name, "Reads fine. It replaces {0} and adds {1}.", replaced, added),
                });
            }
        }

        // Mods that replace the same file: only the last one counts. The launcher builds on top of the others, so it is fine last.
        foreach (var (table, by) in replacedBy.Where(r => r.Value.Count > 1))
        {
            var last = by[^1];
            var launcherFirst = by.Contains(Lineup.PakName, StringComparer.OrdinalIgnoreCase) && !last.Equals(Lineup.PakName, StringComparison.OrdinalIgnoreCase);
            if (launcherFirst || !by.Contains(Lineup.PakName, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(new(CheckLevel.Warning, Path.GetFileNameWithoutExtension(table),
                    launcherFirst ? "Replaced by {0}. Only {1} counts in the game, so the launcher's changes to it are lost." : "Replaced by {0}. Only {1} counts in the game.",
                    string.Join(", ", by), last));
            }
        }

        // The launcher's own pak
        var present = File.Exists(ours);
        results.Add((present, placedSha256) switch
        {
            (false, null) => new(CheckLevel.Ok, "Launcher changes", "No launcher pak in ~mods. The game runs with your other mods only."),
            (false, not null) => new(CheckLevel.Warning, "Launcher changes", "The launcher's pak is gone from ~mods. Apply puts it back."),
            (true, null) => new(CheckLevel.Warning, "Launcher changes", "{0} is in ~mods but the launcher did not place it, so it will not touch it.", Lineup.PakName),
            _ => FileHasher.HashEquals(FileHasher.Sha256(ours), placedSha256)
                ? new(CheckLevel.Ok, "Launcher changes", "In ~mods and unchanged since the last Apply.")
                : new(CheckLevel.Problem, "Launcher changes", "The launcher's pak was changed after it was applied. Apply again to rebuild it."),
        });

        return results;
    }

    // The files of a pak from the game root, or null when it cannot be read.
    private static List<string>? Open(string path, Oodle? oodle)
    {
        try
        {
            using var pak = new PakReader(path, oodle);
            var root = pak.MountPoint.StartsWith("../../../", StringComparison.Ordinal) ? pak.MountPoint["../../../".Length..] : "";
            return pak.Files.Select(f => root + f).ToList();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }
}
