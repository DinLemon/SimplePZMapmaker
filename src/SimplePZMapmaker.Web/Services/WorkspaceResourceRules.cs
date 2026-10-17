using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

/// <summary>Preserves authoritative shared-fence owners across local dependency normalization.</summary>
public static class WorkspaceResourceRules
{
 public sealed record Prepared(MapProject Project,MapProject Projected,WorldSectionArchive.Metadata Metadata);
 static HashSet<string> FenceTiles(WorldSectionArchive.Metadata metadata)=>metadata.FenceStyles.SelectMany(s=>new[]{s.W,s.N,s.NW,s.Post,s.WAlt,s.NAlt}.OfType<string>()).Concat(metadata.GateStyles.SelectMany(s=>s.ClosedW.Concat(s.ClosedN).Concat(s.OpenW).Concat(s.OpenN))).ToHashSet(StringComparer.Ordinal);
 static void Retain(MapProject project,HashSet<string> tiles,IReadOnlyDictionary<string,string> owners){
  foreach(var pair in owners.Where(pair=>tiles.Contains(pair.Key))){if(project.AssetSources.TryGetValue(pair.Key,out var supplied)&&supplied!=pair.Value)throw new ArgumentException($"Tile {pair.Key} has a different saved source: expected {pair.Value}, supplied {supplied}. Enable the original mod.");project.AssetSources[pair.Key]=pair.Value;}
  project.RequiredMods=project.RequiredMods.Concat(project.AssetSources.Where(pair=>tiles.Contains(pair.Key)&&pair.Value!="game").Select(pair=>pair.Value)).Distinct(StringComparer.Ordinal).ToList();if(project.RequiredMods.Count>0||project.AssetSources.Count>0)project.Version=Math.Max(project.Version,6);ResourceDependencies.Validate(project.RequiredMods,project.AssetSources);
 }
 public static async Task<Prepared> PrepareAsync(WorldSectionArchive.RevisionLease lease,string memberId,MapProject? proposed,GameAssetCatalog assets,StaticModelCatalog? models=null,CancellationToken token=default){
  var original=await lease.ReadSectionAsync(memberId,token);var project=proposed??original;ProjectValidator.Validate(project);var metadata=await lease.ReadMetadataAsync(token);var tiles=FenceTiles(metadata);var saved=new Dictionary<string,string>(original.AssetSources,StringComparer.Ordinal);
  Retain(project,tiles,saved);var retained=project.AssetSources.Where(pair=>tiles.Contains(pair.Key)).ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);AssetRules.Project(project,assets);Retain(project,tiles,retained);models?.Validate(project);
  var source=new WorkspaceExportSource(lease,metadata,assets,models);var projected=await source.ProjectSectionAsync(memberId,project,token);WorkspaceExportSource.ValidateFenceResources(projected,assets,saved);Retain(project,tiles,projected.AssetSources);return new(project,projected,metadata);
 }
}
