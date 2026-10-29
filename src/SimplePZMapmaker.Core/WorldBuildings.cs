using System.Text.Json;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Core;
public static partial class WorldAssembler
{
 public static WorldMapMember? MemberAt(WorldProject world,int x,int y)=>world.Maps.FirstOrDefault(m=>x>=m.Project.WorldX*256&&y>=m.Project.WorldY*256&&x<m.Project.WorldX*256+m.Project.Width&&y<m.Project.WorldY*256+m.Project.Height);
 public static IEnumerable<(int X,int Y)> BuildingCells(Building b)=>BuildingDesigner.Occupied(b).Select(i=>(b.X+i%(b.Width+1),b.Y+i/(b.Width+1)));
 private static void ValidateBuildings(WorldProject world,Building? addedBuilding=null){
  if(world.Buildings is null||world.Buildings.Count>4000)throw new ArgumentException("A world can contain up to 4,000 shared buildings.");
  if(world.Buildings.Sum(b=>(long)(b?.Design?.Floors?.Sum(f=>f?.Cells?.Length??0)??0))>20_000_000)throw new ArgumentException("Shared buildings exceed 20 million floor tiles.");
  var occupied=new HashSet<(int,int)>();var ids=new HashSet<string>(StringComparer.Ordinal);
  foreach(var member in world.Maps)foreach(var b in member.Project.Buildings){ids.Add(b.Id);foreach(var point in BuildingCells(b))occupied.Add((point.X+member.Project.WorldX*256,point.Y+member.Project.WorldY*256));}
  if(addedBuilding is not null){if(!world.Buildings.Contains(addedBuilding))throw new ArgumentException("The added building is not in this world.");foreach(var prior in world.Buildings.Where(b=>!ReferenceEquals(b,addedBuilding))){ids.Add(prior.Id);foreach(var point in BuildingCells(prior))occupied.Add(point);}}
  foreach(var b in addedBuilding is null?world.Buildings:[addedBuilding]){
   if(b is null||string.IsNullOrWhiteSpace(b.Id)||b.Id.Length>100||!ids.Add(b.Id)||b.X<0||b.Y<0||b.X>258000||b.Y>258000||b.Name is null||b.Name.Length>100||b.SourceId?.Length>100||b.SourceKind?.Length>100||b.SourceLevels?.Length>100||b.SourceBasements?.Length>100||b.SourceRoof?.Length>100||b.LevelsOrigin is not("inferred" or "osm-levels" or "osm-height" or "manual"))throw new ArgumentException("Invalid shared world building or duplicate ID.");
   if(b.AttachedTo is not null)throw new ArgumentException($"'{b.Name}': shared world buildings cannot be attached; attach buildings inside one section.");
   BuildingDesigner.Validate(b);
   foreach(var point in BuildingCells(b)){
    var member=MemberAt(world,point.X,point.Y)??throw new ArgumentException($"'{b.Name}': the building extends into an empty part of the world ({point.X},{point.Y}). Add an adjacent section.");var p=member.Project;var x=point.X-p.WorldX*256;var y=point.Y-p.WorldY*256;
    if(!occupied.Add(point))throw new ArgumentException($"'{b.Name}': walls or rooms overlap another building.");
    if(BuildingDesigner.Occupies(b,point.X-b.X,point.Y-b.Y)&&p.Terrain[y*p.Width+x]==Terrain.Water)throw new ArgumentException($"'{b.Name}': part of the building stands on water.");
    if(p.Spawns.Any(s=>s.X==x&&s.Y==y)||p.StaticModels.Any(s=>StaticModelPlacement.FootprintCells(s).Any(c=>s.X+c.Dx==x&&s.Y+c.Dy==y))||p.Decorations.Any(d=>d.Parts.Any(part=>d.X+part.Dx==x&&d.Y+part.Dy==y)))throw new ArgumentException($"'{b.Name}': the building overlaps a spawn point or an environment object.");
    if(BuildingDesigner.OccupiesAbove(b,point.X-b.X,point.Y-b.Y)&&p.GameZones.Any(z=>z.Vehicle&&ZoneGeometry.Covers(z,x,y)))throw new ArgumentException($"'{b.Name}': a vehicle zone overlaps a shared building.");
   }
  }
 }
 public static Building CloneBuilding(Building b)=>JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(b,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;
 public static MapProject ProjectSection(WorldProject world,WorldMapMember member,InfrastructureGeometry.Geometry? sharedGeometry=null){
  var p=member.Project;var projected=world.Buildings.Where(b=>b.X<p.WorldX*256+p.Width&&b.Y<p.WorldY*256+p.Height&&b.X+b.Width>=p.WorldX*256&&b.Y+b.Height>=p.WorldY*256).Select(b=>{var copy=CloneBuilding(b);copy.X-=p.WorldX*256;copy.Y-=p.WorldY*256;return copy;}).ToArray();
  var decorations=DecorationsFor(world.Decorations,p);
  if(projected.Length==0&&world.LinearFeatures.Count==0&&world.Fences.Count==0&&decorations.Count==0)return p;
  var result=new MapProject(){Version=Math.Max(6,p.Version),WorldSection=true,Name=p.Name,Width=p.Width,Height=p.Height,Seed=p.Seed,WorldX=p.WorldX,WorldY=p.WorldY,Terrain=p.Terrain,TerrainEdits=p.TerrainEdits,Zombies=p.Zombies,Buildings=[..p.Buildings,..projected],BuildingLibrary=p.BuildingLibrary,ResourcePalettes=p.ResourcePalettes,LinearFeatures=p.LinearFeatures,GameZones=p.GameZones,SurfaceTheme=p.SurfaceTheme,SurfaceRegions=p.SurfaceRegions,SurfaceTiles=p.SurfaceTiles,Decorations=decorations.Count==0?p.Decorations:[..p.Decorations,..decorations],StaticModels=p.StaticModels,RequiredMods=p.RequiredMods,AssetSources=p.AssetSources,Spawns=p.Spawns,Attribution=p.Attribution,Bounds=p.Bounds,MetresPerTile=p.MetresPerTile,ImportSummary=p.ImportSummary};
  // Shared decorations bring their tile providers into the section that draws them.
  var shared=world.Decorations.Where(d=>decorations.Any(local=>local.Id==d.Id)).ToList();if(shared.Any(d=>d.AssetSources.Count>0||d.RequiredMods.Count>0)){result.AssetSources=new(p.AssetSources);foreach(var (tile,provider) in shared.SelectMany(d=>d.AssetSources)){if(result.AssetSources.TryGetValue(tile,out var known)&&known!=provider)throw new ArgumentException($"Tile {tile} has different providers in a shared decoration and its section.");result.AssetSources[tile]=provider;}result.RequiredMods=p.RequiredMods.Concat(shared.SelectMany(d=>d.RequiredMods)).Distinct(StringComparer.Ordinal).ToList();}
  result.ZombieDensityRegions=NeighborhoodPlanner.Copy(p.ZombieDensityRegions);result.ZombieDensityState=NeighborhoodPlanner.Copy(p.ZombieDensityState);result.RoomRegionRules=NeighborhoodPlanner.Copy(p.RoomRegionRules);
  result.Fences=[..p.Fences,..FenceLines.ForSection(world,p)];result.FenceStyles=[..p.FenceStyles,..world.FenceStyles.Where(w=>p.FenceStyles.All(s=>s.Id!=w.Id))];result.GateStyles=[..p.GateStyles,..world.GateStyles.Where(w=>p.GateStyles.All(s=>s.Id!=w.Id))];
  result.SourceFeatures=NeighborhoodPlanner.Copy(p.SourceFeatures);result.Sites=NeighborhoodPlanner.Copy(p.Sites);
  result.Generation=p.Generation is null?null:JsonSerializer.Deserialize<GenerationControl>(JsonSerializer.Serialize(p.Generation));
  return WorldInfrastructure.Project(world,member,result,sharedGeometry);
 }
 public static IEnumerable<StaticModelExporter.PlacedStaticModel> GatherModels(WorldProject world)=>StaticModelExporter.Gather(new(){WorldX=0,WorldY=0,Buildings=world.Buildings});
 public static WorldProject PutBuilding(WorldProject world,Building building){var copy=Clone(world);var prior=copy.Buildings.FindIndex(b=>b.Id==building.Id);if(prior>=0)copy.Buildings[prior]=CloneBuilding(building);else copy.Buildings.Add(CloneBuilding(building));Validate(copy);return copy;}
 public static WorldProject PromoteBuilding(WorldProject world,string memberId,string buildingId,int x,int y){var copy=Clone(world);var member=copy.Maps.SingleOrDefault(m=>m.Id==memberId)??throw new ArgumentException("Section not found.");var b=member.Project.Buildings.SingleOrDefault(b=>b.Id==buildingId)??throw new ArgumentException("Building not found.");member.Project.Buildings.Remove(b);SiteModel.DetachMissingMembers(member.Project);b.X=x;b.Y=y;copy.Buildings.Add(b);Validate(copy);return copy;}
}
