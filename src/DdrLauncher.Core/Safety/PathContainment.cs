namespace DdrLauncher.Core.Safety;

/// <summary>
/// Checks whether a path lies strictly inside a root folder, after full normalization.
/// </summary>
public static class PathContainment
{
    /// <summary>
    /// Normalizes a folder path: absolute, no trailing separator.
    /// </summary>
    public static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Root folder must not be empty.", nameof(root));
        }

        var full = Path.GetFullPath(root);
        var trimmed = Path.TrimEndingDirectorySeparator(full);
        return trimmed;
    }

    /// <summary>
    /// Returns true when <paramref name="candidate"/> is strictly inside <paramref name="root"/>
    /// (the root itself is not considered inside). Comparison is case-insensitive, as on NTFS.
    /// </summary>
    public static bool IsStrictlyInside(string root, string candidate)
    {
        var normalizedRoot = NormalizeRoot(root);
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));

        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.Length > prefix.Length
            && normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
