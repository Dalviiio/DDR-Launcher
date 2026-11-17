using System.Security.Cryptography;

namespace DdrLauncher.Core.Safety;

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
