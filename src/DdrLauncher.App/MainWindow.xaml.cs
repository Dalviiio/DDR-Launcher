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
        RoundedCorners.Apply(this, 14);
        StateChanged += (_, _) =>
        {
            MaximizeButton.Tag = FindResource(WindowState == WindowState.Maximized ? "Icon.RestoreWindow" : "Icon.Maximize");
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";

            // A maximized borderless window overflows the screen by its resize border.
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        };
    }

    private async void OnAddMods(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add mods",
            Filter = "Mods (*.pak, *.zip, *.rar, *.7z)|*.pak;*.zip;*.rar;*.7z",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await _vm.ImportAsync(dialog.FileNames);
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

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        var accept = !_vm.IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = accept ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!_vm.IsBusy && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await _vm.ImportAsync(paths.Where(p => File.Exists(p) || Directory.Exists(p)));
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
