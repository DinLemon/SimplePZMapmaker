using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.Text.Json;
namespace SimplePZMapmaker.Web.Services;
public static class FenceAuthoringEndpoints
{
 public sealed record Request(MapProject Project,FenceLine Line,FenceStyle? Style=null,GateStyle[]? GateStyles=null);
 public sealed record Result(MapProject Project,Dictionary<string,int> Skipped,int Pieces);
 public static void MapFenceAuthoringEndpoints(this WebApplication app)
 {
  app.MapGet("/api/fences/catalog",()=>new{styles=GeographicImporter.DefaultFenceStyles,wicket=GeographicImporter.DefaultWicket,gates=GeographicImporter.DefaultGateStyles,maximumPostSpacing=64,geometry="cardinal-tile-edges"});
  app.MapPost("/api/fences/put",(Request request,GameAssetCatalog assets)=>Build(request,assets));
 }
 public static MapProject Prepare(Request request)
 {
  if(request.Project is null||request.Line is null)throw new ArgumentException("An active section and fence line are required.");
  if(request.GateStyles?.Length>FenceLines.MaximumStyles)throw new ArgumentException("At most 128 gate styles may be proposed.");
  var candidate=NeighborhoodPlanner.Copy(request.Project);ProjectValidator.Validate(candidate);
  void Merge<T>(List<T> list,T value,Func<T,string> id){var previous=list.FirstOrDefault(item=>id(item)==id(value));if(previous is not null){if(JsonSerializer.Serialize(previous,NativeMapExporter.JsonOptions)!=JsonSerializer.Serialize(value,NativeMapExporter.JsonOptions))throw new ArgumentException("A different fence or gate style already owns ID "+id(value)+". Choose another ID; existing styles are not overwritten.");return;}list.Add(NeighborhoodPlanner.Copy(value));}
  var line=NeighborhoodPlanner.Copy(request.Line);var style=request.Style??GeographicImporter.DefaultFenceStyles.FirstOrDefault(s=>s.Id==line.Style);
  if(style is not null){if(style.Id!=line.Style)throw new ArgumentException("The selected fence style does not match the line.");Merge(candidate.FenceStyles,style,s=>s.Id);}
  foreach(var gate in request.GateStyles??[]){if(gate is null)throw new ArgumentException("A gate style is missing.");Merge(candidate.GateStyles,gate,g=>g.Id);}
  foreach(var reference in GeographicImporter.DefaultGateStyles)if(line.Gates?.Any(g=>g?.Style==reference.Id)==true&&candidate.GateStyles.All(g=>g.Id!=reference.Id))Merge(candidate.GateStyles,reference,g=>g.Id);
  candidate.Fences.RemoveAll(f=>f.Id==line.Id);candidate.Fences.Add(line);candidate.Version=Math.Max(candidate.Version,12);FenceLines.Validate(candidate);
  if(line.Points.Any(p=>p.X<0||p.Y<0||p.X>candidate.Width||p.Y>candidate.Height))throw new ArgumentException("Draw the fence on tile edges inside the active section. Shared world fences are edited in their authoritative world.");
  for(var i=1;i<line.Points.Count;i++){var a=line.Points[i-1];var b=line.Points[i];if(a.X==b.X&&a.X==candidate.Width||a.Y==b.Y&&a.Y==candidate.Height)throw new ArgumentException("East and south boundary edges belong to the adjacent section. Draw inside this section, or use a shared world fence.");}
  ProjectValidator.Validate(candidate);return candidate;
 }
 public static Result Build(Request request,GameAssetCatalog assets)
 {
  var candidate=Prepare(request);StampFenceResources(candidate,assets.Get);AssetRules.Project(candidate,assets);StampFenceResources(candidate,assets.Get);ResourceDependencies.Validate(candidate.RequiredMods,candidate.AssetSources);
  var composition=FenceLines.Compose(candidate.Fences,candidate.FenceStyles,candidate.GateStyles,candidate.Width,candidate.Height,blocked:FenceLines.Blocker(candidate));return new(candidate,composition.Skipped,composition.Pieces.Count);
 }
 public static void StampFenceResources(MapProject project,Func<string,GameAssetCatalog.Asset> lookup)
 {
  var usedStyles=project.Fences.Select(f=>f.Style).ToHashSet(StringComparer.Ordinal);var usedGates=project.Fences.SelectMany(f=>f.Gates).Select(g=>g.Style).ToHashSet(StringComparer.Ordinal);
  var tiles=project.FenceStyles.Where(s=>usedStyles.Contains(s.Id)).SelectMany(s=>new[]{s.W,s.N,s.NW,s.Post,s.WAlt,s.NAlt}.OfType<string>()).Concat(project.GateStyles.Where(g=>usedGates.Contains(g.Id)).SelectMany(g=>g.ClosedW.Concat(g.ClosedN).Concat(g.OpenW).Concat(g.OpenN))).Distinct(StringComparer.Ordinal);
  var sources=new Dictionary<string,string>(project.AssetSources,StringComparer.Ordinal);var required=new HashSet<string>(project.RequiredMods,StringComparer.Ordinal);
  foreach(var tile in tiles){var asset=lookup(tile);var provider=asset.ModId??"game";if(sources.TryGetValue(tile,out var original)&&original!=provider)throw new ArgumentException($"Fence tile {tile} has a different source: expected {original}, active {provider}. Enable the original mod.");sources[tile]=provider;foreach(var mod in asset.Requirements??[])required.Add(mod);if(asset.ModId is {} id)required.Add(id);}
  ResourceDependencies.Validate(required.ToList(),sources);project.AssetSources=sources;project.RequiredMods=required.Order(StringComparer.Ordinal).ToList();
 }
}
