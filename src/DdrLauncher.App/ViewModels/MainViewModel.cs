using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using DdrLauncher.App.Localization;
using DdrLauncher.App.Mvvm;
using DdrLauncher.Core;
using DdrLauncher.Core.Game;
using DdrLauncher.Core.Mods;
using DdrLauncher.Core.Safety;
using static DdrLauncher.App.Localization.Translator;

namespace DdrLauncher.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly Settings _settings = Settings.Load();
    private readonly ModLibrary _library;
    private GameInstall? _game;
    private ModItemViewModel? _selectedMod;
    private bool _isBusy;
    private string _status = T("Ready");
    private bool _statusIsError;
    private string _filter = "All";
    private AvailableUpdate? _update;

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

    public string GameFolderText => _game?.Folder ?? T("Not found");

    public string DataFolder => Settings.DataFolder;

    public string VersionText { get; } = "v" + Version;

    public IReadOnlyList<Language> Languages => Translator.Languages;

    /// <summary>The launcher's language; every text on screen follows at once.</summary>
    public Language Language
    {
        get => Translator.Instance.Current;
        set
        {
            if (value is null || value == Translator.Instance.Current)
            {
                return;
            }

            Translator.Instance.Use(value);
            _settings.Language = value.Code;
            _settings.Save();
            _status = T("Ready");
            Raise(null);
            _ = Database.RetranslateAsync();
        }
    }

    /// <summary>What Play does with the launcher once the game starts.</summary>
    public IReadOnlyList<Choice> PlayActions =>
        [new("Stay", T("Keep the launcher open")), new("Minimize", T("Minimize the launcher")), new("Close", T("Close the launcher"))];

    public Choice PlayAction
    {
        get => PlayActions.FirstOrDefault(a => a.Key == _settings.PlayAction) ?? PlayActions[0];
        set
        {
            if (value is not null && value.Key != _settings.PlayAction)
            {
                _settings.PlayAction = value.Key;
                _settings.Save();
            }
        }
    }

    /// <summary>The version this launcher is: "0.4.0".</summary>
    public static string Version => typeof(MainViewModel).Assembly.GetName().Version!.ToString(3);

    /// <summary>Settings: look for a new version at each start.</summary>
    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates == true;
        set
        {
            _settings.CheckForUpdates = value;
            _settings.Save();
            Raise();
        }
    }

    /// <summary>True until the user said whether to look for new versions.</summary>
    public bool UpdatesNotAsked => _settings.CheckForUpdates is null;

    /// <summary>A newer version found on GitHub; null when there is none or it was not looked for.</summary>
    public AvailableUpdate? Update
    {
        get => _update;
        set
        {
            if (Set(ref _update, value))
            {
                Raise(nameof(HasUpdate));
                Raise(nameof(UpdateText));
            }
        }
    }

    public bool HasUpdate => Update is not null;

    public string UpdateText => Update is { } update ? T("Update to {0}", update.Version) : "";

    public UpdateService Updates { get; } =
        new(App.ReleaseRepository, App.UpdatePublicKey, Path.Combine(Settings.DataFolder, "Updates"));

    public bool OodleFound => DatabaseViewModel.HasOodle;

    public string OodleText => T(OodleFound ? "Found" : "Missing");

    public void RaiseOodle()
    {
        Raise(nameof(OodleFound));
        Raise(nameof(OodleText));
    }

    /// <summary>The game's cover from Steam's own cache, shown in the game bar; nothing is copied.</summary>
    public string? CoverImage { get; } = SteamImage("library_600x900.jpg");

    /// <summary>The game's wide artwork from Steam's cache, behind the page title.</summary>
    public string? HeroImage { get; } = SteamImage("library_hero.jpg");

    public string Summary => Mods.Count == 0 ? T("Empty") : Count(Mods.Count, "{0} mod", "{0} mods");

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
            Report(T("Not a Dakar Desert Rally folder"), error: true);
            return false;
        }

        _game = new GameInstall(folder);
        _settings.GameFolder = _game.Folder;
        _settings.Save();
        Raise(nameof(HasGame));
        Raise(nameof(GameFolderText));
        Report(T("Game folder set"));
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
            Report(T("Not added: {0}", string.Join("; ", errors)), error: true);
        }
        else if (added.Count > 0)
        {
            Report(added.Count == 1 ? T("{0} added", added[0].Name) : T("{0} mods added", added.Count));
        }
    }

    internal async Task SetEnabledAsync(ModItemViewModel item, bool enabled)
    {
        if (_game is null)
        {
            Report(T("Set the game folder"), error: true);
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
            Report(T("Kept because they changed since they were added: {0}", string.Join(", ", kept)), error: true);
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
            ? T("{0} removed. Kept because they changed since they were added: {1}", item.Name, string.Join(", ", kept))
            : $"{item.Name} removed", error: kept.Count > 0);
    }

    public void Report(string text, bool error = false)
    {
        if (error)
        {
            Log.Warn(text);
        }

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

/// <summary>An option in a settings list: its stored key and its name on screen.</summary>
public sealed record Choice(string Key, string Name)
{
    public override string ToString() => Name;
}
