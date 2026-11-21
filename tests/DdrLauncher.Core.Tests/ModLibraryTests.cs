using System.IO.Compression;
using DdrLauncher.Core.Game;
using DdrLauncher.Core.Mods;
using DdrLauncher.Core.Safety;

namespace DdrLauncher.Core.Tests;

public sealed class ModLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ddr-tests-" + Guid.NewGuid().ToString("N"));
    private bool _gameRunning;

    public ModLibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Library => Path.Combine(_dir, "Library");

    private string ModsFolder => Path.Combine(_dir, "game", "~mods");

    private ModLibrary NewLibrary() => new(Library, () => _gameRunning);

    private string WriteFile(string relative, string content)
    {
        var path = Path.Combine(_dir, "src", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Importing_a_pak_brings_its_utoc_and_ucas_and_nothing_else()
    {
        var pak = WriteFile("Car_P.pak", "pak");
        WriteFile("Car_P.utoc", "utoc");
        WriteFile("Car_P.ucas", "ucas");
        WriteFile("Other_P.pak", "other");

        var mod = NewLibrary().Import(pak);

        Assert.Equal("Car_P", mod.Name);
        Assert.Equal(["Car_P.pak", "Car_P.ucas", "Car_P.utoc"], mod.Files.Select(f => f.Name));
    }

    [Fact]
    public void Archive_entries_are_flattened_so_a_traversal_path_stays_in_the_library()
    {
        var zip = Path.Combine(_dir, "mod.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("../../evil_P.pak").Open()))
            {
                writer.Write("pak");
            }

            archive.CreateEntry("readme.txt");
        }

        var mod = NewLibrary().Import(zip);

        Assert.Equal(["evil_P.pak"], mod.Files.Select(f => f.Name));
        Assert.False(File.Exists(Path.Combine(_dir, "evil_P.pak")));
    }

    [Fact]
    public void An_import_without_pak_is_rejected_and_leaves_nothing_behind()
    {
        var folder = Path.GetDirectoryName(WriteFile("folder/readme.txt", "hi"))!;

        Assert.Throws<ImportRejectedException>(() => NewLibrary().Import(folder));
        Assert.Empty(Directory.GetDirectories(Library));
    }

    [Fact]
    public void Enable_then_disable_leaves_the_mods_folder_as_it_was()
    {
        var library = NewLibrary();
        var mod = library.Import(WriteFile("Car_P.pak", "pak"));

        library.Enable(mod, ModsFolder);
        Assert.True(File.Exists(Path.Combine(ModsFolder, "Car_P.pak")));

        Assert.Empty(library.Disable(mod, ModsFolder));
        Assert.Empty(Directory.GetFiles(ModsFolder));
        Assert.False(NewLibrary().Mods.Single().Enabled);
    }

    [Fact]
    public void A_file_changed_in_the_game_is_kept_on_disable()
    {
        var library = NewLibrary();
        var mod = library.Import(WriteFile("Car_P.pak", "pak"));
        library.Enable(mod, ModsFolder);
        File.WriteAllText(Path.Combine(ModsFolder, "Car_P.pak"), "edited by hand");

        Assert.Equal(["Car_P.pak"], library.Disable(mod, ModsFolder));
        Assert.Equal("edited by hand", File.ReadAllText(Path.Combine(ModsFolder, "Car_P.pak")));
    }

    [Fact]
    public void A_file_the_launcher_did_not_place_is_never_overwritten()
    {
        Directory.CreateDirectory(ModsFolder);
        File.WriteAllText(Path.Combine(ModsFolder, "Car_P.pak"), "someone else's");
        var library = NewLibrary();
        var mod = library.Import(WriteFile("Car_P.pak", "pak"));

        Assert.Throws<InvalidOperationException>(() => library.Enable(mod, ModsFolder));
        Assert.Equal("someone else's", File.ReadAllText(Path.Combine(ModsFolder, "Car_P.pak")));
        Assert.False(mod.Enabled);
    }

    [Fact]
    public void Two_enabled_mods_cannot_share_a_file_name()
    {
        var library = NewLibrary();
        var first = library.Import(WriteFile("a/Car_P.pak", "one"));
        var second = library.Import(WriteFile("b/Car_P.pak", "two"));
        library.Enable(first, ModsFolder);

        Assert.Throws<InvalidOperationException>(() => library.Enable(second, ModsFolder));
    }

    [Fact]
    public void Nothing_changes_while_the_game_runs()
    {
        var library = NewLibrary();
        var mod = library.Import(WriteFile("Car_P.pak", "pak"));
        _gameRunning = true;

        Assert.Throws<InvalidOperationException>(() => library.Enable(mod, ModsFolder));
        Assert.False(Directory.Exists(ModsFolder));
    }

    [Fact]
    public void Remove_disables_and_deletes_the_library_copy()
    {
        var library = NewLibrary();
        var mod = library.Import(WriteFile("Car_P.pak", "pak"));
        library.Enable(mod, ModsFolder);

        library.Remove(mod, ModsFolder);

        Assert.Empty(Directory.GetFiles(ModsFolder));
        Assert.Empty(NewLibrary().Mods);
        Assert.False(Directory.Exists(Path.Combine(Library, mod.Id)));
    }

    private string Ue4ssMods => Path.Combine(_dir, "game", "ue4ss", "Mods");

    private string WriteUe4ssMod(string folder)
    {
        WriteFile(folder + "/Scripts/main.lua", "print('hi')");
        WriteFile(folder + "/Scripts/menu.lua", "return {}");
        WriteFile(folder + "/native.dll", "MZ");
        return Path.Combine(_dir, "src", folder);
    }

    [Fact]
    public void A_ue4ss_mod_folder_is_imported_with_its_paths_and_without_native_code()
    {
        var mod = NewLibrary().Import(WriteUe4ssMod("Trainer"));

        Assert.True(mod.Ue4ss);
        Assert.Equal(["Trainer/enabled.txt", "Trainer/Scripts/main.lua", "Trainer/Scripts/menu.lua"], mod.Files.Select(f => f.Name));
    }

    [Fact]
    public void A_zipped_ue4ss_mod_keeps_its_folder_name()
    {
        var zip = Path.Combine(_dir, "download.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("Release/Trainer/Scripts/main.lua").Open()))
            {
                writer.Write("print('hi')");
            }

            archive.CreateEntry("Release/Trainer/enabled.txt");
        }

        var mod = NewLibrary().Import(zip);

        Assert.Equal(["Trainer/enabled.txt", "Trainer/Scripts/main.lua"], mod.Files.Select(f => f.Name));
    }

    [Fact]
    public void A_ue4ss_mod_with_a_traversal_path_is_rejected()
    {
        var zip = Path.Combine(_dir, "mod.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("Trainer/Scripts/main.lua");
            archive.CreateEntry("../../evil.lua");
        }

        Assert.Throws<ImportRejectedException>(() => NewLibrary().Import(zip));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.lua")));
        Assert.Empty(Directory.GetDirectories(Library));
    }

    [Fact]
    public void A_ue4ss_mod_goes_into_the_ue4ss_mods_folder_and_leaves_no_trace_when_disabled()
    {
        Directory.CreateDirectory(Ue4ssMods);
        var library = NewLibrary();
        var mod = library.Import(WriteUe4ssMod("Trainer"));

        library.Enable(mod, ModsFolder, Ue4ssMods);
        Assert.Equal("print('hi')", File.ReadAllText(Path.Combine(Ue4ssMods, "Trainer", "Scripts", "main.lua")));
        Assert.True(File.Exists(Path.Combine(Ue4ssMods, "Trainer", "enabled.txt")));
        Assert.False(Directory.Exists(ModsFolder));

        Assert.Empty(library.Disable(mod, ModsFolder, Ue4ssMods));
        Assert.Empty(Directory.GetFileSystemEntries(Ue4ssMods));
    }

    [Fact]
    public void A_ue4ss_mod_cannot_be_enabled_without_ue4ss()
    {
        var library = NewLibrary();
        var mod = library.Import(WriteUe4ssMod("Trainer"));

        Assert.Throws<InvalidOperationException>(() => library.Enable(mod, ModsFolder, Ue4ssMods));
        Assert.False(Directory.Exists(Path.Combine(_dir, "game", "ue4ss")));
    }

    [Fact]
    public void Steam_library_paths_are_read_from_libraryfolders_vdf()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0" { "path"  "C:\\Program Files (x86)\\Steam" }
                "1" { "path"  "D:\\SteamLibrary" }
            }
            """;

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], GameInstall.LibraryPaths(vdf));
    }
}
