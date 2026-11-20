using System.IO.Compression;
using System.Text;

namespace DdrLauncher.Core.Paks;

/// <summary>
/// Read-only access to an unencrypted Unreal Engine 4.26+ pak (version 11, the one Dakar Desert Rally ships).
/// Only the index is parsed up front; file data is read on request.
/// </summary>
public sealed class PakReader : IDisposable
{
    private const uint Magic = 0x5A6F12E1;
    private const int FooterSize = 16 + 1 + 4 + 4 + 8 + 8 + 20 + 5 * 32;

    private readonly FileStream _stream;
    private readonly string[] _compression;
    private readonly byte[] _encoded;
    private readonly List<Entry> _plain = [];
    private readonly Dictionary<string, int> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Oodle? _oodle;

    /// <param name="oodle">Decoder for Oodle blocks; null when none is available.</param>
    public PakReader(string path, Oodle? oodle = null)
    {
        _oodle = oodle;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            var reader = new BinaryReader(_stream);
            if (_stream.Length < FooterSize)
            {
                throw new InvalidDataException("Not a pak file.");
            }

            _stream.Position = _stream.Length - FooterSize;
            _stream.Position += 16;
            var encryptedIndex = reader.ReadByte() != 0;
            if (reader.ReadUInt32() != Magic)
            {
                throw new InvalidDataException("Not a pak file.");
            }

            var version = reader.ReadUInt32();
            if (version != 11)
            {
                throw new InvalidDataException($"Pak version {version} is not supported.");
            }

            if (encryptedIndex)
            {
                throw new InvalidDataException("Encrypted paks are not supported.");
            }

            var indexOffset = reader.ReadInt64();
            var indexSize = reader.ReadInt64();
            reader.ReadBytes(20);
            _compression = new string[5];
            for (var i = 0; i < 5; i++)
            {
                _compression[i] = Encoding.ASCII.GetString(reader.ReadBytes(32)).TrimEnd('\0');
            }

            var index = new BinaryReader(new MemoryStream(ReadAt(indexOffset, indexSize)));
            MountPoint = ReadString(index);
            index.ReadUInt32(); // entry count
            index.ReadUInt64(); // path hash seed
            if (index.ReadUInt32() != 0)
            {
                index.ReadBytes(8 + 8 + 20); // path hash index, not needed with the full directory index
            }

            if (index.ReadUInt32() == 0)
            {
                throw new InvalidDataException("This pak has no full directory index.");
            }

            var directoryOffset = index.ReadInt64();
            var directorySize = index.ReadInt64();
            index.ReadBytes(20);
            _encoded = index.ReadBytes(index.ReadInt32());
            var plainCount = index.ReadInt32();
            for (var i = 0; i < plainCount; i++)
            {
                _plain.Add(ReadPlainEntry(index));
            }

            var directories = new BinaryReader(new MemoryStream(ReadAt(directoryOffset, directorySize)));
            var directoryCount = directories.ReadInt32();
            for (var d = 0; d < directoryCount; d++)
            {
                var directory = ReadString(directories).TrimStart('/');
                var fileCount = directories.ReadInt32();
                for (var f = 0; f < fileCount; f++)
                {
                    _files[directory + ReadString(directories)] = directories.ReadInt32();
                }
            }
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    /// <summary>Where paths in this pak are mounted, relative to the game's Content/Paks folder (usually "../../../").</summary>
    public string MountPoint { get; }

    /// <summary>File paths as stored, relative to <see cref="MountPoint"/>, with forward slashes.</summary>
    public IEnumerable<string> Files => _files.Keys;

    public bool Contains(string path) => _files.ContainsKey(path);

    public byte[] Read(string path)
    {
        if (!_files.TryGetValue(path, out var location))
        {
            throw new FileNotFoundException($"{path} is not in this pak.");
        }

        var entry = location >= 0 ? DecodeEntry(location) : _plain[-location - 1];
        if (entry.Encrypted)
        {
            throw new InvalidDataException($"{path} is encrypted.");
        }

        if (entry.Compression is null)
        {
            return ReadAt(entry.Offset + HeaderSize(0, compressed: false), entry.Size);
        }

        var output = new byte[entry.Size];
        var written = 0;
        foreach (var (start, end) in entry.Blocks)
        {
            var block = ReadAt(entry.Offset + start, end - start);
            var length = (int)Math.Min(entry.BlockSize, entry.Size - written);
            Decompress(entry.Compression, block, output.AsSpan(written, length));
            written += length;
        }

        if (written != entry.Size)
        {
            throw new InvalidDataException($"{path} is truncated.");
        }

        return output;
    }

    public void Dispose() => _stream.Dispose();

    private void Decompress(string method, byte[] block, Span<byte> output)
    {
        switch (method)
        {
            case "Oodle":
                var oodle = _oodle ?? throw new OodleMissingException();
                oodle.Decompress(block, output);
                break;
            case "Zlib":
                using (var zlib = new ZLibStream(new MemoryStream(block), CompressionMode.Decompress))
                {
                    zlib.ReadExactly(output);
                }

                break;
            default:
                throw new InvalidDataException($"Compression '{method}' is not supported.");
        }
    }

    // Layout from UE's FPakEntry::EncodeTo: one flags word, then 32 or 64 bit numbers, then block sizes.
    private Entry DecodeEntry(int position)
    {
        var reader = new BinaryReader(new MemoryStream(_encoded, position, _encoded.Length - position));
        var bits = reader.ReadUInt32();
        var method = (int)((bits >> 23) & 0x3f);
        var encrypted = (bits & (1u << 22)) != 0;
        var blockCount = (int)((bits >> 6) & 0xffff);
        long blockSize = bits & 0x3f;
        blockSize = blockSize == 0x3f ? reader.ReadUInt32() : blockSize << 11;
        long Number(int bit) => (bits & (1u << bit)) != 0 ? reader.ReadUInt32() : reader.ReadInt64();
        var offset = Number(31);
        var size = Number(30);
        var compressedSize = method == 0 ? size : Number(29);

        var blocks = new List<(long, long)>();
        var start = HeaderSize(blockCount, method != 0);
        if (blockCount == 1 && !encrypted)
        {
            blocks.Add((start, start + compressedSize));
        }
        else
        {
            for (var i = 0; i < blockCount; i++)
            {
                long length = reader.ReadUInt32();
                blocks.Add((start, start + length));
                start += encrypted ? (length + 15) & ~15L : length;
            }
        }

        return new Entry(offset, size, method == 0 ? null : _compression[method - 1], encrypted, blockSize, blocks);
    }

    private Entry ReadPlainEntry(BinaryReader reader)
    {
        var offset = reader.ReadInt64();
        reader.ReadInt64(); // compressed size
        var size = reader.ReadInt64();
        var method = reader.ReadInt32();
        reader.ReadBytes(20);
        var blocks = new List<(long, long)>();
        if (method != 0)
        {
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                blocks.Add((reader.ReadInt64(), reader.ReadInt64()));
            }
        }

        var encrypted = reader.ReadByte() != 0;
        var blockSize = reader.ReadUInt32();
        return new Entry(offset, size, method == 0 ? null : _compression[method - 1], encrypted, blockSize, blocks);
    }

    // Size of the FPakEntry copy stored in front of each file's data.
    private static long HeaderSize(int blockCount, bool compressed) => 8 + 8 + 8 + 4 + 20 + (compressed ? 4 + 16L * blockCount : 0) + 1 + 4;

    private byte[] ReadAt(long offset, long length)
    {
        if (offset < 0 || length < 0 || offset + length > _stream.Length || length > int.MaxValue)
        {
            throw new InvalidDataException("The pak points outside itself.");
        }

        var buffer = new byte[length];
        _stream.Position = offset;
        _stream.ReadExactly(buffer);
        return buffer;
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        return length switch
        {
            0 => "",
            > 0 => Encoding.UTF8.GetString(reader.ReadBytes(length), 0, length - 1),
            _ => Encoding.Unicode.GetString(reader.ReadBytes(-length * 2), 0, (-length - 1) * 2),
        };
    }

    private sealed record Entry(long Offset, long Size, string? Compression, bool Encrypted, long BlockSize, List<(long Start, long End)> Blocks);
}
