using System.Diagnostics;
using System.IO;
using System.Windows;
using DdrLauncher.App.Controls;
using DdrLauncher.App.ViewModels;
using Microsoft.Win32;

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
            MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";

            // A maximized borderless window overflows the screen by its resize border.
            Root.Margin = maximized ? new Thickness(7) : new Thickness(0);
            Root.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(24);
        };
    }

    private async void OnImportFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import mods",
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
        var dialog = new OpenFolderDialog { Title = "Import a mod folder" };
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

        if (Confirm("Remove this mod?", $"'{mod.Name}' will be taken out of the game and deleted from your library.", "Remove"))
        {
            await _vm.RemoveAsync(mod);
        }
    }

    private void OnChooseGameFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the Dakar Desert Rally folder" };
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
        _vm.Report("Starting...");
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
        _vm.Filter = (string)((System.Windows.Controls.RadioButton)sender).Content;


    private void OnCloseCrew(object sender, RoutedEventArgs e) => _vm.Database.SelectedCrew = null;

    private void OnResetCrew(object sender, RoutedEventArgs e) => _vm.Database.SelectedCrew?.Reset();

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
            Title = $"Locate {Core.Paks.Oodle.LibraryName}",
            Filter = $"Oodle ({Core.Paks.Oodle.LibraryName})|{Core.Paks.Oodle.LibraryName}",
        };
        if (dialog.ShowDialog(this) == true)
        {
            await _vm.Database.UseOodleAsync(dialog.FileName, _vm.Game);
        }
    }

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
