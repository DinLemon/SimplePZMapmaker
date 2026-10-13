using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Core;
/// <summary>A registered model placed as a pickup item or a persistent native scenery object.</summary>
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
 public string Profile {get;set;}="pickup";
 public int Health {get;set;}=500;
 public int Capacity {get;set;}=50;
 /// <summary>Canonical world attachment from the registered model, stamped by the resource catalog.</summary>
 public double[] WorldRotation {get;set;}=[0,0,0];
 public double WorldScale {get;set;}=1;
 public List<string> RequiredMods {get;set;}=[];
 public static void Validate(StaticModelPlacement p,int width,int height,int minZ=-16,int maxZ=15)
 {
  if(p is null||!Regex.IsMatch(p.Id??"","^[a-fA-F0-9]{32}$")||!Regex.IsMatch(p.ModelId??"",@"^[A-Za-z][A-Za-z0-9_]*\.[A-Za-z0-9_]+$"))throw new ArgumentException("Invalid static model ID.");
  if(p.ItemType is not null&&!Regex.IsMatch(p.ItemType,@"^[A-Za-z][A-Za-z0-9_]*\.[A-Za-z0-9_]+$"))throw new ArgumentException("Invalid model item type.");
  if(p.SourceId is null||p.SourceId.Length>180||p.SourceId.Any(char.IsControl)||p.X<0||p.Y<0||p.X>=width||p.Y>=height||p.Z<minZ||p.Z>maxZ)throw new ArgumentException("The model is outside the map or floor.");
  if(!double.IsFinite(p.OffsetX)||!double.IsFinite(p.OffsetY)||!double.IsFinite(p.OffsetZ)||p.OffsetX is <0 or >1||p.OffsetY is <0 or >1||p.OffsetZ is <0 or >1||!double.IsFinite(p.Rotation)||p.Rotation is <0 or >=360||!double.IsFinite(p.Scale)||p.Scale is <.01 or >20)throw new ArgumentException("Check the model offset, rotation, and scale.");
  ResourceDependencies.Validate(p.RequiredMods,[]);
  if(p.Profile is not ("pickup" or "scenery" or "obstacle" or "destructible" or "container")||p.Health is <1 or >100000||p.Capacity is <1 or >1000)throw new ArgumentException("Check the model behavior, health (1-100,000), and capacity (1-1,000).");
  if(p.WorldRotation is null||p.WorldRotation.Length!=3||p.WorldRotation.Any(v=>!double.IsFinite(v)||Math.Abs(v)>36000)||!double.IsFinite(p.WorldScale)||p.WorldScale is <=0 or >100)throw new ArgumentException("Invalid model world attachment.");
 }
}
