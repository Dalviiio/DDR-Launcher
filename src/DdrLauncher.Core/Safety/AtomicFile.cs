using System.Text;

namespace DdrLauncher.Core.Safety;

/// <summary>
/// Writes a file through a temporary file next to it, then moves it into place,
/// so a crash or a full disk never leaves a half-written file behind.
/// </summary>
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            write(stream);
        }

        File.Move(temp, path, overwrite: true);
    }

    public static void Write(string path, byte[] bytes) => Write(path, s => s.Write(bytes));

    public static void WriteText(string path, string text) => Write(path, Encoding.UTF8.GetBytes(text));
}
