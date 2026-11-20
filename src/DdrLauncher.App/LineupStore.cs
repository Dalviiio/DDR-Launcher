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

    public List<NewPilot> Pilots { get; set; } = [];

    public List<NewTeam> Teams { get; set; } = [];

    public List<LiveryPaint> Paints { get; set; } = [];

    [JsonIgnore]
    public LineupPlan Plan => new(Changes.ToList(), Pilots.ToList(), Teams.ToList(), Paints.ToList());

    public void Set(LineupPlan plan)
    {
        Changes = [.. plan.Changes];
        Pilots = [.. plan.Pilots];
        Teams = [.. plan.Teams];
        Paints = [.. plan.Paints];
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

    [JsonIgnore]
    public Profile? Active => Profiles.FirstOrDefault(p => p.Id == ActiveProfile);

    /// <summary>The edits of the profile in use; nothing for Default.</summary>
    [JsonIgnore]
    public LineupPlan Plan => Active?.Plan ?? LineupPlan.Empty;

    public static LineupStore Load() => JsonFile.Load<LineupStore>(FilePath);

    public void Save() => JsonFile.Save(FilePath, this);
}
