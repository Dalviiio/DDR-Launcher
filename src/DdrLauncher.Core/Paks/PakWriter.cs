using System.Security.Cryptography;
using System.Text;

namespace DdrLauncher.Core.Paks;

/// <summary>
/// Writes an uncompressed, unencrypted version 11 pak, laid out the same way as repak does,
/// so the game loads it from ~mods like any other mod pak.
/// </summary>
public static class PakWriter
{
    private const uint Magic = 0x5A6F12E1;

    /// <param name="files">Pak paths (forward slashes, relative to the mount point) and their bytes.</param>
    public static void Write(Stream output, IReadOnlyDictionary<string, byte[]> files, string mountPoint = "../../../")
    {
        var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        var start = output.Position;
        var entries = new List<(string Path, long Offset, byte[] Data)>();
        // Data in case-insensitive path order like repak's packer; the index below is sorted ordinally.
        foreach (var (path, data) in files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add((path, output.Position - start, data));

            // Each file is preceded by a copy of its entry.
            WriteEntryHeader(writer, data.Length, SHA1.HashData(data));
            writer.Write(data);
        }

        entries.Sort((x, y) => string.CompareOrdinal(x.Path, y.Path));
        var encoded = new MemoryStream();
        var encodedWriter = new BinaryWriter(encoded);
        var offsets = new List<uint>();
        foreach (var e in entries)
        {
            offsets.Add((uint)encoded.Length);
            var smallOffset = e.Offset <= uint.MaxValue;
            encodedWriter.Write((1u << 29) | (1u << 30) | (smallOffset ? 1u << 31 : 0));
            if (smallOffset)
            {
                encodedWriter.Write((uint)e.Offset);
            }
            else
            {
                encodedWriter.Write(e.Offset);
            }

            encodedWriter.Write((uint)e.Data.Length);
        }

        var pathHashIndex = new MemoryStream();
        var phi = new BinaryWriter(pathHashIndex);
        phi.Write(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            phi.Write(Fnv64Path(entries[i].Path, 0));
            phi.Write(offsets[i]);
        }

        phi.Write(0);

        var directories = new SortedDictionary<string, SortedDictionary<string, uint>>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var path = entries[i].Path;
            var parent = path;
            while (SplitPath(parent) is { } split)
            {
                parent = split.Parent;
                directories.TryAdd(parent, new(StringComparer.Ordinal));
            }

            var (directory, name) = SplitPath(path)!.Value;
            directories[directory][name] = offsets[i];
        }

        var fullDirectoryIndex = new MemoryStream();
        var fdi = new BinaryWriter(fullDirectoryIndex);
        fdi.Write(directories.Count);
        foreach (var (directory, names) in directories)
        {
            WriteString(fdi, directory);
            fdi.Write(names.Count);
            foreach (var (name, offset) in names)
            {
                WriteString(fdi, name);
                fdi.Write(offset);
            }
        }

        var indexOffset = output.Position - start;
        var index = new MemoryStream();
        var iw = new BinaryWriter(index);
        WriteString(iw, mountPoint);
        iw.Write(entries.Count);
        iw.Write(0UL); // path hash seed
        var beforeHashIndex = 4 + Encoding.UTF8.GetByteCount(mountPoint) + 1 + 4 + 8 + 4 + 36 + 4 + 36 + 4 + encoded.Length + 4;
        var hashIndexOffset = indexOffset + beforeHashIndex;
        iw.Write(1);
        iw.Write(hashIndexOffset);
        iw.Write(pathHashIndex.Length);
        iw.Write(SHA1.HashData(pathHashIndex.ToArray()));
        iw.Write(1);
        iw.Write(hashIndexOffset + pathHashIndex.Length);
        iw.Write(fullDirectoryIndex.Length);
        iw.Write(SHA1.HashData(fullDirectoryIndex.ToArray()));
        iw.Write((int)encoded.Length);
        iw.Write(encoded.ToArray());
        iw.Write(0); // no entries outside the encoded list

        writer.Write(index.ToArray());
        writer.Write(pathHashIndex.ToArray());
        writer.Write(fullDirectoryIndex.ToArray());

        writer.Write(new byte[16]); // encryption key guid
        writer.Write((byte)0); // index not encrypted
        writer.Write(Magic);
        writer.Write(11);
        writer.Write(indexOffset);
        writer.Write(index.Length);
        writer.Write(SHA1.HashData(index.ToArray()));
        writer.Write(new byte[5 * 32]); // no compression methods
        writer.Flush();
    }

    private static void WriteEntryHeader(BinaryWriter writer, long size, byte[] hash)
    {
        writer.Write(0L); // offset: zero in the copy stored with the data
        writer.Write(size); // compressed size
        writer.Write(size);
        writer.Write(0); // no compression
        writer.Write(hash);
        writer.Write((byte)0); // flags
        writer.Write(0); // compression block size
    }

    private static void WriteString(BinaryWriter writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        writer.Write(bytes.Length + 1);
        writer.Write(bytes);
        writer.Write((byte)0);
    }

    // "a/b/c" -> ("a/b/", "c"); "a/b/" -> ("a/", "b"); "a" -> ("/", "a"); "/" -> null.
    private static (string Parent, string Name)? SplitPath(string path)
    {
        if (path is "/" or "")
        {
            return null;
        }

        var trimmed = path.EndsWith('/') ? path[..^1] : path;
        var i = trimmed.LastIndexOf('/');
        return i < 0 ? ("/", trimmed) : (trimmed[..(i + 1)], trimmed[(i + 1)..]);
    }

    internal static ulong Fnv64Path(string path, ulong seed)
    {
        var hash = 0xcbf29ce484222325UL + seed;
        foreach (var b in Encoding.Unicode.GetBytes(path.ToLowerInvariant()))
        {
            hash ^= b;
            hash *= 0x100000001b3UL;
        }

        return hash;
    }
}
