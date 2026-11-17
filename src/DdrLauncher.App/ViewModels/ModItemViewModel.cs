using System.Globalization;
using System.Windows.Input;
using DdrLauncher.App.Mvvm;
using DdrLauncher.Core.Mods;

namespace DdrLauncher.App.ViewModels;

public sealed class ModItemViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _isConfirmingRemoval;

    public ModItemViewModel(ModEntry model, MainViewModel owner)
    {
        Model = model;
        _owner = owner;
        AskRemoveCommand = new RelayCommand(() => IsConfirmingRemoval = true);
        CancelRemoveCommand = new RelayCommand(() => IsConfirmingRemoval = false);
        RemoveCommand = new AsyncRelayCommand(() => _owner.RemoveAsync(this));
    }

    public ModEntry Model { get; }

    public string Name => Model.Name;

    public string Details
    {
        get
        {
            var files = Model.Files.Count == 1 ? "1 file" : $"{Model.Files.Count} files";
            return string.Create(CultureInfo.InvariantCulture, $"{files} · {FormatSize(Model.TotalSize)} · added {Model.AddedUtc.ToLocalTime():d MMM yyyy}");
        }
    }

    public string FileList => string.Join(Environment.NewLine, Model.Files.Select(f => f.Name));

    /// <summary>Bound to the switch: flipping it enables or disables the mod in the game.</summary>
    public bool IsEnabled
    {
        get => Model.Enabled;
        set
        {
            if (value != Model.Enabled)
            {
                _ = ApplyAsync(value);
            }
        }
    }

    public string StateText => Model.Enabled ? "On" : "Off";

    public bool IsConfirmingRemoval
    {
        get => _isConfirmingRemoval;
        set => Set(ref _isConfirmingRemoval, value);
    }

    public ICommand AskRemoveCommand { get; }

    public ICommand CancelRemoveCommand { get; }

    public ICommand RemoveCommand { get; }

    private async Task ApplyAsync(bool value)
    {
        await _owner.SetEnabledAsync(this, value);

        // Puts the switch back if the change was refused.
        Raise(nameof(IsEnabled));
        Raise(nameof(StateText));
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0 GB", CultureInfo.InvariantCulture),
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0 MB", CultureInfo.InvariantCulture),
        >= 1L << 10 => (bytes / 1024.0).ToString("0 KB", CultureInfo.InvariantCulture),
        _ => bytes + " B",
    };
}
