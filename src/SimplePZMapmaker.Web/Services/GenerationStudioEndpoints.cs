using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public static class GenerationStudioEndpoints
{
    public sealed record PreviewRequest(MapProject Project, GenerationArea Area, int Seed = 42, int Variant = 0, string Mode = "fill", SettlementRules? Rules = null, WorldProject? World = null, string? MemberId = null);
    public sealed record ApplyRequest(MapProject Project, string ProposalId, WorldProject? World = null, string? MemberId = null);
    public static void MapGenerationStudioEndpoints(this WebApplication app)
    {
        var proposals = new GenerationProposalStore();
        app.MapPost("/api/generation/studio/preview", (PreviewRequest request, GameAssetCatalog assets, StaticModelCatalog models) =>
        {
            ValidateMap(request.Project, assets, models);
            if (request.World is null && request.MemberId is not null) throw new ArgumentException("A section ID requires its world snapshot.");
            var context = request.World is null ? null : GenerationStudioWorldContext.Create(request.Project, request.World, request.MemberId);
            if (request.World is not null) WorldEndpoints.Validate(WorldAssembler.Clone(request.World), assets, models);
            var preview = GenerationStudio.Preview(request.Project, request.Area, request.Seed, request.Variant, request.Mode, request.Rules, context);
            ValidateMap(preview.Project, assets, models);
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
                WorldEndpoints.Validate(candidateWorld, assets, models);
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
        var copy = NeighborhoodPlanner.Copy(project);
        AssetRules.Project(copy, assets); models.Validate(copy);
    }
}
