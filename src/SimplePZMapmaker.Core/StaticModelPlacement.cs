using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Core;
/// <summary>A registered game model represented by a world inventory item, without structural collision.</summary>
public sealed class StaticModelPlacement
{
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public string ModelId {get;set;}="";
 public string? ItemType {get;set;}
 public string SourceId {get;set;}="game";
 public int X {get;set;} public int Y {get;set;} public int Z {get;set;}
 public double OffsetX {get;set;}=.5; public double OffsetY {get;set;}=.5;
 public double OffsetZ {get;set;}
 public double Rotation {get;set;} public double Scale {get;set;}=1;
 public List<string> RequiredMods {get;set;}=[];
 public static void Validate(StaticModelPlacement p,int width,int height,int minZ=-16,int maxZ=15)
 {
  if(p is null||!Regex.IsMatch(p.Id??"","^[a-fA-F0-9]{32}$")||!Regex.IsMatch(p.ModelId??"",@"^[A-Za-z][A-Za-z0-9_]*\.[A-Za-z0-9_]+$"))throw new ArgumentException("Некорректный идентификатор статичной модели.");
  if(p.ItemType is not null&&!Regex.IsMatch(p.ItemType,@"^[A-Za-z][A-Za-z0-9_]*\.[A-Za-z0-9_]+$"))throw new ArgumentException("Некорректный тип предмета модели.");
  if(p.SourceId is null||p.SourceId.Length>180||p.SourceId.Any(char.IsControl)||p.X<0||p.Y<0||p.X>=width||p.Y>=height||p.Z<minZ||p.Z>maxZ)throw new ArgumentException("Модель находится вне карты или этажа.");
  if(!double.IsFinite(p.OffsetX)||!double.IsFinite(p.OffsetY)||!double.IsFinite(p.OffsetZ)||p.OffsetX is <0 or >1||p.OffsetY is <0 or >1||p.OffsetZ is <0 or >1||!double.IsFinite(p.Rotation)||p.Rotation is <0 or >=360||!double.IsFinite(p.Scale)||p.Scale is <.01 or >20)throw new ArgumentException("Проверьте смещение, поворот и масштаб модели.");
  ResourceDependencies.Validate(p.RequiredMods,[]);
 }
}
