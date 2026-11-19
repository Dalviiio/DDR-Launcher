using System.IO;

namespace DdrLauncher.App;

/// <summary>The launcher's own data folder and the few settings it remembers.</summary>
public sealed class Settings
{
    public static readonly string DataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DalviiioDDRLauncher");

    private static readonly string FilePath = Path.Combine(DataFolder, "settings.json");

    public string? GameFolder { get; set; }

    public static Settings Load() => JsonFile.Load<Settings>(FilePath);

    public void Save() => JsonFile.Save(FilePath, this);
}
