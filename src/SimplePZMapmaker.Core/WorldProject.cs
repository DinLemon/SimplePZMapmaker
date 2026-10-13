using System.Text.Json;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Core;

public sealed class WorldProject
{
 public string Format {get;set;}="SimplePZWorld";
 public int Version {get;set;}=1;
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public string Name {get;set;}="Новый мир";
 public List<WorldMapMember> Maps {get;set;}=[];
 public WorldGeoReference? GeoReference {get;set;}
}
public sealed class WorldMapMember
{
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public MapProject Project {get;set;}=new();
}
public static class WorldAssembler
{
 public sealed record Bounds(int X,int Y,int Width,int Height,int Cells);
 public static Bounds Validate(WorldProject world){
  if(world is null||world.Format!="SimplePZWorld"||world.Version!=1||!SafeId(world.Id)||string.IsNullOrWhiteSpace(world.Name)||world.Name.Length>80||world.Name.Any(char.IsControl)||world.Maps is null||world.Maps.Count is <1 or >128)throw new ArgumentException("Проект мира повреждён: требуется1–128 карт.");
  if(world.GeoReference is {} geographic)WorldGeoReference.Validate(geographic);
  var ids=new HashSet<string>();var cells=new HashSet<(int,int)>();var minX=int.MaxValue;var minY=int.MaxValue;var maxX=0;var maxY=0;
  foreach(var member in world.Maps){if(member is null||!SafeId(member.Id)||!ids.Add(member.Id))throw new ArgumentException("Идентификатор участка мира повреждён.");var p=member.Project;ProjectValidator.Validate(p);
   for(var y=p.WorldY;y<p.WorldY+p.Height/256;y++)for(var x=p.WorldX;x<p.WorldX+p.Width/256;x++){if(!cells.Add((x,y)))throw new ArgumentException($"Участки мира перекрывают ячейку{x},{y}.");if(cells.Count>8192)throw new ArgumentException("Мир содержит больше8192 нативных ячеек.");}
   minX=Math.Min(minX,p.WorldX);minY=Math.Min(minY,p.WorldY);maxX=Math.Max(maxX,p.WorldX+p.Width/256);maxY=Math.Max(maxY,p.WorldY+p.Height/256);
  }if(world.Maps.Sum(m=>m.Project.Spawns.Count)==0)throw new ArgumentException("В мире нужна хотя бы одна точка появления на суше.");return new(minX,minY,(maxX-minX)*256,(maxY-minY)*256,cells.Count);
 }
 public static WorldProject Clone(WorldProject world)=>JsonSerializer.Deserialize<WorldProject>(JsonSerializer.Serialize(world,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;
 public static WorldProject Move(WorldProject world,string memberId,int cellX,int cellY){var copy=Clone(world);var member=copy.Maps.SingleOrDefault(m=>m.Id==memberId)??throw new ArgumentException("Участок мира не найден.");member.Project.WorldX=cellX;member.Project.WorldY=cellY;Validate(copy);return copy;}
 public static string[] SeamWarnings(WorldProject world){Validate(world);var warnings=new List<string>();for(var i=0;i<world.Maps.Count;i++)for(var j=i+1;j<world.Maps.Count;j++){
   var a=world.Maps[i].Project;var b=world.Maps[j].Project;var aw=a.Width/256;var ah=a.Height/256;var bw=b.Width/256;var bh=b.Height/256;
   var vertical=(a.WorldX+aw==b.WorldX||b.WorldX+bw==a.WorldX)&&a.WorldY<b.WorldY+bh&&b.WorldY<a.WorldY+ah;var horizontal=(a.WorldY+ah==b.WorldY||b.WorldY+bh==a.WorldY)&&a.WorldX<b.WorldX+bw&&b.WorldX<a.WorldX+aw;
   if(!vertical&&!horizontal)continue;var scaleA=a.MetresPerTile??1;var scaleB=b.MetresPerTile??1;if(Math.Abs(scaleA-scaleB)>Math.Max(scaleA,scaleB)*.001)warnings.Add($"«{a.Name}» и «{b.Name}»: разный географический масштаб, повторите импорт с одним размером клетки.");
   var mismatches=0;var compared=0;if(vertical){var left=a.WorldX<b.WorldX?a:b;var right=ReferenceEquals(left,a)?b:a;var start=Math.Max(left.WorldY,right.WorldY)*256;var end=Math.Min(left.WorldY+left.Height/256,right.WorldY+right.Height/256)*256;for(var y=start;y<end;y++){compared++;if(left.Terrain[(y-left.WorldY*256)*left.Width+left.Width-1]!=right.Terrain[(y-right.WorldY*256)*right.Width])mismatches++;}}
   else{var top=a.WorldY<b.WorldY?a:b;var bottom=ReferenceEquals(top,a)?b:a;var start=Math.Max(top.WorldX,bottom.WorldX)*256;var end=Math.Min(top.WorldX+top.Width/256,bottom.WorldX+bottom.Width/256)*256;for(var x=start;x<end;x++){compared++;if(top.Terrain[(top.Height-1)*top.Width+x-top.WorldX*256]!=bottom.Terrain[x-bottom.WorldX*256])mismatches++;}}
   if(mismatches>0)warnings.Add($"«{a.Name}» / «{b.Name}»: покрытия различаются в{mismatches} из{compared} клеток шва. Сверьте дороги и воду перед экспортом.");
  }return warnings.Take(64).ToArray();}
 public static bool SafeId(string? value)=>value is {Length:32}&&value.All(c=>char.IsAsciiHexDigit(c));
}
