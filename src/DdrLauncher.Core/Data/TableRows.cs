using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace DdrLauncher.Core.Data;

/// <summary>Reading and writing the game's data tables with UAssetAPI: opening, rows, and fields by name.</summary>
internal static class TableRows
{
    public const string Folder = "Dakar2Game/Plugins/Game/DakarGame/Content/Data/DataTables/";

    /// <summary>Opens a table (such as "DT_VehicleData") from the cache the game tables were copied to.</summary>
    public static UAsset Open(string cacheFolder, string table) =>
        new(Path.Combine(cacheFolder, Folder + table + ".uasset"), EngineVersion.VER_UE4_27);

    public static List<StructPropertyData> Rows(UAsset table) => table.Exports.OfType<DataTableExport>().First().Table.Data;

    public static string Id(this StructPropertyData row) => row.Name.ToString();

    /// <summary>A field, or a field inside nested structs: row.Get("CountryData", "RowName").</summary>
    public static PropertyData? Get(this StructPropertyData row, params string[] path)
    {
        PropertyData? current = row;
        foreach (var name in path)
        {
            current = (current as StructPropertyData)?.Value?.FirstOrDefault(p => p.Name.ToString() == name);
        }

        return current;
    }

    /// <summary>
    /// The table a row handle points at, such as "Dakar22Teams/DT_TeamDataDK22" (relative to <see cref="Folder"/>),
    /// read from the asset's imports; null when it points elsewhere.
    /// </summary>
    public static string? HandleTable(this UAsset asset, PropertyData? handle)
    {
        const string Package = "/DakarGame/Data/DataTables/";
        if (handle is not StructPropertyData row || row.Get("DataTable") is not ObjectPropertyData { Value: { } index } || !index.IsImport())
        {
            return null;
        }

        var outer = index.ToImport(asset).OuterIndex;
        var path = outer.IsImport() ? outer.ToImport(asset).ObjectName?.ToString() : null;
        return path is not null && path.StartsWith(Package, StringComparison.Ordinal) ? path[Package.Length..] : null;
    }

    public static string Text(this PropertyData? p) => p switch
    {
        NamePropertyData n => n.Value?.ToString() ?? "",
        EnumPropertyData e => e.Value?.ToString() ?? "",
        StrPropertyData s => s.Value?.ToString() ?? "",
        SoftObjectPropertyData o => o.Value.AssetPath.AssetName?.ToString() is { } path && path != "None" ? path : "",
        _ => "",
    };

    public static int Int(this PropertyData? p) => p is IntPropertyData i ? i.Value : 0;

    public static float Float(this PropertyData? p) => p is FloatPropertyData f ? f.Value : 0;

    /// <summary>The structs of an array field, such as a team's members.</summary>
    public static IEnumerable<StructPropertyData> Items(this PropertyData? p) =>
        (p as ArrayPropertyData)?.Value.OfType<StructPropertyData>() ?? [];

    public static void SetInt(this StructPropertyData row, string field, int value)
    {
        if (row.Get(field) is IntPropertyData p)
        {
            p.Value = value;
        }
    }


    /// <summary>Sets a name field, or the RowName of a row handle when <paramref name="path"/> ends at the handle.</summary>
    public static void SetName(this StructPropertyData row, UAsset table, string value, params string[] path)
    {
        var field = row.Get(path) is StructPropertyData handle ? handle.Get("RowName") : row.Get(path);
        if (field is not NamePropertyData name)
        {
            throw new InvalidDataException($"No {string.Join(".", path)} field.");
        }

        // FromString splits "Abc_12" into "Abc" plus a number and adds "Abc" to the name map, as the engine expects.
        name.Value = FName.FromString(table, value);
    }
}
