using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public static class GenerationStudioEndpoints
{
    public sealed record PreviewRequest(MapProject Project, GenerationArea Area, int Seed = 42, int Variant = 0, string Mode = "fill", SettlementRules? Rules = null, WorldProject? World = null, string? MemberId = null, string Strategy = "contextual", NeighborhoodConstraints? Neighborhood = null,string ParcelMaterial="wood",string FenceColor="green");
    public sealed record ApplyRequest(MapProject Project, string ProposalId, WorldProject? World = null, string? MemberId = null);
    public static void MapGenerationStudioEndpoints(this WebApplication app)
    {
        var proposals = new GenerationProposalStore();
        app.MapPost("/api/generation/studio/preview", (PreviewRequest request, GameAssetCatalog assets, StaticModelCatalog models) =>
        {
            ValidateMap(request.Project, assets, models);
            if (request.World is null && request.MemberId is not null) throw new ArgumentException("A section ID requires its world snapshot.");
            var context = request.World is null ? null : GenerationStudioWorldContext.Create(request.Project, request.World, request.MemberId);
            if (request.World is not null) GenerationResourceValidation.World(request.World, assets, models);
            ParcelRegionalResources? regional=null;
            if(request.Strategy=="parcels"&&assets.Ready){var catalog=assets.RegionalSprites();regional=new(TransferRegionalProfiles.SelectRuralAppearance(catalog,request.ParcelMaterial,request.Seed),TransferRegionalProfiles.SelectRuralPalette(catalog,request.FenceColor));}
            var preview = GenerationStudio.Preview(request.Project, request.Area, request.Seed, request.Variant, request.Mode, request.Rules, context, request.Strategy, request.Neighborhood,regional);
            if(assets.Ready){AssetRules.Project(preview.Project,assets);models.Validate(preview.Project);}else ValidateMap(preview.Project, assets, models);
            if (!assets.Ready) preview = preview with { Summary = preview.Summary with { Warnings = [..preview.Summary.Warnings, GenerationResourceValidation.OfflineNote] } };
            return proposals.Add(preview);
        });
        app.MapPost("/api/generation/studio/apply", (ApplyRequest request, GameAssetCatalog assets, StaticModelCatalog models) =>
        {
            var result = proposals.Apply(request.Project, request.ProposalId, request.World, request.MemberId);
            ValidateMap(result.Project, assets, models);
            if (request.World is not null)
            {
                var candidateWorld = WorldAssembler.Clone(request.World);
                candidateWorld.Maps.Single(m => m.Id == request.MemberId).Project = NeighborhoodPlanner.Copy(result.Project);
                GenerationResourceValidation.World(candidateWorld, assets, models);
            }
            return result;
        });
        app.MapPost("/api/generation/studio/provenance", (MapProject project) =>
        {
            ProjectValidator.Validate(project); GenerationControl.Validate(project);
            return GenerationStudio.Provenance(project);
        });
    }
    static void ValidateMap(MapProject project, GameAssetCatalog assets, StaticModelCatalog models)
    {
        GenerationResourceValidation.Map(project, assets, models);
    }
}
