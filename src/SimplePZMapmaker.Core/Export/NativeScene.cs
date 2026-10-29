using System.Text.Json;
namespace SimplePZMapmaker.Core.Export;
public static class NativeScene
{
 public sealed record Sprite(int X,int Y,int Z,string Tile);
 public sealed record Scene(int MinLevel,int MaxLevel,int Width,int Height,List<Sprite> Sprites);
 public static HashSet<string> ResourceIds(MapProject project){var ids=new HashSet<string>(StringComparer.Ordinal);for(var y=0;y<project.Height/256;y++)for(var x=0;x<project.Width/256;x++)ids.UnionWith(new TileComposer(project,x,y).ResourceTiles());if(StaticModelExporter.Gather(project).Any(p=>p.Placement.Profile!="pickup"))ids.Add("invisible_01_0");return ids;}
 // Separate from Cell's 250,000-sprite cap: allow the full floor budget, weather cover,
 // and overlays. The one aggregate limit applies across every cell and level.
 private const int MaximumBuildingPreviewSprites=3*BuildingDesigner.MaximumFloorCells;
 private const int BuildingOrigin=8,BuildingPreviewOrigin=6,BuildingPreviewPadding=6;
 public static HashSet<string> BuildingResourceIds(Building original){
  var p=BuildingProject(original,false);var b=p.Buildings[0];var ids=new HashSet<string>(StringComparer.Ordinal);
  // Include complete authored cells, even resources outside the displayed tile area.
  foreach(var (composer,_,_) in BuildingComposers(p))ids.UnionWith(composer.ResourceTiles(true));
  if(b.Design?.Floors.Any(f=>f.StaticModels.Any(m=>m.Profile!="pickup"))==true)ids.Add("invisible_01_0");return ids;
 }
 public static Scene DraftBuilding(Building original,int? level=null)=>CollectBuilding(original,level,false);
 public static Scene Building(Building original,int? level=null)=>CollectBuilding(original,level,true);
 private static MapProject BuildingProject(Building original,bool requireAccess){
  ArgumentNullException.ThrowIfNull(original);
  // Reject unbounded inputs before cloning, allocating terrain, or composing native tiles.
  if(original.Width is <BuildingDesigner.MinimumFootprint or >BuildingDesigner.MaximumFootprint||original.Height is <BuildingDesigner.MinimumFootprint or >BuildingDesigner.MaximumFootprint)throw new ArgumentException("Check the building dimensions (2-512 tiles per side).");
  if((long)original.Width*original.Height*(original.Design?.Floors?.Count??1)>BuildingDesigner.MaximumFloorCells)throw new ArgumentException("Building floor-cell budget exceeds 262144 tiles.");
  var b=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(original,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;b.X=BuildingOrigin;b.Y=BuildingOrigin;
  BuildingDesigner.Validate(b,false);
  var width=((BuildingPreviewOrigin+b.Width+BuildingPreviewPadding+255)/256)*256;
  var height=((BuildingPreviewOrigin+b.Height+BuildingPreviewPadding+255)/256)*256;
  // A bounded blank rectangle suffices; a generated map's central spawn may lie inside b.
  var p=new MapProject{Version=6,Width=width,Height=height,Terrain=new Terrain[width*height],Zombies=new int[width*height/64],Buildings=[b],Spawns=[new(){X=0,Y=0}]};
  if(requireAccess)ProjectValidator.Validate(p);return p;
 }
 private static IEnumerable<(TileComposer Composer,int X,int Y)> BuildingComposers(MapProject p){
  // Keep only one native cell's composed layers alive at a time.
  for(var y=0;y<p.Height/256;y++)for(var x=0;x<p.Width/256;x++)yield return(new TileComposer(p,x,y),x*256,y*256);
 }
 private static Scene CollectBuilding(Building original,int? level,bool requireAccess){
  if(level is <ZLevels.Min or >ZLevels.Max)throw new ArgumentException("Preview level must be within z=-32 to 31.");
  var p=BuildingProject(original,requireAccess);var b=p.Buildings[0];
  var width=b.Width+BuildingPreviewPadding;var height=b.Height+BuildingPreviewPadding;
  var min=level??Math.Min(0,b.Design?.MinLevel??0);var max=level??Math.Max(1,b.Design is null?1:RoofGeometry.MaxLevel(b));var sprites=new List<Sprite>();
  foreach(var (composer,ox,oy) in BuildingComposers(p)){
   var left=Math.Max(BuildingPreviewOrigin,ox);var top=Math.Max(BuildingPreviewOrigin,oy);
   var right=Math.Min(BuildingPreviewOrigin+width,ox+256);var bottom=Math.Min(BuildingPreviewOrigin+height,oy+256);
   for(var z=min;z<=max;z++)for(var y=top;y<bottom;y++)for(var x=left;x<right;x++)foreach(var tile in composer.At(x-ox,y-oy,z)){
    if(sprites.Count>=MaximumBuildingPreviewSprites)throw new ArgumentException("Too many sprites: select one level or a smaller building.");
    sprites.Add(new(x-BuildingPreviewOrigin,y-BuildingPreviewOrigin,z,tile));
   }
  }
  return new(min,max,width,height,sprites);
 }
 public static Scene Cell(MapProject p,int x,int y,int width,int height,int? level=null,int offsetX=0,int offsetY=0,int cellX=0,int cellY=0){
  return Collect(p,x,y,width,height,level,offsetX,offsetY,cellX,cellY,true);
 }
 public static Scene WorldCell(WorldProject world,string memberId,int x,int y,int? level=0,int cellX=0,int cellY=0){WorldAssembler.Validate(world);var member=world.Maps.SingleOrDefault(m=>m.Id==memberId)??throw new ArgumentException("World section not found.");return Collect(WorldAssembler.ProjectSection(world,member),x,y,48,48,level,x,y,cellX,cellY,false);}
 private static Scene Collect(MapProject p,int x,int y,int width,int height,int? level,int offsetX,int offsetY,int cellX,int cellY,bool validateProject){
  if(cellX<0||cellY<0||cellX>=p.Width/256||cellY>=p.Height/256||x<0||y<0||width is <1 or >96||height is <1 or >96||x+width>256||y+height>256||level is < ZLevels.Min or > ZLevels.Max)throw new ArgumentException("Preview: up to 96 x 96 tiles inside one cell.");
  if(p.Spawns.Count==0&&!p.WorldSection)p.Spawns.Add(MapGenerator.FindSafeSpawn(p));if(validateProject)ProjectValidator.Validate(p);var composer=new TileComposer(p,cellX,cellY);var min=level??composer.MinLevel;var max=level??composer.MaxLevel;var sprites=new List<Sprite>();
  for(var z=min;z<=max;z++)for(var yy=y;yy<y+height;yy++)for(var xx=x;xx<x+width;xx++)foreach(var tile in composer.At(xx,yy,z)){if(sprites.Count>=250000)throw new ArgumentException("Too many sprites: select one level or a smaller area.");sprites.Add(new(xx-offsetX,yy-offsetY,z,tile));}
  return new(min,max,width,height,sprites);
 }
}
