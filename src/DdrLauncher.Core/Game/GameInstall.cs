using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DdrLauncher.Core.Game;

/// <summary>
/// A Dakar Desert Rally install. The launcher only ever writes into <see cref="ModsFolder"/>.
/// </summary>
public sealed partial class GameInstall
{
    public const string SteamFolderName = "Dakar Desert Rally";
    public const string ProjectFolderName = "Dakar2Game";
    public const string SteamAppId = "1839940";

    public GameInstall(string folder)
    {
        if (!IsValid(folder))
        {
            throw new ArgumentException($"'{folder}' is not a Dakar Desert Rally folder (no {ProjectFolderName} inside).", nameof(folder));
        }

        Folder = Path.GetFullPath(folder);
    }

    public string Folder { get; }

    public string PaksFolder => Path.Combine(Folder, ProjectFolderName, "Content", "Paks");

    /// <summary>Pak mods are loaded from here, after the game's own paks.</summary>
    public string ModsFolder => Path.Combine(PaksFolder, "~mods");

    public static bool IsValid(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && Directory.Exists(Path.Combine(folder, ProjectFolderName, "Content", "Paks"));

    /// <summary>True while any process of the game is open (its executables all start with "Dakar2Game").</summary>
    public static bool IsGameRunning() =>
        Process.GetProcesses().Any(p =>
        {
            using (p)
            {
                return p.ProcessName.StartsWith(ProjectFolderName, StringComparison.OrdinalIgnoreCase);
            }
        });

    /// <summary>Looks for the game in every Steam library. Returns null when it is not found.</summary>
    public static GameInstall? FindOnSteam()
    {
        var steam = SteamPath();
        if (steam is null)
        {
            return null;
        }

        // The vdf keeps the real letter case; the registry value is lower case, so it only comes last.
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var libraries = File.Exists(vdf) ? LibraryPaths(File.ReadAllText(vdf)).ToList() : [];
        libraries.Add(steam);

        return libraries
            .Select(l => Path.Combine(l, "steamapps", "common", SteamFolderName))
            .Where(IsValid)
            .Select(f => new GameInstall(f))
            .FirstOrDefault();
    }

    /// <summary>Steam's own folder, from the registry; null when Steam is not installed.</summary>
    public static string? SteamPath() => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;

    /// <summary>Reads the library folders listed in Steam's libraryfolders.vdf.</summary>
    internal static IEnumerable<string> LibraryPaths(string vdf) =>
        PathEntry().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntry();
}
