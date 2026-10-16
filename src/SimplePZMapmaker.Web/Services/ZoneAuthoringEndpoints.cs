using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class ZoneAuthoringEndpoints
{
 public sealed record Request(MapProject Project,GameZone[]? Zones=null,ZombieDensityRegion[]? DensityRegions=null,RoomRegionRule[]? RoomRules=null,RoomRegionChange[]? ReviewedRooms=null);
 public sealed record Result(MapProject Project,RoomRegionChange[] Rooms,DensityRasterChange[] Density,string[] Notes);
 public static void MapZoneAuthoringEndpoints(this WebApplication app)
 {
  app.MapGet("/api/zones/catalog",()=>new{roomNames=BuildingDesigner.RoomNames.Select(name=>new{id=name,sourceId="game",nativeName=name switch{"hotelroom"=>"motelroom","garage"=>"garagestorage","balcony"=>"emptyoutside",_=>name},requiredMods=Array.Empty<string>()}),selectionRules=new[]{"intersects","contained"},polygonCoverage="tile-centres",densityRasterSize=8});
  app.MapPost("/api/zones/preview",(Request request,GameplayCatalog catalog,CancellationToken token)=>Build(request,catalog,false,token));
  app.MapPost("/api/zones/apply",(Request request,GameplayCatalog catalog,CancellationToken token)=>Build(request,catalog,true,token));
 }
 public static Result Build(Request request,GameplayCatalog catalog,bool apply,CancellationToken cancellation=default)
 {
  if(request.Project is null)throw new ArgumentException("An active section is required.");cancellation.ThrowIfCancellationRequested();ProjectValidator.Validate(request.Project);var candidate=NeighborhoodPlanner.Copy(request.Project);
  if(request.Zones is not null){candidate.GameZones=NeighborhoodPlanner.Copy(request.Zones.ToList());GameZones.ValidateProject(candidate);catalog.Validate(candidate);}
  if(request.DensityRegions is not null)candidate=ZombieDensityRegions.ApplyProposal(candidate,request.DensityRegions,cancellation);
  var rules=request.RoomRules;var rooms=rules is null?Array.Empty<RoomRegionChange>():RoomRegionRules.Preview(candidate,rules);
  if(rules is not null)candidate=RoomRegionRules.ApplyProposal(candidate,rules,apply?request.ReviewedRooms??[]:rooms);
  cancellation.ThrowIfCancellationRequested();ProjectValidator.Validate(candidate);
  return new(candidate,rooms,request.DensityRegions is null?[]:ZombieDensityRegions.Preview(request.Project,candidate),["Polygon coverage uses tile centres and exports as deterministic, non-overlapping native rectangles.","Zombie density is rounded to the 8×8 native raster. Later manual edits retain priority until all density regions are removed.","Room mappings rename entire existing rooms. Native room/distribution behavior is provided by the game; this does not create a loot-zone type."]);
 }
}
