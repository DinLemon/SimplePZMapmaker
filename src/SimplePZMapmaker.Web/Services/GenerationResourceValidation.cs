using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

/// <summary>Structural authoring works offline; resource physics need a connected catalog.</summary>
public static class GenerationResourceValidation
{
    public const string OfflineNote = "Game resource catalog unavailable: structural geometry was checked, but native sprite materials and physical properties need a connected game catalog before final verification.";

    public static void Map(MapProject project, GameAssetCatalog assets, StaticModelCatalog models)
    {
        var copy = NeighborhoodPlanner.Copy(project);
        if (assets.Ready) AssetRules.Project(copy, assets);
        else
        {
            ProjectValidator.Validate(copy);
            RequireBaseResources(copy.RequiredMods, copy.AssetSources);
            foreach (var building in copy.Buildings.Concat(copy.BuildingLibrary)) RequireBaseResources(building.RequiredMods, building.AssetSources);
        }
        models.Validate(copy);
    }

    public static void World(WorldProject project, GameAssetCatalog assets, StaticModelCatalog models)
    {
        var copy = WorldAssembler.Clone(project);
        if (assets.Ready) { WorldEndpoints.Validate(copy, assets, models); return; }
        WorldAssembler.Validate(copy);
        foreach (var member in copy.Maps) Map(member.Project, assets, models);
        foreach (var building in copy.Buildings)
        {
            ResourceDependencies.Validate(building.RequiredMods, building.AssetSources);
            RequireBaseResources(building.RequiredMods, building.AssetSources);
            ModelRules.Building(building, models);
        }
        AssetRules.WorldInfrastructure(copy, assets);
    }

    static void RequireBaseResources(IReadOnlyList<string> required, IReadOnlyDictionary<string, string> sources)
    {
        if (required.Count > 0 || sources.Values.Any(source => source != "game"))
            throw new ArgumentException("Connect the game and required mod catalogs before generating with mod resources. Saved provider declarations are retained.");
    }
}
