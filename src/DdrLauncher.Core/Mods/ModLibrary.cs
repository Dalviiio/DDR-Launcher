using System.Text.Json;
using DdrLauncher.Core.Safety;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace DdrLauncher.Core.Mods;

/// <summary>
/// The mod library. Imported mods are stored in <c>libraryRoot/&lt;id&gt;/</c>; enabling a mod copies its files into the
/// game's mods folder, disabling removes them again. Only files the launcher placed itself are ever deleted.
/// </summary>
public sealed class ModLibrary
{
    public static readonly IReadOnlySet<string> PakExtensions =
        new HashSet<string>([".pak", ".utoc", ".ucas"], StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> ArchiveExtensions =
        new HashSet<string>([".zip", ".rar", ".7z"], StringComparer.OrdinalIgnoreCase);

    private const string IndexFile = "library.json";
    private const int MaxFiles = 1_000;
    private const double MaxCompressionRatio = 200;
    private const long RatioThresholdBytes = 50L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _root;
    private readonly Func<bool> _isGameRunning;
    private readonly List<ModEntry> _mods;

    public ModLibrary(string libraryRoot, Func<bool> isGameRunning)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        _isGameRunning = isGameRunning;
        Directory.CreateDirectory(_root);
        _mods = LoadIndex();
    }

    public IReadOnlyList<ModEntry> Mods => _mods;

    /// <summary>
    /// Imports a .pak file (with its .utoc/.ucas next to it), an archive or a folder. Only pak files are kept, flattened
    /// by file name, so no path from the source is ever used on disk.
    /// </summary>
    public ModEntry Import(string source)
    {
        var name = Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(source));
        var id = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..23];
        var staging = Path.Combine(_root, id + ".tmp");
        Directory.CreateDirectory(staging);
        try
        {
            if (Directory.Exists(source))
            {
                CopyFromFolder(source, staging);
            }
            else if (PakExtensions.Contains(Path.GetExtension(source)))
            {
                CopyPakWithSiblings(source, staging);
            }
            else if (ArchiveExtensions.Contains(Path.GetExtension(source)))
            {
                ExtractArchive(source, staging);
            }
            else
            {
                throw new ImportRejectedException("only .pak files, .zip/.rar/.7z archives and folders can be added");
            }

            var files = Directory.GetFiles(staging)
                .Select(f => new ModFile(Path.GetFileName(f), new FileInfo(f).Length, FileHasher.Sha256(f)))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!files.Any(f => f.Name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ImportRejectedException("it contains no .pak file");
            }

            Directory.Move(staging, Path.Combine(_root, id));
            var mod = new ModEntry { Id = id, Name = name, AddedUtc = DateTime.UtcNow, Files = files };
            _mods.Add(mod);
            SaveIndex();
            return mod;
        }
        catch
        {
            Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    /// <summary>Copies the mod's files into the game. Refuses to overwrite a file the launcher did not place.</summary>
    public void Enable(ModEntry mod, string modsFolder)
    {
        EnsureGameClosed();
        foreach (var file in mod.Files)
        {
            var owner = _mods.FirstOrDefault(m => m != mod && m.Enabled && m.Files.Any(f => NameEquals(f.Name, file.Name)));
            if (owner is not null)
            {
                throw new InvalidOperationException($"'{file.Name}' is also in '{owner.Name}', which is enabled. Disable it first.");
            }

            var target = Path.Combine(modsFolder, file.Name);
            if (File.Exists(target) && !FileHasher.HashEquals(FileHasher.Sha256(target), file.Sha256))
            {
                throw new InvalidOperationException($"'{file.Name}' already exists in the game's mods folder and was not placed by the launcher.");
            }
        }

        Directory.CreateDirectory(modsFolder);
        foreach (var file in mod.Files)
        {
            var target = Path.Combine(modsFolder, file.Name);
            if (File.Exists(target))
            {
                continue; // same hash, checked above
            }

            var temp = target + ".ddr-tmp";
            File.Copy(Path.Combine(_root, mod.Id, file.Name), temp, overwrite: true);
            File.Move(temp, target);
        }

        mod.Enabled = true;
        SaveIndex();
    }

    /// <summary>
    /// Removes the mod's files from the game. A file changed since it was placed is left where it is; its name is returned.
    /// </summary>
    public IReadOnlyList<string> Disable(ModEntry mod, string modsFolder)
    {
        EnsureGameClosed();
        var kept = new List<string>();
        foreach (var file in mod.Files)
        {
            var target = Path.Combine(modsFolder, file.Name);
            if (!File.Exists(target))
            {
                continue;
            }

            if (FileHasher.HashEquals(FileHasher.Sha256(target), file.Sha256))
            {
                File.Delete(target);
            }
            else
            {
                kept.Add(file.Name);
            }
        }

        mod.Enabled = false;
        SaveIndex();
        return kept;
    }

    /// <summary>Disables the mod if needed, then deletes it from the library.</summary>
    public IReadOnlyList<string> Remove(ModEntry mod, string? modsFolder)
    {
        var kept = mod.Enabled && modsFolder is not null ? Disable(mod, modsFolder) : [];
        _mods.Remove(mod);
        SaveIndex();
        Directory.Delete(Path.Combine(_root, mod.Id), recursive: true);
        return kept;
    }

    private void EnsureGameClosed() => ModsGuard.EnsureGameClosed(_isGameRunning);

    private static bool NameEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void CopyPakWithSiblings(string pak, string staging)
    {
        var stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pak))!, Path.GetFileNameWithoutExtension(pak));
        foreach (var ext in PakExtensions)
        {
            if (File.Exists(stem + ext))
            {
                File.Copy(stem + ext, Path.Combine(staging, Path.GetFileName(stem + ext)));
            }
        }
    }

    private static void CopyFromFolder(string folder, string staging)
    {
        if (new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ImportRejectedException("the selected folder is a link");
        }

        // Links inside the folder are never followed.
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 32 };
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*", options).Where(f => PakExtensions.Contains(Path.GetExtension(f))))
        {
            if (++count > MaxFiles)
            {
                throw new ImportRejectedException("it holds too many files");
            }

            var target = Path.Combine(staging, Path.GetFileName(file));
            if (File.Exists(target))
            {
                throw new ImportRejectedException($"two files are named '{Path.GetFileName(file)}'");
            }

            File.Copy(file, target);
        }
    }

    private static void ExtractArchive(string archivePath, string staging)
    {
        var archiveLength = new FileInfo(archivePath).Length;
        long written = 0;
        var count = 0;
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
            if (archive.IsEncrypted)
            {
                throw new ImportRejectedException("encrypted archives are not supported");
            }

            foreach (var (entry, open) in EnumerateEntries(archive))
            {
                if (entry.IsDirectory || entry.Key is null)
                {
                    continue;
                }

                var fileName = entry.Key.Split('/', '\\')[^1];
                if (!PakExtensions.Contains(Path.GetExtension(fileName)))
                {
                    continue;
                }

                if (entry.LinkTarget is not null)
                {
                    continue; // links are never extracted
                }

                ArchivePathValidator.GetSafeSegments(fileName);
                if (++count > MaxFiles)
                {
                    throw new ImportRejectedException("it holds too many files");
                }

                var target = Path.Combine(staging, fileName);
                if (File.Exists(target))
                {
                    throw new ImportRejectedException($"two files are named '{fileName}'");
                }

                using var input = open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    written += read;
                    if (written > RatioThresholdBytes && written > archiveLength * MaxCompressionRatio)
                    {
                        throw new ImportRejectedException("it expands far more than a normal archive (possible archive bomb)");
                    }

                    output.Write(buffer, 0, read);
                }
            }
        }
        catch (Exception e) when (e is UnsafePathException or InvalidFormatException or ArchiveException or InvalidOperationException)
        {
            throw new ImportRejectedException($"the archive cannot be read safely ({e.Message})", e);
        }
    }

    private static IEnumerable<(IEntry Entry, Func<Stream> Open)> EnumerateEntries(IArchive archive)
    {
        if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                yield return (reader.Entry, reader.OpenEntryStream);
            }
        }
        else
        {
            foreach (var entry in archive.Entries)
            {
                yield return (entry, entry.OpenEntryStream);
            }
        }
    }

    private List<ModEntry> LoadIndex()
    {
        var path = Path.Combine(_root, IndexFile);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ModEntry>>(File.ReadAllText(path), Json) ?? [];
        }
        catch (JsonException)
        {
            // A damaged index must not lose the library: keep it aside and start empty.
            File.Copy(path, path + ".damaged", overwrite: true);
            return [];
        }
    }

    private void SaveIndex()
    {
        var path = Path.Combine(_root, IndexFile);
        AtomicFile.WriteText(path, JsonSerializer.Serialize(_mods, Json));
    }
}
