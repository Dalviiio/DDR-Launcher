using System.IO;
using System.Text.Json;
using DdrLauncher.Core.Safety;

namespace DdrLauncher.App;

/// <summary>The launcher's own data folder and the few settings it remembers.</summary>
public sealed class Settings
{
    public static readonly string DataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DalviiioDDRLauncher");

    private static readonly string FilePath = Path.Combine(DataFolder, "settings.json");

    public string? GameFolder { get; set; }

    /// <summary>The launcher's language code; null follows Windows.</summary>
    public string? Language { get; set; }

    /// <summary>Look for a new version at each start; null until the user answered the first time.</summary>
    public bool? CheckForUpdates { get; set; }

    /// <summary>What the launcher does once Play started the game: "Stay", "Minimize" or "Close".</summary>
    public string PlayAction { get; set; } = "Stay";

    public static Settings Load() => JsonFile.Load<Settings>(FilePath);

    public void Save() => JsonFile.Save(FilePath, this);
}

/// <summary>The launcher's small JSON files: a damaged or missing file reads as defaults, a save never leaves half a file.</summary>
internal static class JsonFile
{
    public static T Load<T>(string path)
        where T : new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteText(path, JsonSerializer.Serialize(value));
    }
}
