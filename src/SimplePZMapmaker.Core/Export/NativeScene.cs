using System.Text.Json;
namespace SimplePZMapmaker.Core.Export;
public static class NativeScene
{
 public sealed record Sprite(int X,int Y,int Z,string Tile);
 public sealed record Scene(int MinLevel,int MaxLevel,int Width,int Height,List<Sprite> Sprites);
 public static HashSet<string> ResourceIds(MapProject project){var ids=new HashSet<string>(StringComparer.Ordinal);for(var y=0;y<project.Height/256;y++)for(var x=0;x<project.Width/256;x++)ids.UnionWith(new TileComposer(project,x,y).ResourceTiles());if(StaticModelExporter.Gather(project).Any(p=>p.Placement.Profile!="pickup"))ids.Add("invisible_01_0");return ids;}
 public static HashSet<string> BuildingResourceIds(Building original){var b=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(original,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;b.X=8;b.Y=8;BuildingDesigner.Validate(b,false);var p=MapGenerator.Generate(new(){Kind="blank"});p.Buildings.Add(b);var ids=new TileComposer(p,0,0).ResourceTiles(true);if(b.Design?.Floors.Any(f=>f.StaticModels.Any(m=>m.Profile!="pickup"))==true)ids.Add("invisible_01_0");return ids;}
 public static Scene DraftBuilding(Building original,int? level=null){
  var b=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(original,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;BuildingDesigner.Validate(b,false);b.X=8;b.Y=8;
  var p=MapGenerator.Generate(new(){Kind="blank"});p.Buildings.Add(b);return Collect(p,6,6,b.Width+6,b.Height+6,level,6,6,0,0,false);
 }
 public static Scene Building(Building original,int? level=null){
  var b=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(original,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;b.X=8;b.Y=8;var p=MapGenerator.Generate(new(){Kind="blank"});p.Version=6;p.Buildings.Add(b);return Cell(p,6,6,b.Width+6,b.Height+6,level,6,6);
 }
 public static Scene Cell(MapProject p,int x,int y,int width,int height,int? level=null,int offsetX=0,int offsetY=0,int cellX=0,int cellY=0){
  return Collect(p,x,y,width,height,level,offsetX,offsetY,cellX,cellY,true);
 }
 public static Scene WorldCell(WorldProject world,string memberId,int x,int y,int? level=0,int cellX=0,int cellY=0){WorldAssembler.Validate(world);var member=world.Maps.SingleOrDefault(m=>m.Id==memberId)??throw new ArgumentException("World section not found.");return Collect(WorldAssembler.ProjectSection(world,member),x,y,48,48,level,x,y,cellX,cellY,false);}
 private static Scene Collect(MapProject p,int x,int y,int width,int height,int? level,int offsetX,int offsetY,int cellX,int cellY,bool validateProject){
  if(cellX<0||cellY<0||cellX>=p.Width/256||cellY>=p.Height/256||x<0||y<0||width is <1 or >96||height is <1 or >96||x+width>256||y+height>256||level is < -16 or >31)throw new ArgumentException("Preview: up to 96 x 96 tiles inside one cell.");
  if(p.Spawns.Count==0&&!p.WorldSection)p.Spawns.Add(MapGenerator.FindSafeSpawn(p));if(validateProject)ProjectValidator.Validate(p);var composer=new TileComposer(p,cellX,cellY);var min=level??composer.MinLevel;var max=level??composer.MaxLevel;var sprites=new List<Sprite>();
  for(var z=min;z<=max;z++)for(var yy=y;yy<y+height;yy++)for(var xx=x;xx<x+width;xx++)foreach(var tile in composer.At(xx,yy,z)){if(sprites.Count>=250000)throw new ArgumentException("Too many sprites: select one level or a smaller area.");sprites.Add(new(xx-offsetX,yy-offsetY,z,tile));}
  return new(min,max,width,height,sprites);
 }
}
