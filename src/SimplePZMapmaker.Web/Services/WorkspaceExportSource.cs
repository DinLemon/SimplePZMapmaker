using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

/// <summary>Adapts an immutable workspace lease; resource validation runs on the same revision as the writer.</summary>
internal sealed class WorkspaceExportSource(WorldSectionArchive.RevisionLease lease,WorldSectionArchive.Metadata metadata,GameAssetCatalog? assets,StaticModelCatalog? models):IStreamedWorldExportSource
{
 public WorldProject Metadata {get;}=new(){Version=metadata.Version,Id=metadata.Id,Name=metadata.Name,Buildings=metadata.Buildings,LinearFeatures=metadata.LinearFeatures,GeoReference=metadata.GeoReference,Elevation=metadata.Elevation};
 public IReadOnlyList<StreamedWorldSection> Sections {get;}=lease.PinnedManifest.Sections.Select(s=>new StreamedWorldSection(s.Id,s.Name,s.WorldX,s.WorldY,s.Width,s.Height)).ToArray();
 public object AuthoringIndex=>lease.PinnedManifest;
 public async Task<MapProject> ReadSectionAsync(string id,CancellationToken token){var p=await lease.ReadSectionAsync(id,token);if(assets is not null)AssetRules.Project(p,assets);models?.Validate(p);return p;}
 public async Task<MapProject> ProjectSectionAsync(string id,MapProject authored,CancellationToken token){var shared=WorldSectionProjection.Context(metadata,authored);foreach(var building in shared.Buildings){if(assets is not null)AssetRules.Building(building,assets);if(models is not null)ModelRules.Building(building,models);}var p=await lease.ProjectSectionAsync(id,authored,metadata,token);if(assets?.Ready==true){foreach(var tile in (p.SharedInfrastructure?.Tiles.Select(t=>t.Id)??[]).Concat(p.Decorations.Where(d=>d.Id.StartsWith("world-",StringComparison.Ordinal)).SelectMany(d=>d.Parts.Select(part=>part.Tile))).Distinct())if(assets.Get(tile).Source!="game")throw new ArgumentException("Shared infrastructure requires the original game resource provider: "+tile);}return p;}
 public Task CopySectionSourceAsync(string id,Stream target,CancellationToken token)=>lease.CopySectionSourceAsync(id,target,token);
 public Task CopyMetadataSourceAsync(Stream target,CancellationToken token)=>lease.CopyMetadataSourceAsync(target,token);
}
