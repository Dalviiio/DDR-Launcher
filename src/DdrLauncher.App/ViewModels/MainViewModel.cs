using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using DdrLauncher.App.Mvvm;
using DdrLauncher.Core.Game;
using DdrLauncher.Core.Mods;
using DdrLauncher.Core.Safety;

namespace DdrLauncher.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly Settings _settings = Settings.Load();
    private readonly ModLibrary _library;
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private GameInstall? _game;
    private bool _isBusy;
    private string? _message;
    private bool _messageIsError;

    public MainViewModel()
    {
        _library = new ModLibrary(Path.Combine(Settings.DataFolder, "Library"), GameInstall.IsGameRunning);
        foreach (var mod in _library.Mods)
        {
            Mods.Add(new ModItemViewModel(mod, this));
        }

        _game = GameInstall.IsValid(_settings.GameFolder) ? new GameInstall(_settings.GameFolder!) : GameInstall.FindOnSteam();
        if (_game is not null && _settings.GameFolder != _game.Folder)
        {
            _settings.GameFolder = _game.Folder;
            _settings.Save();
        }

        _messageTimer.Tick += (_, _) => Message = null;
        Mods.CollectionChanged += (_, _) => RaiseCounts();
        DismissMessageCommand = new RelayCommand(() => Message = null);
    }

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];

    public ICommand DismissMessageCommand { get; }

    public bool HasGame => _game is not null;

    public string GameFolderText => _game?.Folder ?? "Not found";

    public bool HasMods => Mods.Count > 0;

    public string Summary => Mods.Count == 0
        ? "Add a mod to get started"
        : $"{Mods.Count} {(Mods.Count == 1 ? "mod" : "mods")}, {Mods.Count(m => m.IsEnabled)} enabled";

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public string? Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
            {
                Raise(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => Message is not null;

    public bool MessageIsError
    {
        get => _messageIsError;
        private set => Set(ref _messageIsError, value);
    }

    public bool TrySetGameFolder(string folder)
    {
        if (!GameInstall.IsValid(folder))
        {
            ShowMessage("This folder is not Dakar Desert Rally. Pick the folder that contains Dakar2Game.", error: true);
            return false;
        }

        _game = new GameInstall(folder);
        _settings.GameFolder = _game.Folder;
        _settings.Save();
        Raise(nameof(HasGame));
        Raise(nameof(GameFolderText));
        ShowMessage("Game folder saved.");
        return true;
    }

    public async Task ImportAsync(IEnumerable<string> sources)
    {
        var added = 0;
        var errors = new List<string>();
        await RunBusy(async () =>
        {
            foreach (var source in sources)
            {
                try
                {
                    var mod = await Task.Run(() => _library.Import(source));
                    Mods.Add(new ModItemViewModel(mod, this));
                    added++;
                }
                catch (Exception e) when (e is ImportRejectedException or IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetFileName(source)}: {Reason(e)}");
                }
            }
        });

        if (errors.Count > 0)
        {
            ShowMessage($"Could not add {string.Join("; ", errors)}", error: true);
        }
        else if (added > 0)
        {
            ShowMessage(added == 1 ? "Mod added. Switch it on to use it in the game." : $"{added} mods added. Switch them on to use them in the game.");
        }
    }

    /// <summary>Returns the state the mod really ends up in.</summary>
    internal async Task<bool> SetEnabledAsync(ModItemViewModel item, bool enabled)
    {
        if (_game is null)
        {
            ShowMessage("Choose the game folder first.", error: true);
            return item.Model.Enabled;
        }

        var game = _game;
        IReadOnlyList<string> kept = [];
        await RunBusy(async () =>
        {
            try
            {
                if (enabled)
                {
                    await Task.Run(() => _library.Enable(item.Model, game.ModsFolder));
                }
                else
                {
                    kept = await Task.Run(() => _library.Disable(item.Model, game.ModsFolder));
                }
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                ShowMessage(Reason(e), error: true);
            }
        });

        if (kept.Count > 0)
        {
            ShowMessage($"Left in the game because they changed since they were placed: {string.Join(", ", kept)}", error: true);
        }

        RaiseCounts();
        return item.Model.Enabled;
    }

    internal async Task RemoveAsync(ModItemViewModel item)
    {
        IReadOnlyList<string> kept = [];
        var removed = false;
        var game = _game;
        await RunBusy(async () =>
        {
            try
            {
                kept = await Task.Run(() => _library.Remove(item.Model, game?.ModsFolder));
                removed = true;
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                ShowMessage(Reason(e), error: true);
            }
        });

        if (!removed)
        {
            return;
        }

        Mods.Remove(item);
        ShowMessage(kept.Count > 0
            ? $"'{item.Name}' removed. Left in the game because they changed: {string.Join(", ", kept)}"
            : $"'{item.Name}' removed.", error: kept.Count > 0);
    }

    private async Task RunBusy(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowMessage(string text, bool error = false)
    {
        MessageIsError = error;
        Message = text;
        _messageTimer.Stop();
        if (!error)
        {
            _messageTimer.Start();
        }
    }

    private void RaiseCounts()
    {
        Raise(nameof(HasMods));
        Raise(nameof(Summary));
    }

    private static string Reason(Exception e) => e is ImportRejectedException r ? r.Reason : e.Message;
}
