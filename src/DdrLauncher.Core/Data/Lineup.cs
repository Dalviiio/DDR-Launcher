using DdrLauncher.Core.Safety;
using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace DdrLauncher.Core.Data;

/// <summary>A new crew for one team row. A null field keeps what the row has.</summary>
/// <param name="SkinRow">
/// A team row of the same table whose livery (materials, pictures) the team takes. It must drive the same vehicle model as
/// <paramref name="VehicleId"/>: in the game a livery belongs to a team row, not to the vehicle.
/// </param>
public sealed record CrewChange(string Table, string Row, string? VehicleId = null, string? DriverId = null, string? CoDriverId = null, string? SkinRow = null);

/// <summary>A new vehicle: a copy of <paramref name="BaseId"/> (same 3D model and handling) under its own name, price and stats.</summary>
public sealed record NewVehicle(string Id, string BaseId, string Name, int Price, int Speed, int Acceleration, int Handling, int Strength);

/// <summary>A new driver or co-driver.</summary>
public sealed record NewPilot(string Id, string Name, string CountryId);

/// <summary>
/// A new team in an edition, added next to the others: it starts from the <paramref name="BaseRow"/> team of that edition,
/// whose livery (skin) it wears, with its own number, name, vehicle and crew. The vehicle must be the same model as the base team's.
/// </summary>
public sealed record NewTeam(string Table, string BaseRow, int Number, string Name, string VehicleId, string DriverId, string? CoDriverId)
{
    public string Row => Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Everything the user changed or added. Applied as one mod pak. Two plans are equal when they hold the same items.</summary>
/// <param name="Paints">Painted livery parts; null (a plan saved before liveries) means none.</param>
public sealed record LineupPlan(
    IReadOnlyList<CrewChange> Changes,
    IReadOnlyList<NewVehicle> Vehicles,
    IReadOnlyList<NewPilot> Pilots,
    IReadOnlyList<NewTeam> Teams,
    IReadOnlyList<LiveryPaint>? Paints = null)
{
    public static readonly LineupPlan Empty = new([], [], [], []);

    public IReadOnlyList<LiveryPaint> Paints { get; init; } = Paints ?? [];

    public bool IsEmpty => Changes.Count == 0 && Vehicles.Count == 0 && Pilots.Count == 0 && Teams.Count == 0 && Paints.Count == 0;

    public bool Equals(LineupPlan? other) =>
        other is not null && Changes.SequenceEqual(other.Changes) && Vehicles.SequenceEqual(other.Vehicles)
        && Pilots.SequenceEqual(other.Pilots) && Teams.SequenceEqual(other.Teams) && Paints.SequenceEqual(other.Paints);

    public override int GetHashCode() => HashCode.Combine(Changes.Count, Vehicles.Count, Pilots.Count, Teams.Count, Paints.Count);
}

/// <summary>
/// Turns the plan into one mod pak in ~mods. The game's own files are never touched:
/// deleting the pak gives the original game back.
/// </summary>
public static class Lineup
{
    /// <summary>Sorts after the game's paks and after other "zzz_" mods named before it, so its tables win.</summary>
    public const string PakName = "zzz_DalviiioDDRLauncher_P.pak";

    /// <summary>
    /// Changes whenever the way the pak is built changes, so a pak built by an older launcher is rebuilt (Apply again)
    /// even when the plan is the same. 2: added names get their own text ids.
    /// </summary>
    public const int BuildVersion = 2;

    /// <summary>Team numbers from here up are free in the base game.</summary>
    public const int FirstFreeNumber = 790;

    /// <summary>Builds the mod pak from the cached game tables.</summary>
    /// <param name="cacheFolder">Where <see cref="GameData.Load"/> copied the game tables.</param>
    /// <param name="workFolder">Scratch folder for the rewritten tables; emptied first.</param>
    /// <param name="textureFolder">Where <see cref="Liveries.Fetch"/> copied the textures the paints start from.</param>
    /// <param name="loadImage">Reads a paint's picture (by file name) as BGRA pixels the size of its texture.</param>
    public static byte[] BuildPak(string cacheFolder, string workFolder, LineupPlan plan, string textureFolder, Func<string, Paks.DecodedImage> loadImage)
    {
        if (Directory.Exists(workFolder))
        {
            Directory.Delete(workFolder, recursive: true);
        }

        var (tables, texts) = Apply(cacheFolder, plan);
        var files = texts.Changed ? texts.Files() : [];
        foreach (var (name, table) in tables)
        {
            var asset = TableRows.Folder + name;
            var output = Path.Combine(workFolder, asset + ".uasset");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            table.Write(output);
            files[asset + ".uasset"] = File.ReadAllBytes(output);
            files[asset + ".uexp"] = File.ReadAllBytes(Path.ChangeExtension(output, ".uexp"));
        }

        foreach (var paint in plan.Paints)
        {
            var (package, file) = Liveries.Copy(paint);
            var copy = LiveryTexture.Copy(Liveries.Open(textureFolder, paint.File), Liveries.Package(paint.Texture), package, loadImage(paint.Image));
            files[file + ".uasset"] = copy.Uasset;
            files[file + ".uexp"] = copy.Uexp;
            if (copy.Ubulk.Length > 0)
            {
                files[file + ".ubulk"] = copy.Ubulk;
            }
        }

        using var pak = new MemoryStream();
        Paks.PakWriter.Write(pak, files);
        return pak.ToArray();
    }

    /// <summary>Puts the pak in ~mods, or removes it when <paramref name="pak"/> is null. Returns the SHA-256 of what is now there.</summary>
    /// <param name="placedSha256">Hash of the pak the launcher placed last time; any other file with that name is left alone.</param>
    public static string? Install(byte[]? pak, string modsFolder, string? placedSha256, Func<bool> isGameRunning)
    {
        ModsGuard.EnsureGameClosed(isGameRunning);
        var target = Path.Combine(modsFolder, PakName);
        if (File.Exists(target) && !FileHasher.HashEquals(FileHasher.Sha256(target), placedSha256))
        {
            throw new InvalidOperationException($"{PakName} in ~mods was not placed by the launcher, so the launcher leaves it alone.");
        }

        if (pak is null)
        {
            File.Delete(target);
            return null;
        }

        Directory.CreateDirectory(modsFolder);
        try
        {
            AtomicFile.Write(target, pak);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new InvalidOperationException($"Another program has {PakName} open. Close it, then apply again.", e);
        }

        return FileHasher.Sha256(target);
    }

    /// <summary>
    /// The tables the plan touches, opened from the cache and changed in memory (by table name),
    /// and the game's texts with the names of what the plan adds.
    /// </summary>
    internal static (Dictionary<string, UAsset> Tables, GameTexts Texts) Apply(string cacheFolder, LineupPlan plan)
    {
        var texts = GameTexts.Open(cacheFolder);
        var editions = GameData.SubTeamTables(cacheFolder);
        var tables = new Dictionary<string, UAsset>();
        UAsset Table(string name) =>
            tables.TryGetValue(name, out var table) ? table : tables[name] = TableRows.Open(cacheFolder, name);

        foreach (var vehicle in plan.Vehicles)
        {
            AddVehicle(Table("DT_VehicleData"), vehicle, texts);
        }

        foreach (var pilot in plan.Pilots)
        {
            AddPilot(Table("DT_PilotData"), pilot, texts);
        }

        foreach (var team in plan.Teams)
        {
            CheckTeamTable(editions, team.Table);
            var subTeams = Table(team.Table);
            var baseRow = TableRows.Rows(subTeams).FirstOrDefault(r => r.Id() == team.BaseRow) ?? throw new InvalidDataException($"Team {team.BaseRow} is not in {team.Table}.");
            var names = subTeams.HandleTable(baseRow.Get("TeamData")) ?? throw new InvalidDataException($"Team {team.BaseRow} has no team name table.");
            AddTeam(subTeams, Table(names), team, texts);
        }

        foreach (var group in plan.Changes.GroupBy(c => c.Table))
        {
            CheckTeamTable(editions, group.Key);
            ApplyCrews(Table(group.Key), group);
        }

        // Last: a paint goes on the team as it ends up, added teams included.
        foreach (var paint in plan.Paints)
        {
            CheckTeamTable(editions, paint.Table);
            var table = Table(paint.Table);
            var row = TableRows.Rows(table).FirstOrDefault(r => r.Id() == paint.Row) ?? throw new InvalidDataException($"Team {paint.Row} is not in {paint.Table}.");
            Liveries.Paint(table, row, paint);
        }

        return (tables, texts);
    }

    private static void AddVehicle(UAsset table, NewVehicle vehicle, GameTexts texts)
    {
        var row = Clone(table, vehicle.BaseId, vehicle.Id);

        // The game shows the text whose id is in NameId (from its text database); NameIdText is only a label.
        row.SetInt("NameId", texts.Add(vehicle.Name));
        row.SetName(table, vehicle.Name, "NameIdText");
        row.SetInt("Price", vehicle.Price);
        row.SetInt("SpeedStat", vehicle.Speed);
        row.SetInt("AccelerationStat", vehicle.Acceleration);
        row.SetInt("HandlingStat", vehicle.Handling);
        row.SetInt("StrengthStat", vehicle.Strength);
    }

    private static void AddPilot(UAsset table, NewPilot pilot, GameTexts texts)
    {
        // Any real driver row serves as the template: every field below is overwritten.
        var template = TableRows.Rows(table).FirstOrDefault(r => !GameData.IsPlaceholder(r.Id()))
            ?? throw new InvalidDataException("The driver table is empty.");
        var row = Clone(table, template.Id(), pilot.Id);
        row.SetInt("NameId", texts.Add(pilot.Name));
        row.SetName(table, pilot.Name, "NameIdText");
        row.SetName(table, pilot.CountryId, "CountryData");
    }

    private static void AddTeam(UAsset subTeams, UAsset teamNames, NewTeam team, GameTexts texts)
    {
        if (TableRows.Rows(subTeams).Any(r => r.Id() == team.Row))
        {
            throw new InvalidDataException($"Team number {team.Number} is already taken.");
        }

        var row = Clone(subTeams, team.BaseRow, team.Row);
        row.SetInt("TeamNumber", team.Number);
        foreach (var flag in new[] { "bIsEnabled", "bShowInTeamSelection", "bCanBeBought" })
        {
            if (row.Get(flag) is BoolPropertyData b)
            {
                b.Value = true;
            }
        }

        if (row.Get("RequiredDLCList") is ArrayPropertyData dlc)
        {
            dlc.Value = [];
        }

        // The team's name lives in the edition's team table: give it its own row there.
        var nameRow = Clone(teamNames, row.Get("TeamData", "RowName").Text(), "DDR_Team_" + team.Row);
        nameRow.SetInt("NameId", texts.Add(team.Name));
        nameRow.SetName(teamNames, team.Name, "NameIdText");
        row.SetName(subTeams, nameRow.Id(), "TeamData");

        ApplyCrews(subTeams, [new CrewChange(team.Table, team.Row, team.VehicleId, team.DriverId, team.CoDriverId)]);
    }

    private static void ApplyCrews(UAsset table, IEnumerable<CrewChange> changes)
    {
        var rows = TableRows.Rows(table).ToDictionary(r => r.Id());

        // Liveries are copied as the game has them, before any change in this table touches them.
        var skins = changes.Select(c => c.SkinRow).OfType<string>().Distinct()
            .ToDictionary(r => r, r => rows.TryGetValue(r, out var skin) ? DeepClone(skin) : throw new InvalidDataException($"Skin {r} is not in the table."));
        foreach (var change in changes)
        {
            if (!rows.TryGetValue(change.Row, out var row))
            {
                throw new InvalidDataException($"Team {change.Row} is not in {change.Table}.");
            }

            if (change.VehicleId is not null)
            {
                row.SetName(table, change.VehicleId, "VehicleData");
            }

            if (change.SkinRow is not null)
            {
                foreach (var field in LiveryFields)
                {
                    var index = row.Value.FindIndex(p => p.Name.ToString() == field);
                    if (index >= 0 && skins[change.SkinRow].Get(field) is { } livery)
                    {
                        row.Value[index] = DeepClone(livery);
                    }
                }
            }

            foreach (var (role, id) in new[] { (CrewRole.Driver, change.DriverId), (CrewRole.CoDriver, change.CoDriverId) })
            {
                if (id is null)
                {
                    continue;
                }

                var member = row.Get("TeamMembersList").Items().FirstOrDefault(m => m.Get("Type").Text().EndsWith("::" + role, StringComparison.Ordinal))
                    ?? throw new InvalidDataException($"Team {change.Row} has no {role} seat.");
                member.SetName(table, id, "PilotData");
            }
        }
    }

    /// <summary>The fields of a team row that make up its livery: materials and textures on the vehicle, and its pictures.</summary>
    private static readonly string[] LiveryFields =
        ["VehicleDetails", "VehicleCustomization", "VehiclePicture", "VehicleSlotNormal", "VehicleSlotHover", "ProfileBarPicture", "ManageCareerPicture"];

    private static void CheckTeamTable(IReadOnlyList<string> editions, string table)
    {
        if (!editions.Contains(table))
        {
            throw new InvalidDataException($"{table} is not a team table.");
        }
    }

    /// <summary>Appends a deep copy of row <paramref name="from"/> named <paramref name="to"/>.</summary>
    private static StructPropertyData Clone(UAsset table, string from, string to)
    {
        var rows = TableRows.Rows(table);
        if (rows.Any(r => r.Id() == to))
        {
            throw new InvalidDataException($"{to} already exists.");
        }

        var copy = DeepClone(rows.FirstOrDefault(r => r.Id() == from) ?? throw new InvalidDataException($"{from} is not in the table."));
        copy.Name = UAssetAPI.UnrealTypes.FName.FromString(table, to);
        rows.Add(copy);
        return copy;
    }

    /// <summary>
    /// A full copy of a property and everything inside it. UAssetAPI 1.1.0's own Clone is not deep: a copied array keeps the
    /// same items (so editing a copied team's crew edited the original), and cloning a non-empty set throws. So children are
    /// taken out before Clone (which then copies only the property itself) and copied here, level by level.
    /// shortcut: drop this for PropertyData.Clone once UAssetAPI copies collections deeply.
    /// </summary>
    internal static T DeepClone<T>(T source)
        where T : PropertyData
    {
        PropertyData copy;
        switch (source)
        {
            case SetPropertyData set:
                var (items, removed) = (set.Value, set.ElementsToRemove);
                (set.Value, set.ElementsToRemove) = ([], null);
                try
                {
                    copy = (PropertyData)set.Clone();
                }
                finally
                {
                    (set.Value, set.ElementsToRemove) = (items, removed);
                }

                ((SetPropertyData)copy).Value = Copy(items);
                ((SetPropertyData)copy).ElementsToRemove = removed is null ? null : Copy(removed);
                break;
            case ArrayPropertyData array:
                var elements = array.Value;
                array.Value = [];
                try
                {
                    copy = (PropertyData)array.Clone();
                }
                finally
                {
                    array.Value = elements;
                }

                ((ArrayPropertyData)copy).Value = Copy(elements);
                break;
            case StructPropertyData structure:
                var fields = structure.Value;
                structure.Value = null;
                try
                {
                    copy = (PropertyData)structure.Clone();
                }
                finally
                {
                    structure.Value = fields;
                }

                ((StructPropertyData)copy).Value = fields?.Select(DeepClone).ToList();
                break;
            case MapPropertyData map:
                var (entries, keysToRemove) = (map.Value, map.KeysToRemove);
                (map.Value, map.KeysToRemove) = (new UAssetAPI.UnrealTypes.TMap<PropertyData, PropertyData>(), null);
                try
                {
                    copy = (PropertyData)map.Clone();
                }
                finally
                {
                    (map.Value, map.KeysToRemove) = (entries, keysToRemove);
                }

                var copiedMap = (MapPropertyData)copy;
                copiedMap.Value = new UAssetAPI.UnrealTypes.TMap<PropertyData, PropertyData>();
                foreach (var (key, value) in entries)
                {
                    copiedMap.Value.Add(DeepClone(key), DeepClone(value));
                }

                copiedMap.KeysToRemove = keysToRemove is null ? null : Copy(keysToRemove);
                break;
            default:
                copy = (PropertyData)source.Clone();
                break;
        }

        return (T)copy;
    }

    private static PropertyData[] Copy(PropertyData[] items) => items.Select(i => i is null ? null! : DeepClone(i)).ToArray();
}
