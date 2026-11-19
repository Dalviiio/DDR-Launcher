using System.IO;
using System.Text.Json.Serialization;
using DdrLauncher.Core.Data;

namespace DdrLauncher.App;

/// <summary>One saved set of edits: crews, and the vehicles, drivers and teams it adds.</summary>
public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = "";

    public List<CrewChange> Changes { get; set; } = [];

    public List<NewVehicle> Vehicles { get; set; } = [];

    public List<NewPilot> Pilots { get; set; } = [];

    public List<NewTeam> Teams { get; set; } = [];

    [JsonIgnore]
    public LineupPlan Plan => new(Changes.ToList(), Vehicles.ToList(), Pilots.ToList(), Teams.ToList());

    public void Set(LineupPlan plan)
    {
        Changes = [.. plan.Changes];
        Vehicles = [.. plan.Vehicles];
        Pilots = [.. plan.Pilots];
        Teams = [.. plan.Teams];
    }
}

/// <summary>
/// The user's profiles, the one in use (none: the game's own line-up, "Default"), what is in the game right now,
/// and the hash of the pak the launcher placed.
/// </summary>
public sealed class LineupStore
{
    private static readonly string FilePath = Path.Combine(Settings.DataFolder, "lineup.json");

    public List<Profile> Profiles { get; set; } = [];

    /// <summary>The profile in use; null for Default (the game as it ships, not editable).</summary>
    public string? ActiveProfile { get; set; }

    /// <summary>What the pak in ~mods holds now; null when there is none.</summary>
    public LineupPlan? Applied { get; set; }

    public string? PlacedSha256 { get; set; }

    /// <summary>The <see cref="Lineup.BuildVersion"/> the pak in ~mods was built with.</summary>
    public int AppliedBuild { get; set; }

    // Before profiles, the edits were kept here: they become the first profile.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CrewChange>? Changes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NewVehicle>? Vehicles { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NewPilot>? Pilots { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NewTeam>? Teams { get; set; }

    [JsonIgnore]
    public Profile? Active => Profiles.FirstOrDefault(p => p.Id == ActiveProfile);

    /// <summary>The edits of the profile in use; nothing for Default.</summary>
    [JsonIgnore]
    public LineupPlan Plan => Active?.Plan ?? LineupPlan.Empty;

    public static LineupStore Load()
    {
        var store = JsonFile.Load<LineupStore>(FilePath);
        if (store.Profiles.Count == 0 && (store.Changes?.Count > 0 || store.Vehicles?.Count > 0 || store.Pilots?.Count > 0 || store.Teams?.Count > 0))
        {
            var profile = new Profile
            {
                Name = "My lineup",
                Changes = store.Changes ?? [],
                Vehicles = store.Vehicles ?? [],
                Pilots = store.Pilots ?? [],
                Teams = store.Teams ?? [],
            };
            store.Profiles.Add(profile);
            store.ActiveProfile = profile.Id;
        }

        (store.Changes, store.Vehicles, store.Pilots, store.Teams) = (null, null, null, null);
        return store;
    }

    public void Save() => JsonFile.Save(FilePath, this);
}
