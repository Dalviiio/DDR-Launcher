using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using DdrLauncher.App.Mvvm;
using DdrLauncher.Core.Data;
using DdrLauncher.Core.Game;
using DdrLauncher.Core.Paks;
using static DdrLauncher.App.Localization.Translator;

namespace DdrLauncher.App.ViewModels;

/// <summary>
/// The Paddock: the game's line-up, vehicles and drivers, with the user's additions applied.
/// One edition, class and search filter applies to all three tabs.
/// </summary>
public sealed partial class DatabaseViewModel : ObservableObject
{
    private static readonly string CacheFolder = Path.Combine(Settings.DataFolder, "Cache", "Game");

    // Pictures already decoded, kept across reloads so an edit does not read them all again.
    private readonly Dictionary<string, ImageSource?> _pictures = [];

    // Filtering rebuilds the cards, so the search waits for a short pause in typing.
    private readonly DispatcherTimer _searchPause = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _pictureLoad;
    private List<VehicleItem> _vehicles = [];
    private List<PilotItem> _pilots = [];
    private ICollectionView? _vehiclesView;
    private ICollectionView? _pilotsView;
    private bool _isLoading;
    private string? _error;
    private string _tab = "Lineup";
    private string _search = "";
    private string _class = "All";
    private string _edition = "All";
    private CrewRole? _pilotRole;
    private VehicleItem? _selectedVehicle;
    private PilotItem? _selectedPilot;
    private GameInstall? _game;

    public DatabaseViewModel()
    {
        _searchPause.Tick += (_, _) =>
        {
            _searchPause.Stop();
            RefreshView();
        };
    }

    public ICollectionView? VehiclesView { get => _vehiclesView; private set => Set(ref _vehiclesView, value); }

    public ICollectionView? PilotsView { get => _pilotsView; private set => Set(ref _pilotsView, value); }

    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    public bool IsLoaded => _vehicles.Count > 0;

    /// <summary>Why the data could not be read; null when it was.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
                Raise(nameof(NeedsOodle));
            }
        }
    }

    public bool HasError => Error is not null;

    public bool NeedsOodle { get; private set; }

    public int VehicleCount => _vehicles.Count;

    public int PilotCount => _pilots.Count;

    /// <summary>The open tab: "Lineup", "Garage" or "Drivers".</summary>
    public string Tab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value))
            {
                Raise(nameof(IsLineup));
                Raise(nameof(IsGarage));
                Raise(nameof(IsDrivers));
                Raise(nameof(AddText));
                RefreshView();
            }
        }
    }

    public bool IsLineup => Tab == "Lineup";

    public bool IsGarage => Tab == "Garage";

    public bool IsDrivers => Tab == "Drivers";

    /// <summary>The Add button's label on the open tab. Vehicles are not added by hand: they come with car mods.</summary>
    public string AddText => T(IsDrivers ? "Add driver" : "Add team");

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                _searchPause.Stop();
                _searchPause.Start();
            }
        }
    }

    /// <summary>"All", "Car", "Truck", "Bike", "Quad" or "Ssv"; drivers are not filtered by class.</summary>
    public string Class { get => _class; set { if (Set(ref _class, value)) { RefreshView(); } } }

    /// <summary>"All" or an edition key such as "Dakar22".</summary>
    public string Edition { get => _edition; set { if (Set(ref _edition, value)) { RefreshView(); } } }

    /// <summary>"All", "Driver" or "CoDriver".</summary>
    public string PilotRole
    {
        set
        {
            _pilotRole = value == "All" ? null : Enum.Parse<CrewRole>(value);
            RefreshView();
        }
    }

    /// <summary>The game's editions in table order (Dakar20, Dakar21, Dakar22, Classics...), for the edition plates.</summary>
    public IReadOnlyList<EditionChip> Editions { get; private set; } = [];

    public VehicleItem? SelectedVehicle
    {
        get => _selectedVehicle;
        set { if (Set(ref _selectedVehicle, value)) { Raise(nameof(HasVehicle)); } }
    }

    public bool HasVehicle => SelectedVehicle is not null;

    public PilotItem? SelectedPilot
    {
        get => _selectedPilot;
        set { if (Set(ref _selectedPilot, value)) { Raise(nameof(HasPilot)); } }
    }

    public bool HasPilot => SelectedPilot is not null;

    /// <summary>Folders searched for the Oodle library: next to the launcher, then its data folder.</summary>
    public static string[] OodleFolders => [AppContext.BaseDirectory, Settings.DataFolder];

    public async Task LoadAsync(GameInstall? game)
    {
        _game = game ?? _game;
        await ReloadAsync();
    }

    /// <summary>True when the Oodle library is next to the launcher or in its data folder.</summary>
    public static bool HasOodle => OodleFolders.Any(f => File.Exists(Path.Combine(f, Oodle.LibraryName)));

    /// <summary>Downloads the Oodle library into the data folder (only the known file is kept), then reads the game.</summary>
    public async Task DownloadOodleAsync(GameInstall? game)
    {
        Report(T("Downloading {0}...", Oodle.LibraryName), false);
        bool done;
        try
        {
            done = await Oodle.DownloadAsync(Settings.DataFolder);
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            done = false;
        }

        if (!done)
        {
            Report(T("The download did not work. Use Locate to pick the file yourself."), true);
            return;
        }

        Report(T("{0} downloaded", Oodle.LibraryName), false);
        Raise(nameof(HasOodle));
        await LoadAsync(game);
    }

    /// <summary>Deletes the copies of the game's files and pictures the launcher keeps, then reads the game again. Liveries and profiles stay.</summary>
    public async Task ClearCacheAsync()
    {
        try
        {
            var cache = Path.Combine(Settings.DataFolder, "Cache");
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, recursive: true);
            }

            PictureCache.Clear();
            _pictures.Clear();
            Report(T("Cache cleared"), false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Report(e.Message, true);
            return;
        }

        await ReloadAsync();
    }

    /// <summary>Shows every text again in the language now in use.</summary>
    public async Task RetranslateAsync()
    {
        Raise(null);
        await ReloadAsync();
    }

    /// <summary>Copies the Oodle library the user picked into the launcher's data folder, where it is looked for.</summary>
    public async Task UseOodleAsync(string path, GameInstall? game)
    {
        if (!string.Equals(Path.GetFileName(path), Oodle.LibraryName, StringComparison.OrdinalIgnoreCase))
        {
            Error = T("Choose {0}", Oodle.LibraryName);
            return;
        }

        Directory.CreateDirectory(Settings.DataFolder);
        File.Copy(path, Path.Combine(Settings.DataFolder, Oodle.LibraryName), overwrite: true);
        await LoadAsync(game);
    }

    /// <summary>Reads the game's tables with the user's additions applied (crew edits stay on the cards) and shows them.</summary>
    private async Task ReloadAsync()
    {
        if (_game is not { } game || IsLoading)
        {
            return;
        }

        IsLoading = true;
        NeedsOodle = false;
        Error = null;
        try
        {
            var additions = _store.Plan with { Changes = [], Paints = [] };
            Show(await Task.Run(() => GameData.Load(game, CacheFolder, additions, OodleFolders)));
            _ = LoadPicturesAsync(game);
        }
        catch (Exception e)
        {
            // Any failure reading the game stays on this page instead of closing the launcher.
            NeedsOodle = e is OodleMissingException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
            Error = NeedsOodle ? T("{0} missing", Oodle.LibraryName) : e.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Show(GameData data)
    {
        var teams = data.Teams.Where(t => !t.IsPlaceholder).ToList();
        var pilotNames = data.Pilots.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().Name);
        var byVehicle = teams.ToLookup(t => t.VehicleId);
        var byPilot = teams.SelectMany(t => t.Members.Where(m => m.Role != CrewRole.Other).Select(m => (Team: t, Member: m))).ToLookup(x => x.Member.PilotId);
        string Crew(Team t) => string.Join(" / ", t.Members.Where(m => m.Role != CrewRole.Other).Select(m => pilotNames.GetValueOrDefault(m.PilotId, m.PilotId)));

        _vehicles = data.Vehicles
            .Select(v => new VehicleItem(v, byVehicle[v.Id].Select(t => new TeamLine(t, Crew(t))).ToList(), _store.Plan.Vehicles.Any(n => n.Id == v.Id)))
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var vehicleNames = _vehicles.GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First().DisplayName);
        _pilots = data.Pilots
            .Select(p => new PilotItem(p, byPilot[p.Id].Select(x => new TeamLine(x.Team, $"{RoleText(x.Member.Role)} · {vehicleNames.GetValueOrDefault(x.Team.VehicleId, x.Team.VehicleId)}", x.Member.Role)).ToList(),
                _store.Plan.Pilots.Any(n => n.Id == p.Id)))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _countries = data.Countries.OrderBy(c => c.Value, StringComparer.OrdinalIgnoreCase).ToList();

        // Editions stay the same object once shown, so the chosen plate keeps its state when the lists reload.
        Editions = Editions.Count > 0 ? Editions : [new EditionChip("All"), .. teams.Select(t => t.Edition).Distinct().Select(e => new EditionChip(e))];

        VehiclesView = new ListCollectionView(_vehicles)
        {
            Filter = o => o is VehicleItem v && (Class == "All" || v.Class == Class)
                && (Edition == "All" || v.Teams.Any(t => t.Team.Edition == Edition)) && Matches(Search, v.Name, v.Brand, v.Id),
        };
        PilotsView = new ListCollectionView(_pilots)
        {
            Filter = o => o is PilotItem p && (_pilotRole is null && Edition == "All"
                    || p.Teams.Any(t => (_pilotRole is null || t.Role == _pilotRole) && (Edition == "All" || t.Team.Edition == Edition)))
                && Matches(Search, p.Name, p.Country, p.Id),
        };
        SelectedVehicle = _vehicles.FirstOrDefault(v => v.Model.Picture != "") ?? _vehicles.FirstOrDefault();
        SelectedPilot = null;
        ShowLineup(teams);
        foreach (var name in new[] { nameof(IsLoaded), nameof(VehicleCount), nameof(PilotCount), nameof(Editions) })
        {
            Raise(name);
        }
    }

    /// <summary>Applies the filters to the open tab only; the others refresh when they open.</summary>
    private void RefreshView()
    {
        switch (Tab)
        {
            case "Garage":
                VehiclesView?.Refresh();

                // The garage always shows a vehicle when one matches.
                if (VehiclesView is { } view && (SelectedVehicle is null || !view.Contains(SelectedVehicle)))
                {
                    SelectedVehicle = view.Cast<VehicleItem>().FirstOrDefault();
                }

                break;
            case "Drivers":
                PilotsView?.Refresh();
                break;
            default:
                CrewsView?.Refresh();
                break;
        }
    }

    /// <summary>
    /// Shows the pictures: from memory, from the PNG cache, or decoded from the game (a few seconds, the first time only).
    /// A newer load cancels the one still running.
    /// </summary>
    private async Task LoadPicturesAsync(GameInstall game)
    {
        _pictureLoad?.Cancel();
        var load = _pictureLoad = new CancellationTokenSource();

        var targets = _crews.SelectMany(c => new (string Asset, Action<ImageSource?> Set)[] { (c.Team.Picture, p => c.TeamPicture = p), (c.Team.CrewPhoto, p => c.CrewPhoto = p) })
            .Concat(_vehicles.Select(v => (v.Model.Picture, (Action<ImageSource?>)(p => v.Picture = p))))
            .Concat(_vehicles.SelectMany(v => v.Teams).Concat(_pilots.SelectMany(p => p.Teams)).Select(l => (l.Team.Picture, (Action<ImageSource?>)(p => l.Picture = p))))
            .Concat(_pilots.Select(p => (p.PhotoAsset ?? "", (Action<ImageSource?>)(photo => p.Photo = photo))))
            .Where(t => t.Item1 != "")
            .ToLookup(t => t.Item1, t => t.Item2);

        var dispatcher = Application.Current.Dispatcher;
        void Show(string asset, ImageSource? picture) => dispatcher.InvokeAsync(() =>
        {
            _pictures[asset] = picture;
            foreach (var set in targets[asset])
            {
                set(picture);
            }
        });

        foreach (var group in targets.Where(g => _pictures.ContainsKey(g.Key)))
        {
            foreach (var set in group)
            {
                set(_pictures[group.Key]);
            }
        }

        var needed = targets.Select(g => g.Key).Where(a => !_pictures.ContainsKey(a)).ToList();
        if (needed.Count == 0)
        {
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                var unknown = new List<string>();
                foreach (var asset in needed)
                {
                    load.Token.ThrowIfCancellationRequested();
                    if (PictureCache.Load(asset) is { } cached)
                    {
                        Show(asset, cached);
                    }
                    else if (!PictureCache.Failed(asset))
                    {
                        unknown.Add(asset);
                    }
                }

                if (unknown.Count > 0)
                {
                    var failed = GamePictures.Extract(game, unknown, OodleFolders, (asset, image) => Show(asset, PictureCache.Save(asset, image)), load.Token);
                    PictureCache.MarkFailed(failed);
                }
            }, load.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer load took over.
        }
        catch (Exception e)
        {
            // Pictures are a comfort: the lists work without them.
            Report(T("Pictures not loaded: {0}", e.Message), true);
        }
    }

    /// <summary>"Dakar22" -> "Dakar 22"; other editions keep their name.</summary>
    internal static string EditionName(string edition) => DakarEdition().Replace(edition, "Dakar $1");

    internal static string RoleText(CrewRole role) => T(role == CrewRole.CoDriver ? "Co-driver" : role.ToString());

    private static bool Matches(string search, params string[] fields) =>
        string.IsNullOrWhiteSpace(search) || fields.Any(f => f.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^Dakar(\d+)$")]
    internal static partial Regex DakarEdition();
}

/// <summary>One team a vehicle or driver appears in: "#204 BAHRAIN RAID XTREME" plus a detail line, with the team's picture.</summary>
public sealed class TeamLine(Team team, string detail, CrewRole? role = null) : ObservableObject
{
    private ImageSource? _picture;

    public Team Team { get; } = team;

    public string Detail { get; } = detail;

    public CrewRole? Role { get; } = role;

    public string EditionText => DatabaseViewModel.EditionName(Team.Edition).ToUpperInvariant();

    public string Title => $"#{Team.Number} {Team.Name}";

    public ImageSource? Picture
    {
        get => _picture;
        set => Set(ref _picture, value);
    }
}

/// <summary>An edition plate: "DAKAR" over "22", or "EDITION" over "Classics". The key "All" is the plate for every edition.</summary>
public sealed record EditionChip(string Key)
{
    public bool IsDefault => Key == "All";

    public string Caption => T(IsDefault ? "EDITIONS" : DatabaseViewModel.DakarEdition().IsMatch(Key) ? "DAKAR" : "EDITION");

    public string Label => IsDefault ? T("ALL") : DatabaseViewModel.DakarEdition().Match(Key) is { Success: true } m ? m.Groups[1].Value : Key;

    /// <summary>"Dakar 22", "Classics".</summary>
    public string Name => DatabaseViewModel.EditionName(Key);
}

public sealed record Stat(string Label, int Value)
{
    public double Fraction => Math.Clamp(Value / 100.0, 0, 1);
}

public sealed class VehicleItem(Vehicle model, IReadOnlyList<TeamLine> teams, bool isAdded) : ObservableObject
{
    private ImageSource? _picture;

    public Vehicle Model { get; } = model;

    /// <summary>A vehicle the user added: it can be deleted.</summary>
    public bool IsAdded { get; } = isAdded;

    public ImageSource? Picture
    {
        get => _picture;
        set => Set(ref _picture, value);
    }

    public string Id => Model.Id;

    public string Name => Model.Name;

    /// <summary>"GR DKR HILUX V8 (2022)": the name with the years (or editions) it races in, so same-named vehicles can be told apart.</summary>
    public string DisplayName { get; } = Years(model.Name, teams);

    public string Brand => Model.Brand;

    public string Class => Model.Class;

    public string ClassText => T(Model.Class == "Ssv" ? "SSV" : Model.Class);

    public string PriceText => Model.Price.ToString("N0", CultureInfo.InvariantCulture);

    public string MaxSpeedText => Model.MaxSpeed.ToString("0", CultureInfo.InvariantCulture);

    public IReadOnlyList<Stat> Stats { get; } =
        [new(T("Speed"), model.Speed), new(T("Acceleration"), model.Acceleration), new(T("Handling"), model.Handling), new(T("Strength"), model.Strength)];

    public IReadOnlyList<TeamLine> Teams { get; } = teams;

    // Dakar editions give a year (Dakar22 -> 2022); others (Classics, Snowrunner) give their name.
    private static string Years(string name, IReadOnlyList<TeamLine> teams)
    {
        var editions = teams.Select(t => t.Team.Edition).Distinct().ToList();
        var years = editions.Select(e => DatabaseViewModel.DakarEdition().Match(e)).Where(m => m.Success).Select(m => 2000 + int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Order().ToList();

        // Years in a row read as a span: 2020, 2021, 2022 -> 2020-2022.
        var spans = new List<string>();
        for (var i = 0; i < years.Count;)
        {
            var end = i;
            while (end + 1 < years.Count && years[end + 1] == years[end] + 1)
            {
                end++;
            }

            spans.Add(end > i ? $"{years[i]}-{years[end]}" : $"{years[i]}");
            i = end + 1;
        }

        var labels = spans.Concat(editions.Where(e => !DatabaseViewModel.DakarEdition().IsMatch(e)).Order()).ToList();
        return labels.Count == 0 ? name : $"{name} ({string.Join(", ", labels)})";
    }
}

public sealed class PilotItem(Pilot model, IReadOnlyList<TeamLine> teams, bool isAdded) : ObservableObject
{
    // The crew photo of the first team that has one: the driver stands on the left, the co-driver on the right.
    private readonly TeamLine? _photoLine = teams.FirstOrDefault(t => t.Team.CrewPhoto != "");
    private ImageSource? _photo;

    public Pilot Model { get; } = model;

    /// <summary>A driver the user added: they can be deleted.</summary>
    public bool IsAdded { get; } = isAdded;

    public string? PhotoAsset => _photoLine?.Team.CrewPhoto;

    /// <summary>The part of the crew photo with this driver's face, as a fraction of the photo.</summary>
    public Rect PhotoViewbox => _photoLine?.Role == CrewRole.CoDriver ? new Rect(0.44, 0, 0.4, 0.6) : new Rect(0.04, 0, 0.4, 0.6);

    public ImageSource? Photo
    {
        get => _photo;
        set => Set(ref _photo, value);
    }

    public string Id => Model.Id;

    public string Name => Model.Name;

    public string Country => Model.Country;

    public IReadOnlyList<TeamLine> Teams { get; } = teams;

    /// <summary>"Spain · Co-driver", or just the country when the driver is in no team.</summary>
    public string Subtitle { get; } = string.Join(" · ", [model.Country, .. teams.Select(t => t.Role).OfType<CrewRole>().Distinct().Order().Select(DatabaseViewModel.RoleText)]);

    public string TeamsText => Count(Teams.Count, "{0} team", "{0} teams");
}
