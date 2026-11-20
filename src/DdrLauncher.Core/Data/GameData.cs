using DdrLauncher.Core.Game;
using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace DdrLauncher.Core.Data;

/// <param name="Blueprint">The vehicle's game class: vehicles sharing it share the 3D model.</param>
/// <param name="Picture">A picture of the vehicle from a team that drives it, as a game asset path; empty when there is none.</param>
public sealed record Vehicle(string Id, string Name, string Class, string Brand, int Price, float MaxSpeed,
    int Speed, int Acceleration, int Handling, int Strength, string Blueprint, string Picture = "");

public sealed record Pilot(string Id, string Name, string Country);

/// <summary>A crew seat. Other covers the team's mechanics.</summary>
public enum CrewRole
{
    Other,
    Driver,
    CoDriver,
}

public sealed record TeamMember(string PilotId, CrewRole Role);

/// <summary>A sub-team row: one entry in an edition's team selection, with its vehicle and crew.</summary>
/// <param name="Table">The sub-team table holding the row, such as "Dakar22Teams/DT_SubTeamDataDK22".</param>
/// <param name="Row">The row name in that table.</param>
/// <param name="Picture">The team's vehicle picture from the selection screen, as a game asset path; empty when it has none.</param>
/// <param name="CrewPhoto">Photo of the crew (driver on the left, co-driver on the right); empty when the team has none or only a silhouette.</param>
/// <param name="Livery">The parts of the team's livery on the car; null when unknown.</param>
public sealed record Team(string Table, string Row, string Edition, int Number, string Name, string VehicleId, bool Enabled, IReadOnlyList<TeamMember> Members,
    string Picture = "", string CrewPhoto = "", IReadOnlyList<LiveryPart>? Livery = null)
{
    /// <summary>The game's template rows ("Placeholder"), not real teams.</summary>
    public bool IsPlaceholder => GameData.IsPlaceholder(Row);

    public bool HasCoDriver => Members.Any(m => m.Role == CrewRole.CoDriver);
}

/// <summary>The game's cars, drivers and teams, read from its own data tables. Nothing is written to the game.</summary>
public sealed class GameData
{
    // Vehicles, drivers, brands and countries live in fixed tables; editions (sub-team tables) are found in the paks, mods included.
    private static readonly string[] Tables = ["DT_VehicleData", "DT_PilotData", "DT_BrandData", "DT_CountryData"];

    public required IReadOnlyList<Vehicle> Vehicles { get; init; }

    public required IReadOnlyList<Pilot> Pilots { get; init; }

    public required IReadOnlyList<Team> Teams { get; init; }

    /// <summary>Country row names and their display names, for the driver form.</summary>
    public required IReadOnlyDictionary<string, string> Countries { get; init; }


    /// <summary>The game's template rows ("Placeholder...", "Default...") hold no real team, car or driver.</summary>
    public static bool IsPlaceholder(string row) =>
        row.StartsWith("Placeholder", StringComparison.OrdinalIgnoreCase) || row.StartsWith("Default", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Copies the tables out of the game's paks into <paramref name="cacheFolder"/> (only when the paks changed), then reads them
    /// with <paramref name="plan"/> applied, so what is shown is what the mod pak would hold.
    /// </summary>
    /// <param name="oodleFolders">Folders searched for the Oodle library, needed only when the cache must be refreshed.</param>
    public static GameData Load(GameInstall game, string cacheFolder, LineupPlan plan, params string[] oodleFolders)
    {
        TableCache.Refresh(game, cacheFolder, [.. Tables.SelectMany(t => new[] { TableRows.Folder + t + ".uasset", TableRows.Folder + t + ".uexp" }),
            GameTexts.MessagesFile, GameTexts.LanguagesFile], IsTeamTable, oodleFolders);
        var changed = Lineup.Apply(cacheFolder, plan).Tables;
        return Read(name => changed.TryGetValue(name, out var table) ? table : TableRows.Open(cacheFolder, name), SubTeamTables(cacheFolder));
    }

    /// <summary>
    /// The editions' team tables in the cache, such as "Dakar22Teams/DT_SubTeamDataDK22", the game's and any a mod adds (a "Dakar26").
    /// </summary>
    internal static IReadOnlyList<string> SubTeamTables(string cacheFolder) =>
        TableCache.Found(cacheFolder)
            .Where(f => f.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(f).StartsWith("DT_SubTeam", StringComparison.Ordinal))
            .Select(f => f[TableRows.Folder.Length..^".uasset".Length])
            .ToList();

    // Edition tables (DT_SubTeam...) and their team names (DT_Team...), anywhere under the game's data tables.
    private static bool IsTeamTable(string path) =>
        path.StartsWith(TableRows.Folder, StringComparison.OrdinalIgnoreCase)
        && (Path.GetFileName(path).StartsWith("DT_SubTeam", StringComparison.Ordinal) || Path.GetFileName(path).StartsWith("DT_Team", StringComparison.Ordinal))
        && (path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase));

    internal static GameData Read(Func<string, UAsset> open, IReadOnlyList<string> subTeamTables)
    {
        var brands = NameIds(open("DT_BrandData"));
        var countries = NameIds(open("DT_CountryData"));

        // Each team row points at its name in the edition's team table; a mod's new edition brings its own.
        var names = new Dictionary<string, Dictionary<string, string>>();
        string TeamName(UAsset table, StructPropertyData row)
        {
            var id = row.Get("TeamData", "RowName").Text();
            if (table.HandleTable(row.Get("TeamData")) is not { } nameTable)
            {
                return id;
            }

            if (!names.TryGetValue(nameTable, out var byId))
            {
                byId = names[nameTable] = TryOpen(open, nameTable) is { } asset ? NameIds(asset) : [];
            }

            return Lookup(byId, id);
        }

        var teams = subTeamTables.SelectMany(name => open(name) is var table ? TableRows.Rows(table).Select(r => new Team(
            name,
            r.Id(),
            r.Get("DakarEdition").Text(),
            r.Get("TeamNumber").Int(),
            TeamName(table, r),
            r.Get("VehicleData", "RowName").Text(),
            r.Get("bIsEnabled") is BoolPropertyData { Value: true },
            r.Get("TeamMembersList").Items().Select(m => new TeamMember(m.Get("PilotData", "RowName").Text(), Role(m.Get("Type").Text()))).ToList(),
            // The selected-state picture is in colour; older teams' normal one is dimmed.
            r.Get("VehicleSlotHover").Text() is { Length: > 0 } hover ? hover : r.Get("VehicleSlotNormal").Text(),
            r.Get("TeamSelectionPicture").Text() is var photo && !photo.Contains("Placeholde", StringComparison.OrdinalIgnoreCase) ? photo : "",
            Liveries.Parts(r))) : [])
            .ToList();

        // A vehicle's picture: a clean side view ("_Right") from one of its teams, else one from a vehicle with the same model.
        var pictures = teams.Where(t => t.Picture != "").ToLookup(t => t.VehicleId, t => t.Picture);
        var vehicles = TableRows.Rows(open("DT_VehicleData")).Select(r => new Vehicle(
            r.Id(),
            r.Get("NameIdText").Text(),
            r.Get("ClassData", "RowName").Text(),
            Lookup(brands, r.Get("BrandData", "RowName").Text()),
            r.Get("Price").Int(),
            r.Get("PointMaxSpeed").Float(),
            r.Get("SpeedStat").Int(), r.Get("AccelerationStat").Int(), r.Get("HandlingStat").Int(), r.Get("StrengthStat").Int(),
            r.Get("VehicleClass").Text(),
            pictures[r.Id()].OrderBy(p => p.Contains("_Right", StringComparison.Ordinal) ? 0 : 1).FirstOrDefault() ?? "")).ToList();
        var byModel = vehicles.Where(v => v.Picture != "").GroupBy(v => v.Blueprint).ToDictionary(g => g.Key, g => g.First().Picture);
        vehicles = vehicles.Select(v => v.Picture == "" && byModel.TryGetValue(v.Blueprint, out var p) ? v with { Picture = p } : v).ToList();

        var pilots = TableRows.Rows(open("DT_PilotData")).Select(r => new Pilot(
            r.Id(),
            r.Get("NameIdText").Text(),
            Lookup(countries, r.Get("CountryData", "RowName").Text()))).ToList();

        return new GameData { Vehicles = vehicles, Pilots = pilots, Teams = teams, Countries = countries };
    }

    private static string Lookup(Dictionary<string, string> names, string id) => names.GetValueOrDefault(id, id);

    // A team table a row points at may be missing (a broken mod): its teams then show their row names.
    private static UAsset? TryOpen(Func<string, UAsset> open, string table)
    {
        try
        {
            return open(table);
        }
        catch (Exception e) when (e is IOException or FormatException or InvalidDataException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> NameIds(UAsset table) =>
        TableRows.Rows(table).GroupBy(r => r.Id()).ToDictionary(g => g.Key, g => g.First().Get("NameIdText").Text());

    // "EDKCharacterType::CoDriver" -> CrewRole.CoDriver; mechanics and unknown seats -> Other.
    private static CrewRole Role(string type) =>
        Enum.TryParse<CrewRole>(type[(type.LastIndexOf(':') + 1)..], out var role) ? role : CrewRole.Other;
}
