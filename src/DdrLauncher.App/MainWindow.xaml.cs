using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using DdrLauncher.App.Controls;
using DdrLauncher.App.ViewModels;
using DdrLauncher.Core;
using Microsoft.Win32;
using static DdrLauncher.App.Localization.Translator;

namespace DdrLauncher.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        RoundedCorners.Apply(this, 24);
        StateChanged += (_, _) =>
        {
            var maximized = WindowState == WindowState.Maximized;
            Ui.SetIcon(MaximizeButton, (System.Windows.Media.Geometry)FindResource(maximized ? "Icon.RestoreWindow" : "Icon.Maximize"));
            MaximizeButton.ToolTip = T(maximized ? "Restore" : "Maximize");

            // A maximized borderless window overflows the screen by its resize border.
            Root.Margin = maximized ? new Thickness(7) : new Thickness(0);
            Root.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(24);
        };
        Loaded += async (_, _) => await CheckForUpdateAsync();
    }

    // At start: ask once whether to look for new versions, then look quietly; a failed check only goes to the log.
    private async Task CheckForUpdateAsync()
    {
        if (_vm.UpdatesNotAsked)
        {
            _vm.CheckForUpdates = Confirm(T("Check for new versions?"),
                T("Should the launcher look for a new version each time it starts?\n\nIt only reads the list of versions on its GitHub page (github.com/{0}). Nothing about you, your game or your mods is sent. Each update is checked with the author's signature before it is installed.\n\nYou can change this in Settings.", App.ReleaseRepository),
                T("Check"), DialogKind.Info);
        }

        if (!_vm.CheckForUpdates)
        {
            return;
        }

        try
        {
            _vm.Update = await _vm.Updates.CheckAsync(MainViewModel.Version);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FormatException or ArgumentException)
        {
            Log.Warn("Update check: " + e.Message);
            return;
        }

        if (_vm.Update is { } update)
        {
            Log.Info($"Version {update.Version} is available");
            if (Confirm(T("A new version is available"), UpdateMessage(update), T("Update"), DialogKind.Info))
            {
                await InstallUpdateAsync(update);
            }
        }
    }

    private async void OnUpdate(object sender, RoutedEventArgs e)
    {
        if (_vm.Update is { } update && Confirm(T("A new version is available"), UpdateMessage(update), T("Update"), DialogKind.Info))
        {
            await InstallUpdateAsync(update);
        }
    }

    private static string UpdateMessage(AvailableUpdate update) =>
        T("Dalviiio DDR Launcher {0} is available (you have {1}).", update.Version, MainViewModel.Version)
        + (string.IsNullOrWhiteSpace(update.Notes) ? "" : "\n\n" + T("What's new:") + "\n" + update.Notes.Trim())
        + "\n\n" + T("The launcher closes, installs it and opens again. Your profiles, liveries and mods stay.");

    // Downloads the signed installer, checks it, then hands over: the installer waits for the launcher to close and reopens it.
    private async Task InstallUpdateAsync(AvailableUpdate update)
    {
        _vm.Report(T("Downloading version {0}...", update.Version));
        try
        {
            var installer = await _vm.Updates.DownloadVerifiedAsync(update);
            Log.Info($"Installing version {update.Version}");
            Process.Start(new ProcessStartInfo(installer, "/SILENT /SUPPRESSMSGBOXES /NOCANCEL") { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Update", e);
            _vm.Report(T("The update did not work: {0}", e.Message), true);
        }
    }

    private async void OnImportFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("Import mods"),
            Filter = "Mods (*.pak, *.zip, *.rar, *.7z)|*.pak;*.zip;*.rar;*.7z",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await _vm.ImportAsync(dialog.FileNames);
        }
    }

    private async void OnImportFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = T("Import a mod folder") };
        if (dialog.ShowDialog(this) == true)
        {
            await _vm.ImportAsync([dialog.FolderName]);
        }
    }

    private async void OnRemoveMod(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedMod is not { } mod)
        {
            return;
        }

        if (Confirm(T("Remove this mod?"), T("'{0}' will be taken out of the game and deleted from your library.", mod.Name), T("Remove")))
        {
            await _vm.RemoveAsync(mod);
        }
    }

    private void OnChooseGameFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = T("Choose the Dakar Desert Rally folder") };
        if (dialog.ShowDialog(this) == true)
        {
            _vm.TrySetGameFolder(dialog.FolderName);
        }
    }

    private void OnOpenModsFolder(object sender, RoutedEventArgs e)
    {
        if (_vm.Game is { } game)
        {
            OpenFolder(game.ModsFolder);
        }
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        OpenFolder(Settings.DataFolder);
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo($"steam://rungameid/{Core.Game.GameInstall.SteamAppId}") { UseShellExecute = true });
        _vm.Report(T("Starting..."));
        switch (_vm.PlayAction.Key)
        {
            case "Minimize":
                WindowState = WindowState.Minimized;
                break;
            case "Close":
                Close();
                break;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _vm.IsIdle && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm.IsIdle && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await _vm.ImportAsync(paths.Where(p => File.Exists(p) || Directory.Exists(p)));
        }
    }

    private void OnFilter(object sender, RoutedEventArgs e) =>
        _vm.Filter = (string)((System.Windows.Controls.RadioButton)sender).CommandParameter;


    private void OnCloseCrew(object sender, RoutedEventArgs e) => _vm.Database.SelectedCrew = null;

    private void OnResetCrew(object sender, RoutedEventArgs e) => _vm.Database.SelectedCrew?.Reset();

    private void OnLivery(object sender, RoutedEventArgs e)
    {
        if (_vm.Database.SelectedCrew is { } crew)
        {
            LiveryWindow.Show(this, _vm.Database, crew);
        }
    }

    private async void OnApplyLineup(object sender, RoutedEventArgs e) => await _vm.Database.ApplyAsync();


    private void OnCloseDetails(object sender, RoutedEventArgs e) => _vm.SelectedMod = null;

    // The Paddock's tabs and filters keep their value in the RadioButton's CommandParameter.
    private static string Choice(object sender) => (string)((System.Windows.Controls.RadioButton)sender).CommandParameter;

    private void OnPaddockTab(object sender, RoutedEventArgs e) => _vm.Database.Tab = Choice(sender);

    private void OnClass(object sender, RoutedEventArgs e) => _vm.Database.Class = Choice(sender);

    private void OnEdition(object sender, RoutedEventArgs e) => _vm.Database.Edition = Choice(sender);

    private void OnPilotRole(object sender, RoutedEventArgs e) => _vm.Database.PilotRole = Choice(sender);

    private void OnClosePilot(object sender, RoutedEventArgs e) => _vm.Database.SelectedPilot = null;

    private async void OnReloadDatabase(object sender, RoutedEventArgs e) => await _vm.Database.LoadAsync(_vm.Game);

    private async void OnLocateOodle(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("Locate {0}", Core.Paks.Oodle.LibraryName),
            Filter = $"Oodle ({Core.Paks.Oodle.LibraryName})|{Core.Paks.Oodle.LibraryName}",
        };
        if (dialog.ShowDialog(this) == true)
        {
            await _vm.Database.UseOodleAsync(dialog.FileName, _vm.Game);
            _vm.RaiseOodle();
        }
    }

    private async void OnDownloadOodle(object sender, RoutedEventArgs e)
    {
        if (Confirm(T("Download {0}?", Core.Paks.Oodle.LibraryName),
                T("The game's files are compressed with Oodle, and the game does not ship this file on its own. The launcher downloads it from OodleUE on GitHub (the build FModel uses) and keeps it only if it is exactly the file that was checked."),
                T("Download")))
        {
            await _vm.Database.DownloadOodleAsync(_vm.Game);
            _vm.RaiseOodle();
        }
    }

    private async void OnClearCache(object sender, RoutedEventArgs e) => await _vm.Database.ClearCacheAsync();

    private async void OnCheckFiles(object sender, RoutedEventArgs e) => await _vm.Database.CheckFilesAsync();

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
