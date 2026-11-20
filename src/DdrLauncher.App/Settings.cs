using System.IO;

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

    /// <summary>What the launcher does once Play started the game: "Stay", "Minimize" or "Close".</summary>
    public string PlayAction { get; set; } = "Stay";

    public static Settings Load() => JsonFile.Load<Settings>(FilePath);

    public void Save() => JsonFile.Save(FilePath, this);
}
