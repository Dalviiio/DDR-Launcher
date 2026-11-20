using System.Security.Cryptography;
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
        try
        {
            using (var stream = File.Create(temp))
            {
                write(stream);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    public static void Write(string path, byte[] bytes) => Write(path, s => s.Write(bytes));

    public static void WriteText(string path, string text) => Write(path, Encoding.UTF8.GetBytes(text));
}

/// <summary>
/// SHA-256 hashing of files, read as raw data.
/// </summary>
public static class FileHasher
{
    /// <summary>
    /// Returns the lowercase hexadecimal SHA-256 of a file.
    /// </summary>
    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        return Sha256(stream);
    }

    public static string Sha256(Stream stream)
    {
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool HashEquals(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Rules every write to the game's mods folder follows.</summary>
public static class ModsGuard
{
    /// <summary>The game reads its mods when it starts and keeps them open: nothing in ~mods changes while it runs.</summary>
    public static void EnsureGameClosed(Func<bool> isGameRunning)
    {
        if (isGameRunning())
        {
            throw new InvalidOperationException("Close Dakar Desert Rally first: its mods cannot change while it is running.");
        }
    }
}
