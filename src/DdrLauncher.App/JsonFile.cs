using System.IO;
using System.Text.Json;
using DdrLauncher.Core.Safety;

namespace DdrLauncher.App;

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
