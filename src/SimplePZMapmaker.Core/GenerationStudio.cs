using System.Security.Cryptography;
using System.Text.Json;

namespace SimplePZMapmaker.Core;

public sealed record GenerationStudioSummary(int AddedBuildings, int RemovedBuildings, int PreservedBuildings, int LockedBuildings, int ChangedTerrainTiles, IReadOnlyList<string> Warnings);
public sealed record GenerationBuildingProvenance(string BuildingId, string Footprint, string Levels, string Layout, bool Modified, bool Locked, string? SourceId);
public sealed record GenerationStudioPreview(string BaseFingerprint, MapProject Project, GenerationStudioSummary Summary, IReadOnlyList<GenerationBuildingProvenance> Provenance, string? WorldFingerprint = null, string? MemberId = null);

/// <summary>Pure selected-area completion: all edits happen on isolated snapshots.</summary>
public static class GenerationStudio
{
    public static string Fingerprint(MapProject project) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(project)));

    public static GenerationStudioPreview Preview(MapProject project, GenerationArea area, int seed, int variant = 0, string mode = "fill", SettlementRules? rules = null, GenerationStudioWorldContext? worldContext = null)
    {
        ProjectValidator.Validate(project);
        GenerationControl.Validate(project);
        GenerationArea.Validate(area, project);
        worldContext?.ValidateSource(project);
        if (variant is < 0 or > 10000 || mode is not ("fill" or "regenerate")) throw new ArgumentException("Generation needs fill/regenerate mode and a variant between 0 and 10,000.");
        var inheritedRules = rules ?? project.Generation?.Rules ?? new SettlementRules();
        SettlementRules.Validate(inheritedRules);
        if (rules is { Preset: not "contextual" }) throw new ArgumentException("Selective completion uses contextual district rules. Legacy random towns are available when creating a new map.");
        rules = Copy(inheritedRules);
        rules.Preset = "contextual";
        var fingerprint = Fingerprint(project);
        var candidate = Copy(project);
        var control = candidate.Generation ??= new();
        var locked = control.LockedBuildingIds.Concat(project.Sites.SelectMany(s=>s.BuildingIds)).ToHashSet(StringComparer.Ordinal);
        var reserved = Reserved(project, area);
        // Generated entrances can occupy the building's outer edge. Untouched pavement
        // must not lock its owner against regeneration; authored masks/resources still do.
        var removalReserved = mode == "regenerate" ? Reserved(project, area, preserveUneditedPavement: false) : reserved;
        if (worldContext is not null) for (var i = 0; i < reserved.Length; i++) if (worldContext.ReservedCells[i]) { reserved[i] = true; removalReserved[i] = true; }
        bool TouchesReserved(Building b) => BuildingCells(b, project.Width).Any(i => reserved[i]);
        var removed = mode == "regenerate" ? candidate.Buildings.Where(b => IsGenerated(b) && !locked.Contains(b.Id) && area.Contains(b) && !control.ProtectedAreas.Any(a => a.Intersects(b)) && !BuildingCells(b, project.Width).Any(i => removalReserved[i])).Select(b => b.Id).ToHashSet(StringComparer.Ordinal) : [];
        candidate.Buildings.RemoveAll(b => removed.Contains(b.Id));
        // Existing building footprints are protected even when a source outline contains courtyards.
        foreach (var building in candidate.Buildings) ReserveBuilding(reserved, building, project.Width);
        var terrainReserved = (bool[])reserved.Clone();
        var stage = Copy(candidate);
        if (worldContext is not null)
        {
            stage.Terrain = (Terrain[])worldContext.ProjectedProject.Terrain.Clone();
            stage.Decorations = Copy(worldContext.ProjectedProject.Decorations);
            if (worldContext.ProjectedProject.SharedInfrastructure is { } shared) stage.SharedInfrastructure = new([..shared.Tiles], [..shared.Holes]);
        }
        var existing = candidate.Buildings.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        // An isolated mask reserves sites without changing dry land under infrastructure.
        // Road tiles remain traversable so frontage can connect to a protected existing street.
        var stageMask = TerrainEditMask.Decode(stage);
        for (var i = 0; i < reserved.Length; i++) if (reserved[i] && stage.Terrain[i] != Terrain.Road) stageMask[i / 8] |= (byte)(1 << (i % 8));
        stage.TerrainEdits = TerrainEditMask.Encode(stageMask);
        var runSeed = variant == 0 ? seed : unchecked(seed * 16777619 ^ variant * 486187739);
        var population = SettlementGenerator.Populate(stage, Copy(rules), runSeed, 100);
        var added = new List<Building>();
        foreach (var building in stage.Buildings.Where(b => !existing.Contains(b.Id)))
        {
            if (!area.Contains(building) || control.ProtectedAreas.Any(a => a.Intersects(building)) || TouchesReserved(building) || candidate.Buildings.Any(b => b.Id == building.Id)) continue;
            if (!MapGenerator.CanPlace(candidate, building) || BuildingCells(building, project.Width).Any(i => project.Terrain[i] == Terrain.Water)) continue;
            candidate.Buildings.Add(Copy(building));
            added.Add(building);
            ReserveBuilding(reserved, building, project.Width);
        }
        var changed = 0;
        // Only unedited natural ground near an accepted building may become access or a court.
        for (var y = area.Y; y < area.Y + area.Height; y++) for (var x = area.X; x < area.X + area.Width; x++)
        {
            var index = y * project.Width + x;
            if (terrainReserved[index] || stage.Terrain[index] == project.Terrain[index] || stage.Terrain[index] is not (Terrain.Pavement or Terrain.Dirt) || project.Terrain[index] is not (Terrain.Grass or Terrain.Forest or Terrain.Dirt or Terrain.Sand)) continue;
            var reach = rules.Setback + 18;
            if (!added.Any(b => x >= b.X - reach && y >= b.Y - reach && x <= b.X + b.Width + reach && y <= b.Y + b.Height + reach)) continue;
            candidate.Terrain[index] = stage.Terrain[index]; changed++;
        }
        control.Rules = Copy(rules); control.LastSeed = seed; control.LastVariant = variant; control.LastMode = mode;
        control.GeneratedBuildingIds = candidate.Buildings.Where(IsGenerated).Select(b => b.Id).ToList();
        var warnings = population.Warnings.ToList();
        if (added.Count == 0) warnings.Add("No free road-adjacent sites fit the selected area and its protections. Existing source and authored content were preserved.");
        ProjectValidator.Validate(candidate); GenerationControl.Validate(candidate);
        worldContext?.ValidateCandidate(candidate);
        return new(fingerprint, candidate, new(added.Count, removed.Count, project.Buildings.Count - removed.Count, project.Buildings.Count(b => locked.Contains(b.Id)), changed, warnings.Distinct().ToArray()), Provenance(candidate), worldContext?.WorldFingerprint, worldContext?.MemberId);
    }

    public static IReadOnlyList<GenerationBuildingProvenance> Provenance(MapProject project)
    {
        var locks = (project.Generation?.LockedBuildingIds ?? []).ToHashSet(StringComparer.Ordinal);
        return project.Buildings.Select(b => new GenerationBuildingProvenance(b.Id,
            b.FootprintOrigin == "manual-source-edit" ? "source-edited" : b.SourceOutline.Count > 0 || b.FootprintOrigin == "source-grid" ? "osm-source" : b.SourceId is not null ? "source-approximated" : IsGenerated(b) ? "generated" : "authored",
            b.LevelsOrigin, b.Modified ? "authored" : b.SourceId is not null ? "inferred" : IsGenerated(b) ? "generated" : "authored", b.Modified, locks.Contains(b.Id), b.SourceId)).ToArray();
    }

    public static bool IsGenerated(Building b) => b.SourceId is null && b.SourceOutline.Count == 0 && !b.Modified && b.LevelsOrigin != "manual" && b.FootprintOrigin == "authored" &&
        (b.SourceKind == "settlement-generator" || b.Id.StartsWith("generated-", StringComparison.Ordinal) || b.Id.StartsWith("settlement-", StringComparison.Ordinal));

    static bool[] Reserved(MapProject project, GenerationArea selected, bool preserveUneditedPavement = true)
    {
        var reserved = new bool[project.Terrain.Length];
        foreach(var index in SiteModel.LockedCells(project))reserved[index]=true;
        var mask = TerrainEditMask.Decode(project);
        for (var y = 0; y < project.Height; y++) for (var x = 0; x < project.Width; x++)
        {
            var index = y * project.Width + x;
            reserved[index] = reserved[index] || !selected.Contains(x, y) || TerrainEditMask.IsEdited(mask, index) || project.SurfaceTiles.ContainsKey(index) || project.Terrain[index] is Terrain.Road or Terrain.Water || preserveUneditedPavement && project.Terrain[index] == Terrain.Pavement || (project.Generation?.ProtectedAreas.Any(a => a.Contains(x, y)) ?? false);
        }
        foreach (var d in project.Decorations) foreach (var part in d.Parts) reserved[(d.Y + part.Dy) * project.Width + d.X + part.Dx] = true;
        foreach (var m in project.StaticModels) foreach (var part in StaticModelPlacement.FootprintCells(m)) reserved[(m.Y + part.Dy) * project.Width + m.X + part.Dx] = true;
        foreach (var spawn in project.Spawns) reserved[spawn.Y * project.Width + spawn.X] = true;
        foreach (var zone in project.GameZones) for (var y = zone.Y; y < zone.Y + zone.Height; y++) for (var x = zone.X; x < zone.X + zone.Width; x++) reserved[y * project.Width + x] = true;
        foreach (var feature in project.LinearFeatures) foreach (var index in LinearFeatureEditor.Rasterize(feature, project.Width, project.Height)) reserved[index] = true;
        if (InfrastructureGeometry.HasFeatures(project))
        {
            var infrastructure = InfrastructureGeometry.Build(project);
            foreach (var tile in infrastructure.Tiles) reserved[tile.Y * project.Width + tile.X] = true;
            foreach (var hole in infrastructure.Holes) reserved[hole.Y * project.Width + hole.X] = true;
        }
        return reserved;
    }

    static IEnumerable<int> BuildingCells(Building b, int width)
    {
        for (var y = b.Y; y <= b.Y + b.Height; y++) for (var x = b.X; x <= b.X + b.Width; x++) yield return y * width + x;
    }
    static void ReserveBuilding(bool[] mask, Building b, int width) { foreach (var index in BuildingCells(b, width)) mask[index] = true; }
    static T Copy<T>(T value)
    {
        var copy = JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
        if (value is MapProject { SharedInfrastructure: { } infrastructure } && copy is MapProject map)
            map.SharedInfrastructure = new([..infrastructure.Tiles], [..infrastructure.Holes]);
        return copy;
    }
}
