using System.Text.Json;
namespace SimplePZMapmaker.Core.Export;
public static class NativeScene
{
 public sealed record Sprite(int X,int Y,int Z,string Tile);
 public sealed record Scene(int MinLevel,int MaxLevel,int Width,int Height,List<Sprite> Sprites);
 public static Scene DraftBuilding(Building original,int? level=null){
  var b=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(original,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;BuildingDesigner.Validate(b,false);b.X=8;b.Y=8;
  var p=MapGenerator.Generate(new(){Kind="blank"});p.Buildings.Add(b);return Collect(p,6,6,b.Width+6,b.Height+6,level,6,6,0,0,false);
 }
 public static Scene Building(Building original,int? level=null){
  var b=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(original,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;b.X=8;b.Y=8;var p=MapGenerator.Generate(new(){Kind="blank"});p.Buildings.Add(b);return Cell(p,6,6,b.Width+6,b.Height+6,level,6,6);
 }
 public static Scene Cell(MapProject p,int x,int y,int width,int height,int? level=null,int offsetX=0,int offsetY=0,int cellX=0,int cellY=0){
  return Collect(p,x,y,width,height,level,offsetX,offsetY,cellX,cellY,true);
 }
 private static Scene Collect(MapProject p,int x,int y,int width,int height,int? level,int offsetX,int offsetY,int cellX,int cellY,bool validateProject){
  if(cellX<0||cellY<0||cellX>=p.Width/256||cellY>=p.Height/256||x<0||y<0||width is <1 or >96||height is <1 or >96||x+width>256||y+height>256||level is < -16 or >31)throw new ArgumentException("Предпросмотр: участок до 96×96 в пределах одной ячейки.");
  if(p.Spawns.Count==0)p.Spawns.Add(MapGenerator.FindSafeSpawn(p));if(validateProject)ProjectValidator.Validate(p);var composer=new TileComposer(p,cellX,cellY);var min=level??composer.MinLevel;var max=level??composer.MaxLevel;var sprites=new List<Sprite>();
  for(var z=min;z<=max;z++)for(var yy=y;yy<y+height;yy++)for(var xx=x;xx<x+width;xx++)foreach(var tile in composer.At(xx,yy,z)){if(sprites.Count>=250000)throw new ArgumentException("Слишком много спрайтов: выберите один уровень или меньший участок.");sprites.Add(new(xx-offsetX,yy-offsetY,z,tile));}
  return new(min,max,width,height,sprites);
 }
}
