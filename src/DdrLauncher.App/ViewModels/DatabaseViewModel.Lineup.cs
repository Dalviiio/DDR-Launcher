using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media;
using DdrLauncher.App.Mvvm;
using DdrLauncher.Core.Data;
using DdrLauncher.Core.Game;
using DdrLauncher.Core.Paks;
using static DdrLauncher.App.Localization.Translator;

namespace DdrLauncher.App.ViewModels;

/// <summary>
/// Line-up and additions, kept in profiles: crews to edit, new vehicles, drivers and teams.
/// Default is the game as it ships and cannot be edited; Apply puts the profile in use in the game as one mod pak.
/// </summary>
public sealed partial class DatabaseViewModel
{
    private static readonly string BuildFolder = Path.Combine(Settings.DataFolder, "Cache", "Build");

    /// <summary>The game textures that liveries start from, copied out of the paks.</summary>
    internal static readonly string TextureFolder = Path.Combine(Settings.DataFolder, "Cache", "Textures");

    /// <summary>The painted livery pictures, named by their SHA-256.</summary>
    internal static readonly string LiveryFolder = Path.Combine(Settings.DataFolder, "Liveries");

    private readonly LineupStore _store = LineupStore.Load();
    private List<CrewItem> _crews = [];
    private Dictionary<(string Table, string Row), CrewItem> _crewIndex = [];
    private IReadOnlyList<KeyValuePair<string, string>> _countries = [];
    private ICollectionView? _crewsView;
    private CrewItem? _selectedCrew;
    private bool _isApplying;

    /// <summary>Shows a status line in the game bar; true marks an error.</summary>
    public Action<string, bool> Report { get; set; } = (_, _) => { };

    public ICollectionView? CrewsView { get => _crewsView; private set => Set(ref _crewsView, value); }

    public int CrewCount => _crews.Count;

    public CrewItem? SelectedCrew
    {
        get => _selectedCrew;
        set { if (Set(ref _selectedCrew, value)) { Raise(nameof(HasCrew)); } }
    }

    public bool HasCrew => SelectedCrew is not null;

    /// <summary>All drivers, for the driver and co-driver pickers.</summary>
    public IReadOnlyList<PilotItem> PilotOptions => _pilots;

    public IReadOnlyList<VehicleItem> VehicleOptions => _vehicles;

    /// <summary>The game's countries by display name, for the driver form.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Countries => _countries;

    /// <summary>Default first, then the user's profiles.</summary>
    public IReadOnlyList<ProfileItem> Profiles => [new ProfileItem(null, T("Default")), .. _store.Profiles.Select(p => new ProfileItem(p.Id, p.Name))];

    /// <summary>The profile in use; choosing another one shows its line-up (Apply puts it in the game).</summary>
    public ProfileItem ActiveProfile
    {
        get => Profiles.First(p => p.Id == _store.ActiveProfile);
        set
        {
            if (value is not null && value.Id != _store.ActiveProfile)
            {
                _store.ActiveProfile = value.Id;
                RaiseProfiles();
                _ = SavedAsync();
            }
        }
    }

    /// <summary>Default shows the game as it ships: changes need a profile.</summary>
    public bool CanEdit => _store.Active is not null;

    /// <summary>Edited crews plus added vehicles, drivers and teams in the profile in use.</summary>
    public int EditedCount => _store.Plan is var plan ? plan.Changes.Count + plan.Pilots.Count + plan.Teams.Count + plan.Paints.Count : 0;

    /// <summary>"· 3 changes" after the status; nothing on Default, which has none.</summary>
    public string ChangesText => CanEdit ? "· " + Count(EditedCount, "{0} change", "{0} changes") : "";

    /// <summary>True while the launcher's pak is in the game.</summary>
    public bool HasEdits => _store.PlacedSha256 is not null;

    /// <summary>True when the profile in use differs from what is in the game.</summary>
    public bool IsPending { get; private set; }

    public string PendingText => (IsPending, CanEdit) switch
    {
        (true, true) => T("Not in the game yet"),
        (true, false) => T("Apply to put the original game back"),
        (false, true) => T("In the game"),
        _ => T("Original game"),
    };

    public bool IsApplying { get => _isApplying; private set { if (Set(ref _isApplying, value)) { Raise(nameof(CanApply)); } } }

    public bool CanApply => IsPending && !IsApplying;

    private IReadOnlyList<CheckLine> _checkResults = [];
    private bool _isChecking;

    /// <summary>The last file check, problems first.</summary>
    public IReadOnlyList<CheckLine> CheckResults { get => _checkResults; private set => Set(ref _checkResults, value); }

    public bool IsChecking { get => _isChecking; private set { if (Set(ref _isChecking, value)) { Raise(nameof(CanCheck)); } } }

    public bool CanCheck => !IsChecking;

    /// <summary>Reads the game's paks, the mods in ~mods and the launcher's pak, and lists what it finds. Changes nothing.</summary>
    public async Task CheckFilesAsync()
    {
        if (_game is not { } game || IsChecking)
        {
            return;
        }

        IsChecking = true;
        try
        {
            var placed = _store.PlacedSha256;
            CheckResults = (await Task.Run(() => FileCheck.Run(game, OodleFolders, placed)))
                .Select(r => new CheckLine(r.Level, T(r.Title), T(r.Text, r.Values)))
                .OrderByDescending(r => r.Level).ToList();
            var problems = CheckResults.Count(r => r.Level != CheckLevel.Ok);
            Report(problems == 0 ? T("Files checked: no problem found") : Count(problems, "Files checked: {0} point to look at", "Files checked: {0} points to look at"), problems > 0);
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>A name no profile uses yet: "My lineup", then "My lineup 2"...</summary>
    public string FreeProfileName(string name)
    {
        var used = _store.Profiles.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var free = name;
        for (var i = 2; used.Contains(free); i++)
        {
            free = $"{name} {i}";
        }

        return free;
    }

    /// <summary>A new profile, a copy of the one in use (Default gives an empty one), which becomes the one in use.</summary>
    public Task NewProfileAsync(string name)
    {
        var profile = new Profile { Name = name.Trim() };
        profile.Set(_store.Plan);
        _store.Profiles.Add(profile);
        _store.ActiveProfile = profile.Id;
        RaiseProfiles();
        return SavedAsync();
    }

    public void RenameProfile(string name)
    {
        if (_store.Active is { } profile)
        {
            profile.Name = name.Trim();
            _store.Save();
            RaiseProfiles();
        }
    }

    /// <summary>Deletes the profile in use and goes back to Default. What is in the game stays until Apply.</summary>
    public Task DeleteProfileAsync()
    {
        _store.Profiles.RemoveAll(p => p.Id == _store.ActiveProfile);
        _store.ActiveProfile = null;
        RaiseProfiles();
        return SavedAsync();
    }

    public async Task ApplyAsync()
    {
        if (_game is not { } game || !CanApply)
        {
            return;
        }

        IsApplying = true;
        try
        {
            var plan = _store.Plan;
            var placed = _store.PlacedSha256;
            var hash = await Task.Run(() =>
            {
                // The game textures are copied again when a pak changed since they were last taken.
                Liveries.Fetch(game, TextureFolder, plan.Paints.Select(p => p.Texture).ToList(), OodleFolders);
                return Lineup.Install(
                    plan.IsEmpty ? null : Lineup.BuildPak(CacheFolder, BuildFolder, plan, TextureFolder, image => Images.Load(Path.Combine(LiveryFolder, image))),
                    game.ModsFolder, placed, GameInstall.IsGameRunning);
            });
            _store.Applied = plan.IsEmpty ? null : plan;
            _store.PlacedSha256 = hash;
            _store.AppliedBuild = Lineup.BuildVersion;
            _store.Save();
            Report(plan.IsEmpty ? T("Original game back") : T("{0} is in the game", ActiveProfile.Name), false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException or FormatException)
        {
            Report(e.Message, true);
        }
        finally
        {
            IsApplying = false;
            RaiseLineup();
        }
    }

    /// <summary>Takes the launcher's pak out of the game right away and goes back to Default. Profiles are kept.</summary>
    public async Task ResetEverythingAsync()
    {
        if (_game is not { } game || IsApplying)
        {
            return;
        }

        IsApplying = true;
        try
        {
            var placed = _store.PlacedSha256;
            await Task.Run(() => Lineup.Install(null, game.ModsFolder, placed, GameInstall.IsGameRunning));
            _store.Applied = null;
            _store.PlacedSha256 = null;
            _store.ActiveProfile = null;
            Report(T("Original game back"), false);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Report(e.Message, true);
            return;
        }
        finally
        {
            IsApplying = false;
        }

        RaiseProfiles();
        await SavedAsync();
    }

    public async Task AddPilotAsync(string name, string countryId)
    {
        if (_store.Active is not { } profile)
        {
            return;
        }

        var id = NewId(name, _pilots.Select(p => p.Id));
        profile.Pilots.Add(new NewPilot(id, name.Trim(), countryId));
        await SavedAsync();
        SelectedPilot = _pilots.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>The first team number from 790 up that no team uses.</summary>
    public int NextFreeNumber()
    {
        var used = _crews.Select(c => c.Team.Number).ToHashSet();
        var number = Lineup.FirstFreeNumber;
        while (used.Contains(number))
        {
            number++;
        }

        return number;
    }

    /// <summary>
    /// The liveries a vehicle can wear in an edition: those of the game's teams there that drive the same vehicle model.
    /// In the game a livery belongs to a team row, so a Toyota only ever gets a Toyota livery.
    /// </summary>
    public IReadOnlyList<CrewItem> Skins(string table, VehicleItem vehicle) =>
        _crews.Where(c => !c.IsAdded && c.Team.Table == table && c.OriginalVehicle.Model.Blueprint == vehicle.Model.Blueprint).ToList();

    /// <summary>Vehicles that have at least one livery in an edition's table.</summary>
    public IReadOnlyList<VehicleItem> VehiclesWithSkins(string table) =>
        _vehicles.Where(v => _crews.Any(c => !c.IsAdded && c.Team.Table == table && c.OriginalVehicle.Model.Blueprint == v.Model.Blueprint)).ToList();

    /// <summary>The sub-team table of an edition, such as "Dakar22" -> "Dakar22Teams/DT_SubTeamDataDK22".</summary>
    public string? TableOf(string edition) => _crews.FirstOrDefault(c => c.Team.Edition == edition)?.Team.Table;

    internal CrewItem? CrewAt(string table, string row) => _crewIndex.GetValueOrDefault((table, row));

    /// <summary>
    /// Adds a new team next to the others: it wears the livery of <paramref name="skin"/> (a team of the same edition and
    /// vehicle model), drives <paramref name="vehicle"/>, and has its own number, name and crew. Nothing else changes.
    /// </summary>
    public async Task AddTeamAsync(CrewItem skin, VehicleItem vehicle, int number, string name, PilotItem driver, PilotItem? coDriver)
    {
        if (_store.Active is not { } profile)
        {
            return;
        }

        if (_crews.Any(c => c.Team.Number == number))
        {
            Report(T("Number {0} is taken", number), true);
            return;
        }

        profile.Teams.Add(new NewTeam(skin.Team.Table, skin.Team.Row, number, name.Trim(), vehicle.Id, driver.Id,
            skin.HasCoDriver ? (coDriver ?? driver).Id : null));
        await SavedAsync();
        SelectedCrew = _crews.FirstOrDefault(c => c.Team.Number == number);
    }

    /// <summary>Removes a driver the user added, unless a team uses them.</summary>
    public Task DeletePilotAsync(PilotItem pilot)
    {
        if (_crews.Any(c => c.Driver == pilot || c.CoDriver == pilot || c.OriginalDriver == pilot || c.OriginalCoDriver == pilot))
        {
            Report(T("{0} is in a team", pilot.Name), true);
            return Task.CompletedTask;
        }

        _store.Active?.Pilots.RemoveAll(p => p.Id == pilot.Id);
        return SavedAsync();
    }

    /// <summary>Removes a team the user added.</summary>
    public Task DeleteTeamAsync(CrewItem crew)
    {
        _store.Active?.Teams.RemoveAll(t => t.Table == crew.Team.Table && t.Row == crew.Team.Row);
        _store.Active?.Changes.RemoveAll(c => c.Table == crew.Team.Table && c.Row == crew.Team.Row);
        _store.Active?.Paints.RemoveAll(p => p.Table == crew.Team.Table && p.Row == crew.Team.Row);
        return SavedAsync();
    }

    /// <summary>The painted parts of a team's livery in the profile in use.</summary>
    public IReadOnlyList<LiveryPaint> PaintsOf(CrewItem crew) =>
        _store.Active?.Paints.Where(p => p.Table == crew.Team.Table && p.Row == crew.Team.Row).ToList() ?? [];

    /// <summary>Copies the textures out of the game (once) and gives where each one is.</summary>
    public Task<IReadOnlyDictionary<string, string>> FetchTexturesAsync(IReadOnlyCollection<string> textures) =>
        _game is { } game
            ? Task.Run(() => Liveries.Fetch(game, TextureFolder, textures, OodleFolders))
            : Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    /// <summary>A fetched game texture, at most <paramref name="maxWidth"/> wide.</summary>
    public static DecodedImage? GameTexture(string file, int maxWidth)
    {
        var files = Liveries.Open(TextureFolder, file);
        return TextureDecoder.Decode(files.Uexp, maxWidth, files.Ubulk);
    }

    /// <summary>The car's 3D model; null when it cannot be read.</summary>
    public async Task<CarModel?> CarAsync(string blueprint)
    {
        if (_game is not { } game)
        {
            return null;
        }

        try
        {
            return await Task.Run(() => GameModels.Car(game, OodleFolders, blueprint));
        }
        catch (Exception e) when (e is IOException or OodleMissingException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Replaces the painted parts of a team's livery in the profile in use (texture -> picture file name).</summary>
    public void SavePaints(CrewItem crew, IReadOnlyDictionary<string, string> images, IReadOnlyDictionary<string, string> files)
    {
        if (_store.Active is not { } profile)
        {
            return;
        }

        profile.Paints.RemoveAll(p => p.Table == crew.Team.Table && p.Row == crew.Team.Row);
        profile.Paints.AddRange(images.Select(i => new LiveryPaint(crew.Team.Table, crew.Team.Row, i.Key, files[i.Key], i.Value)));
        _store.Save();
        RaiseLineup();
    }

    internal void CrewsChanged()
    {
        if (_store.Active is { } profile)
        {
            profile.Changes = _crews.Select(c => c.Change).OfType<CrewChange>().ToList();

            // A painted part goes with the livery it was painted on: another skin drops it.
            profile.Paints.RemoveAll(p => _crewIndex.TryGetValue((p.Table, p.Row), out var crew) && crew.LiveryParts.All(l => l.Texture != p.Texture));
            _store.Save();
        }

        RaiseLineup();
    }

    private async Task SavedAsync()
    {
        _store.Save();
        await ReloadAsync();
        RaiseLineup();
    }

    private void ShowLineup(IReadOnlyList<Team> teams)
    {
        var vehicles = _vehicles.ToDictionary(v => v.Id);
        var pilots = _pilots.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var plan = _store.Plan;
        var added = plan.Teams.Select(t => (t.Table, t.Row)).ToHashSet();
        var changes = plan.Changes.ToDictionary(c => (c.Table, c.Row));

        _crews = teams
            .Where(t => vehicles.ContainsKey(t.VehicleId))
            .Select(t => new CrewItem(this, t, vehicles, pilots, added.Contains((t.Table, t.Row))))
            .OrderBy(c => c.Team.Number)
            .ToList();
        _crewIndex = _crews.ToDictionary(c => (c.Team.Table, c.Team.Row));

        // Edits saved earlier come back on the cards; ones whose team is gone are dropped.
        foreach (var crew in _crews)
        {
            crew.Restore(changes.GetValueOrDefault((crew.Team.Table, crew.Team.Row)));
        }

        if (_store.Active is { } profile)
        {
            profile.Changes = _crews.Select(c => c.Change).OfType<CrewChange>().ToList();
        }

        CrewsView = new ListCollectionView(_crews)
        {
            Filter = o => o is CrewItem c && (Class == "All" || c.Class == Class) && (Edition == "All" || c.Team.Edition == Edition)
                && Matches(Search, c.Team.Name, c.VehicleName, c.CrewText, c.Team.Number.ToString(CultureInfo.InvariantCulture)),
        };
        SelectedCrew = null;
        foreach (var name in new[] { nameof(CrewCount), nameof(PilotOptions), nameof(VehicleOptions), nameof(Countries) })
        {
            Raise(name);
        }

        RaiseLineup();
    }

    /// <summary>"Hilux Evo" -> "DDR_HiluxEvo", with a number added when that is taken.</summary>
    private static string NewId(string name, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = "DDR_" + Regex.Replace(name, "[^A-Za-z0-9]", "");
        var id = stem;
        for (var i = 2; used.Contains(id); i++)
        {
            id = $"{stem}{i}";
        }

        return id;
    }

    private void RaiseLineup()
    {
        // A pak built by an older launcher counts as not applied: Apply rebuilds it.
        IsPending = !_store.Plan.Equals(_store.Applied ?? LineupPlan.Empty)
            || (_store.Applied is not null && _store.AppliedBuild != Lineup.BuildVersion);
        foreach (var name in new[] { nameof(EditedCount), nameof(ChangesText), nameof(HasEdits), nameof(IsPending), nameof(PendingText), nameof(CanApply) })
        {
            Raise(name);
        }
    }

    private void RaiseProfiles()
    {
        foreach (var name in new[] { nameof(Profiles), nameof(ActiveProfile), nameof(CanEdit), nameof(ChangesText), nameof(PendingText) })
        {
            Raise(name);
        }
    }
}

/// <summary>
/// One team on the line-up screen. Setting the vehicle, its skin or a driver records the edit right away.
/// The skin is the livery of another team of the same edition that drives the same vehicle model.
/// </summary>
/// <summary>A profile in the list; a null id is Default (the game as it ships).</summary>
public sealed record ProfileItem(string? Id, string Name);

public sealed class CrewItem : ObservableObject
{
    private readonly DatabaseViewModel _owner;
    private readonly IReadOnlyDictionary<string, VehicleItem> _vehicles;
    private readonly IReadOnlyDictionary<string, PilotItem> _pilots;
    private VehicleItem _vehicle;
    private CrewItem? _skin;
    private PilotItem? _driver;
    private PilotItem? _coDriver;
    private ImageSource? _teamPicture;
    private ImageSource? _crewPhoto;

    internal CrewItem(DatabaseViewModel owner, Team team, IReadOnlyDictionary<string, VehicleItem> vehicles,
        IReadOnlyDictionary<string, PilotItem> pilots, bool isAdded)
    {
        _owner = owner;
        _vehicles = vehicles;
        _pilots = pilots;
        Team = team;
        IsAdded = isAdded;
        OriginalVehicle = _vehicle = vehicles[team.VehicleId];
        OriginalDriver = _driver = Member(CrewRole.Driver);
        OriginalCoDriver = _coDriver = Member(CrewRole.CoDriver);
    }

    public Team Team { get; }

    /// <summary>A team the user added: it can be deleted.</summary>
    public bool IsAdded { get; }

    public VehicleItem OriginalVehicle { get; }

    public PilotItem? OriginalDriver { get; }

    public PilotItem? OriginalCoDriver { get; }

    public bool HasCoDriver => Team.HasCoDriver;

    /// <summary>The team's own picture from the selection screen, with its livery.</summary>
    public ImageSource? TeamPicture
    {
        get => _teamPicture;
        set
        {
            if (Set(ref _teamPicture, value))
            {
                Raise(nameof(Picture));
            }
        }
    }

    /// <summary>The game's photo of this crew, when it has one (Dakar 20 and 21 teams).</summary>
    public ImageSource? CrewPhoto
    {
        get => _crewPhoto;
        set => Set(ref _crewPhoto, value);
    }

    /// <summary>The picture of the livery the team wears.</summary>
    public ImageSource? Picture => Skin?.TeamPicture ?? TeamPicture;

    /// <summary>The parts of the livery the team wears, as the game has them.</summary>
    public IReadOnlyList<LiveryPart> LiveryParts => (Skin ?? this).Team.Livery ?? [];

    /// <summary>"#205 OVERDRIVE TOYOTA", as a skin in a list.</summary>
    public string SkinName => $"#{Team.Number} {Team.Name}";

    /// <summary>Vehicles of the same class that have a livery in this edition: a car team keeps a car, with a livery made for it.</summary>
    public IReadOnlyList<VehicleItem> VehicleOptions => _owner.VehiclesWithSkins(Team.Table).Where(v => v.Class == Class).ToList();

    /// <summary>The liveries the chosen vehicle can wear here.</summary>
    public IReadOnlyList<CrewItem> SkinOptions =>
        _owner.Skins(Team.Table, Vehicle) is var skins && Skin is { } current && !skins.Contains(current) ? [current, .. skins] : skins;

    public IReadOnlyList<PilotItem> PilotOptions => _owner.PilotOptions;

    /// <summary>False on Default: the game's own line-up is shown, not edited.</summary>
    public bool CanEdit => _owner.CanEdit;

    public string Class => OriginalVehicle.Class;

    public string EditionText => DatabaseViewModel.EditionName(Team.Edition).ToUpperInvariant();

    public string VehicleName => Vehicle.DisplayName;

    public string CrewText => HasCoDriver ? $"{Driver?.Name} / {CoDriver?.Name}" : Driver?.Name ?? "";

    public string OriginalText => $"{OriginalVehicle.DisplayName} · {OriginalDriver?.Name}" + (HasCoDriver ? $" / {OriginalCoDriver?.Name}" : "");

    public bool IsEdited => Vehicle != OriginalVehicle || _skin is not null || Driver != OriginalDriver || CoDriver != OriginalCoDriver;

    /// <summary>Choosing another vehicle also puts on one of its liveries; the team's own vehicle gets its own livery back.</summary>
    public VehicleItem Vehicle
    {
        get => _vehicle;
        set
        {
            if (value is null || value == _vehicle)
            {
                return;
            }

            _vehicle = value;
            _skin = value.Model.Blueprint == OriginalVehicle.Model.Blueprint ? null : _owner.Skins(Team.Table, value).FirstOrDefault();
            Changed();
        }
    }

    /// <summary>The team whose livery this team wears; null for its own.</summary>
    public CrewItem? Skin
    {
        get => _skin ?? this;
        set
        {
            var skin = value == this ? null : value;
            if (value is not null && skin != _skin)
            {
                _skin = skin;
                Changed();
            }
        }
    }

    public PilotItem? Driver
    {
        get => _driver;
        set
        {
            if (value is not null && value != _driver)
            {
                _driver = value;
                Changed();
            }
        }
    }

    public PilotItem? CoDriver
    {
        get => _coDriver;
        set
        {
            if (value is not null && value != _coDriver)
            {
                _coDriver = value;
                Changed();
            }
        }
    }

    /// <summary>What differs from the game, or null when nothing does.</summary>
    public CrewChange? Change => IsEdited
        ? new CrewChange(Team.Table, Team.Row,
            Vehicle != OriginalVehicle ? Vehicle.Id : null,
            Driver != OriginalDriver ? Driver?.Id : null,
            CoDriver != OriginalCoDriver ? CoDriver?.Id : null,
            _skin?.Team.Row)
        : null;

    /// <summary>Shows a saved edit, or the game's crew when <paramref name="change"/> is null, without recording anything.</summary>
    internal void Restore(CrewChange? change)
    {
        _vehicle = change?.VehicleId is { } v && _vehicles.TryGetValue(v, out var vehicle) ? vehicle : OriginalVehicle;
        _skin = change?.SkinRow is { } row ? _owner.CrewAt(Team.Table, row) : null;
        _driver = change?.DriverId is { } d && _pilots.TryGetValue(d, out var driver) ? driver : OriginalDriver;
        _coDriver = change?.CoDriverId is { } c && _pilots.TryGetValue(c, out var coDriver) ? coDriver : OriginalCoDriver;
        RaiseAll();
    }

    public void Reset()
    {
        Restore(null);
        _owner.CrewsChanged();
    }

    private void Changed()
    {
        RaiseAll();
        _owner.CrewsChanged();
    }

    private void RaiseAll()
    {
        foreach (var name in new[] { nameof(Vehicle), nameof(Skin), nameof(SkinOptions), nameof(Picture), nameof(Driver), nameof(CoDriver), nameof(VehicleName), nameof(CrewText), nameof(IsEdited) })
        {
            Raise(name);
        }
    }

    private PilotItem? Member(CrewRole role) =>
        Team.Members.FirstOrDefault(m => m.Role == role) is { } m && _pilots.TryGetValue(m.PilotId, out var p) ? p : null;
}

/// <summary>A file check line, translated.</summary>
public sealed record CheckLine(CheckLevel Level, string Title, string Detail);
