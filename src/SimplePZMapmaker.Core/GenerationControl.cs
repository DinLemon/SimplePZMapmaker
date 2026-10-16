namespace SimplePZMapmaker.Core;

/// <summary>Persisted editor protections and the last accepted settlement run.</summary>
public sealed class GenerationControl
{
    public List<string> LockedBuildingIds { get; set; } = [];
    public List<GenerationArea> ProtectedAreas { get; set; } = [];
    public List<string> GeneratedBuildingIds { get; set; } = [];
    public SettlementRules? Rules { get; set; }
    public int? LastSeed { get; set; }
    public int LastVariant { get; set; }
    public string? LastMode { get; set; }

    public static void Validate(MapProject project)
    {
        if (project.Generation is not { } control) return;
        if (control.LockedBuildingIds is null || control.GeneratedBuildingIds is null || control.ProtectedAreas is null ||
            control.LockedBuildingIds.Count > 4000 || control.GeneratedBuildingIds.Count > 4000 || control.ProtectedAreas.Count > 128 ||
            !ValidIds(control.LockedBuildingIds) || !ValidIds(control.GeneratedBuildingIds) ||
            control.LastVariant is < 0 or > 10000 || control.LastMode is not (null or "fill" or "regenerate"))
            throw new ArgumentException("Invalid generation protections or run metadata.");
        foreach (var area in control.ProtectedAreas) GenerationArea.Validate(area, project);
        if (control.Rules is not null) SettlementRules.Validate(control.Rules);
    }

    static bool ValidIds(List<string> ids) => ids.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 100 && !id.Any(char.IsControl)) && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count;
}

public sealed class GenerationArea
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool Contains(int x, int y) => x >= X && y >= Y && (long)x < (long)X + Width && (long)y < (long)Y + Height;
    public bool Contains(Building building) => Contains(building.X, building.Y) && Contains(building.X + building.Width, building.Y + building.Height);
    public bool Intersects(Building building) => building.X <= (long)X + Width - 1 && building.Y <= (long)Y + Height - 1 && (long)building.X + building.Width >= X && (long)building.Y + building.Height >= Y;

    public static void Validate(GenerationArea? area, MapProject project)
    {
        if (area is null || area.X < 0 || area.Y < 0 || area.Width < 1 || area.Height < 1 || (long)area.X + area.Width > project.Width || (long)area.Y + area.Height > project.Height ||
            area.Id is { } id && (id.Length > 100 || id.Any(char.IsControl)) || area.Name is { } name && (name.Length > 100 || name.Any(char.IsControl)))
            throw new ArgumentException("Select a generation rectangle entirely inside the map.");
    }
}
