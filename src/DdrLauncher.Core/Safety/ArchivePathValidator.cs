

namespace DdrLauncher.Core.Safety;

/// <summary>
/// Validates paths coming from untrusted archives (Zip Slip, traversal, absolute paths,
/// alternate data streams, Windows reserved names) before anything is written to disk.
/// </summary>
public static class ArchivePathValidator
{
    public const int MaxSegmentLength = 255;

    private static readonly char[] Separators = ['/', '\\'];

    private static readonly char[] ForbiddenChars = ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = BuildReservedNames();

    /// <summary>
    /// Validates an archive entry path and returns its segments. Throws <see cref="UnsafePathException"/>
    /// if the path is not a plain relative path.
    /// </summary>
    public static IReadOnlyList<string> GetSafeSegments(string entryPath)
    {
        if (string.IsNullOrEmpty(entryPath))
        {
            throw new UnsafePathException(entryPath ?? string.Empty, "path is empty");
        }

        if (entryPath[0] is '/' or '\\')
        {
            throw new UnsafePathException(entryPath, "absolute or UNC paths are not allowed");
        }

        // A single trailing separator marks a directory entry.
        var trimmed = entryPath[^1] is '/' or '\\' ? entryPath[..^1] : entryPath;
        var segments = trimmed.Split(Separators);

        foreach (var segment in segments)
        {
            ValidateSegment(entryPath, segment);
        }

        return segments;
    }

    private static void ValidateSegment(string entryPath, string segment)
    {
        if (segment.Length == 0)
        {
            throw new UnsafePathException(entryPath, "path contains an empty segment");
        }

        if (segment is "." or "..")
        {
            throw new UnsafePathException(entryPath, "relative segments ('.' or '..') are not allowed");
        }

        if (segment.Length > MaxSegmentLength)
        {
            throw new UnsafePathException(entryPath, "a segment is too long");
        }

        if (segment.Contains(':'))
        {
            throw new UnsafePathException(entryPath, "drive letters and alternate data streams are not allowed");
        }

        foreach (var c in segment)
        {
            if (c < 0x20 || Array.IndexOf(ForbiddenChars, c) >= 0)
            {
                throw new UnsafePathException(entryPath, "path contains a forbidden character");
            }
        }

        // Windows silently strips trailing dots and spaces, which would let two names alias each other.
        if (segment[^1] is '.' or ' ')
        {
            throw new UnsafePathException(entryPath, "a segment ends with a dot or a space");
        }

        if (IsReservedName(segment))
        {
            throw new UnsafePathException(entryPath, "Windows reserved device names are not allowed");
        }
    }

    private static bool IsReservedName(string segment)
    {
        // "CON", "con.txt" and "CON .txt" all refer to the console device.
        var dot = segment.IndexOf('.');
        var baseName = (dot >= 0 ? segment[..dot] : segment).TrimEnd(' ');
        return ReservedNames.Contains(baseName);
    }

    private static HashSet<string> BuildReservedNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        };

        // COM0-9 / LPT0-9 plus the superscript digits Windows also treats as device names.
        foreach (var digit in "0123456789¹²³")
        {
            names.Add("COM" + digit);
            names.Add("LPT" + digit);
        }

        return names;
    }
}

/// <summary>Thrown when a path is rejected by a safety rule. The path is never used after this is thrown.</summary>
public sealed class UnsafePathException(string path, string reason) : Exception($"Unsafe path '{path}': {reason}");
