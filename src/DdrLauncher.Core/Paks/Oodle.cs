using System.Runtime.InteropServices;

namespace DdrLauncher.Core.Paks;

/// <summary>Oodle decompression through RAD's oo2core library, which the game's paks are compressed with.</summary>
public sealed class Oodle : IDisposable
{
    public const string LibraryName = "oo2core_9_win64.dll";

    private readonly IntPtr _library;
    private readonly DecompressFn _decompress;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate long DecompressFn(byte[] source, long sourceSize, ref byte destination, long destinationSize,
        int fuzzSafe, int checkCrc, int verbosity, IntPtr decodeBase, long decodeBaseSize,
        IntPtr callback, IntPtr callbackData, IntPtr decoderMemory, long decoderMemorySize, int threadPhase);

    private Oodle(string path)
    {
        _library = NativeLibrary.Load(path);
        _decompress = Marshal.GetDelegateForFunctionPointer<DecompressFn>(NativeLibrary.GetExport(_library, "OodleLZ_Decompress"));
    }

    /// <summary>Loads the first oo2core library found in <paramref name="folders"/>; null when there is none.</summary>
    public static Oodle? Find(params string[] folders)
    {
        foreach (var folder in folders)
        {
            var path = Path.Combine(folder, LibraryName);
            if (File.Exists(path))
            {
                return new Oodle(path);
            }
        }

        return null;
    }

    public void Decompress(byte[] source, Span<byte> destination)
    {
        // Fuzz-safe mode: a damaged block fails instead of reading out of bounds.
        var written = _decompress(source, source.Length, ref MemoryMarshal.GetReference(destination), destination.Length,
            1, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 3);
        if (written != destination.Length)
        {
            throw new InvalidDataException("An Oodle block could not be decompressed.");
        }
    }

    public void Dispose() => NativeLibrary.Free(_library);
}

/// <summary>The game's paks are Oodle-compressed and no Oodle library was found to read them.</summary>
public sealed class OodleMissingException() : Exception($"{Oodle.LibraryName} missing");
