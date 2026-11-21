using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DdrLauncher.App.Controls;
using DdrLauncher.App.Localization;
using DdrLauncher.Core.Data;
using DdrLauncher.Core.Game;
using DdrLauncher.Core.Mods;
using static DdrLauncher.App.Localization.Translator;

namespace DdrLauncher.App;

public partial class App : Application
{
    /// <summary>The GitHub repository whose releases hold the signed installers.</summary>
    public const string ReleaseRepository = "Dalviiio/DDR-Launcher";

    /// <summary>The author's public signing key: an update installs only when its installer is signed with the matching private key.</summary>
    public const string UpdatePublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEl7qwqZrV3AKrL0BGvBb0j74/q5LwTG1//AM6vza+Nm92FgRrAedVH3Q4LimSZzsEp29SZIJ7sYweQOvgiJVI2A==";

    /// <summary>Started by the uninstaller: takes the launcher's changes out of the game, then exits.</summary>
    public const string UninstallCleanup = "--uninstall-cleanup";

    // Two launchers writing the same library index would corrupt it.
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\DalviiioDDRLauncher", out var first);
        var settings = Settings.Load();
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

        // An unexpected error is written to the log and shown; the launcher keeps running when it can.
        DispatcherUnhandledException += OnUnexpectedError;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Background task", args.Exception);
            args.SetObserved();
        };

        Log.Info($"Started version {typeof(App).Assembly.GetName().Version!.ToString(3)} in {Translator.Instance.Current.Code}");
        base.OnStartup(e);
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnexpectedError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unexpected error", e.Exception);
        e.Handled = true;
        var dialog = new DialogWindow(T("Something went wrong"),
            DialogWindow.MessageText(T("{0}\n\nThe launcher keeps running. The details are in its log: {1}", e.Exception.Message, Log.FilePath)), DialogKind.Error)
        {
            Owner = Current.MainWindow is { IsLoaded: true } window ? window : null,
        };
        dialog.AddButton(T("Close"), null, primary: true);
        dialog.ShowDialog();
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
                library.Disable(mod, game.ModsFolder, game.Ue4ssModsFolder);
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

/// <summary>
/// The launcher's log (Logs/launcher.log in its data folder): what it did and what went wrong, for bug reports.
/// The user's folder is written as %USERPROFILE%, and the log starts over when it grows past 1 MB.
/// </summary>
public static class Log
{
    public static readonly string FilePath = Path.Combine(Settings.DataFolder, "Logs", "launcher.log");

    private static readonly object Lock = new();

    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string context, Exception? e) => Write("ERROR", $"{context}: {e}");

    private static void Write(string level, string message)
    {
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1 << 20)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                // Logs end up in public bug reports: the user's folder (and so their name) is not written.
                message = message.Replace(Profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written must never stop the launcher.
            }
        }
    }
}
