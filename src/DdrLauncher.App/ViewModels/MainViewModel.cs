using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using DdrLauncher.App.Mvvm;
using DdrLauncher.Core.Game;
using DdrLauncher.Core.Mods;
using DdrLauncher.Core.Safety;

namespace DdrLauncher.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly Settings _settings = Settings.Load();
    private readonly ModLibrary _library;
    private GameInstall? _game;
    private ModItemViewModel? _selectedMod;
    private bool _isBusy;
    private string _status = "Ready";
    private bool _statusIsError;
    private string _filter = "All";

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

        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = o => o is ModItemViewModel m && (_filter == "All" || m.IsEnabled == (_filter == "On"));
        Mods.CollectionChanged += (_, _) => RaiseCounts();
        RaiseCounts();
        Database.Report = Report;
        _ = Database.LoadAsync(_game);
    }

    /// <summary>The game's vehicles and drivers.</summary>
    public DatabaseViewModel Database { get; } = new();

    /// <summary>The mod cards shown, after the All / On / Off filter.</summary>
    public ICollectionView ModsView { get; }

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
            {
                ModsView.Refresh();
            }
        }
    }

    public int OnCount => Mods.Count(m => m.IsEnabled);

    public int OffCount => Mods.Count - OnCount;

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];

    public GameInstall? Game => _game;

    public bool HasGame => _game is not null;

    public string GameFolderText => _game?.Folder ?? "Not found";

    public string DataFolder => Settings.DataFolder;

    public string VersionText { get; } = "v" + typeof(MainViewModel).Assembly.GetName().Version!.ToString(3);

    /// <summary>The game's cover from Steam's own cache, shown in the game bar; nothing is copied.</summary>
    public string? CoverImage { get; } = SteamImage("library_600x900.jpg");

    /// <summary>The game's wide artwork from Steam's cache, behind the page title.</summary>
    public string? HeroImage { get; } = SteamImage("library_hero.jpg");

    public string Summary => Mods.Count == 0 ? "Empty" : DatabaseViewModel.Count(Mods.Count, "mod");

    public bool HasMods => Mods.Count > 0;

    public ModItemViewModel? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (Set(ref _selectedMod, value))
            {
                Raise(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => SelectedMod is not null;

    public bool IsIdle => !_isBusy;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => Set(ref _statusIsError, value);
    }

    public bool TrySetGameFolder(string folder)
    {
        if (!GameInstall.IsValid(folder))
        {
            Report("Not a Dakar Desert Rally folder", error: true);
            return false;
        }

        _game = new GameInstall(folder);
        _settings.GameFolder = _game.Folder;
        _settings.Save();
        Raise(nameof(HasGame));
        Raise(nameof(GameFolderText));
        Report("Game folder set");
        _ = Database.LoadAsync(_game);
        return true;
    }

    public async Task ImportAsync(IEnumerable<string> sources)
    {
        var added = new List<ModItemViewModel>();
        var errors = new List<string>();
        await RunBusy(async () =>
        {
            foreach (var source in sources)
            {
                try
                {
                    var mod = await Task.Run(() => _library.Import(source));
                    var item = new ModItemViewModel(mod, this);
                    Mods.Add(item);
                    added.Add(item);
                }
                catch (Exception e) when (e is ImportRejectedException or IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetFileName(Path.TrimEndingDirectorySeparator(source))}: {Reason(e)}");
                }
            }
        });

        if (added.Count > 0)
        {
            SelectedMod = added[^1];
        }

        if (errors.Count > 0)
        {
            Report($"Not added: {string.Join("; ", errors)}", error: true);
        }
        else if (added.Count > 0)
        {
            Report(added.Count == 1 ? $"{added[0].Name} added" : $"{added.Count} mods added");
        }
    }

    internal async Task SetEnabledAsync(ModItemViewModel item, bool enabled)
    {
        if (_game is null)
        {
            Report("Set the game folder", error: true);
            return;
        }

        var modsFolder = _game.ModsFolder;
        IReadOnlyList<string> kept = [];
        var failed = false;
        await RunBusy(async () =>
        {
            try
            {
                if (enabled)
                {
                    await Task.Run(() => _library.Enable(item.Model, modsFolder));
                }
                else
                {
                    kept = await Task.Run(() => _library.Disable(item.Model, modsFolder));
                }
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                failed = true;
                Report(Reason(e), error: true);
            }
        });

        if (kept.Count > 0)
        {
            Report($"Kept because they changed since they were added: {string.Join(", ", kept)}", error: true);
        }
        else if (!failed)
        {
            Report(enabled ? $"{item.Name} on" : $"{item.Name} off");
        }

        RaiseCounts();
    }

    internal async Task RemoveAsync(ModItemViewModel item)
    {
        IReadOnlyList<string> kept = [];
        var removed = false;
        var modsFolder = _game?.ModsFolder;
        await RunBusy(async () =>
        {
            try
            {
                kept = await Task.Run(() => _library.Remove(item.Model, modsFolder));
                removed = true;
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Report(Reason(e), error: true);
            }
        });

        if (!removed)
        {
            return;
        }

        Mods.Remove(item);
        Report(kept.Count > 0
            ? $"{item.Name} removed. Kept because they changed since they were added: {string.Join(", ", kept)}"
            : $"{item.Name} removed", error: kept.Count > 0);
    }

    public void Report(string text, bool error = false)
    {
        StatusIsError = error;
        Status = text;
    }

    private async Task RunBusy(Func<Task> action)
    {
        _isBusy = true;
        Raise(nameof(IsIdle));
        try
        {
            await action();
        }
        finally
        {
            _isBusy = false;
            Raise(nameof(IsIdle));
        }
    }

    private void RaiseCounts()
    {
        Raise(nameof(HasMods));
        Raise(nameof(Summary));
        Raise(nameof(OnCount));
        Raise(nameof(OffCount));
        for (var i = 0; i < Mods.Count; i++)
        {
            Mods[i].Number = i + 1;
        }

        ModsView?.Refresh();
    }

    private static string? SteamImage(string fileName)
    {
        var steam = GameInstall.SteamPath();
        var image = steam is null ? null : Path.Combine(steam, "appcache", "librarycache", GameInstall.SteamAppId, fileName);
        return File.Exists(image) ? image : null;
    }

    private static string Reason(Exception e) => e is ImportRejectedException r ? r.Reason : e.Message;
}
