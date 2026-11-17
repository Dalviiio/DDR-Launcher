using System.IO;
using System.Text.Json;

namespace DdrLauncher.App;

/// <summary>The launcher's own data folder and the few settings it remembers.</summary>
public sealed class Settings
{
    public static readonly string DataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DalviiioDDRLauncher");

    private static readonly string FilePath = Path.Combine(DataFolder, "settings.json");

    public string? GameFolder { get; set; }

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this));
        File.Move(FilePath + ".tmp", FilePath, overwrite: true);
    }
}
