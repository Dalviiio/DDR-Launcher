using System.IO;
using System.Text.Json;
using System.Windows;
using DdrLauncher.App.Localization;
using DdrLauncher.Core.Data;
using DdrLauncher.Core.Game;
using DdrLauncher.Core.Mods;
using static DdrLauncher.App.Localization.Translator;

namespace DdrLauncher.App;

public partial class App : Application
{
    /// <summary>Started by the uninstaller: takes the launcher's changes out of the game, then exits.</summary>
    public const string UninstallCleanup = "--uninstall-cleanup";

    /// <summary>Left next to the exe by the installer with the choices made there; applied once, then deleted.</summary>
    private const string InstallOptionsFile = "install-options.json";

    // Two launchers writing the same library index would corrupt it.
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\DalviiioDDRLauncher", out var first);
        var settings = Settings.Load();
        ApplyInstallOptions(settings);
        Translator.Instance.Use(Translator.Pick(settings.Language));

        if (e.Args.Contains(UninstallCleanup, StringComparer.OrdinalIgnoreCase))
        {
            // Exit codes for the uninstaller: 0 done, 1 failed (a message said why), 2 the launcher is open.
            Shutdown(first ? Cleanup(settings) : 2);
            return;
        }

        if (!first)
        {
            MessageBox.Show(T("Dalviiio DDR Launcher is already open."), "Dalviiio DDR Launcher");
            Shutdown();
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    // The language picked in the installer becomes the launcher's.
    private static void ApplyInstallOptions(Settings settings)
    {
        var path = Path.Combine(AppContext.BaseDirectory, InstallOptionsFile);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            if (JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) is { } options
                && options.TryGetValue("language", out var code) && Translator.Languages.Any(l => l.Code == code))
            {
                settings.Language = code;
                settings.Save();
            }

            File.Delete(path);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged or locked file is ignored: the launcher follows Windows' language.
        }
    }

    // Leaves the game as it ships: the launcher's pak and the mods it placed go, each only if unchanged since placed.
    private static int Cleanup(Settings settings)
    {
        if (!GameInstall.IsValid(settings.GameFolder))
        {
            return 0;
        }

        var game = new GameInstall(settings.GameFolder!);
        try
        {
            var library = new ModLibrary(Path.Combine(Settings.DataFolder, "Library"), GameInstall.IsGameRunning);
            foreach (var mod in library.Mods.Where(m => m.Enabled).ToList())
            {
                library.Disable(mod, game.ModsFolder);
            }

            var store = LineupStore.Load();
            if (store.PlacedSha256 is not null)
            {
                Lineup.Install(null, game.ModsFolder, store.PlacedSha256, GameInstall.IsGameRunning);
                store.PlacedSha256 = null;
                store.Applied = null;
                store.Save();
            }

            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            MessageBox.Show(e.Message, "Dalviiio DDR Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }
    }
}
