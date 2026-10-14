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
 public List<ModelFootprintCell> Footprint {get;set;}=[];
 public ModelDoorSettings? Door {get;set;}
 public ModelLightSettings? Light {get;set;}
 public string? LootDistribution {get;set;}
 public string LootSourceId {get;set;}="game";
 public List<string> LootRequiredMods {get;set;}=[];
 [System.Text.Json.Serialization.JsonIgnore] public bool Interactive=>Footprint?.Count>0||Door is not null||Light is not null||LootDistribution is not null||Profile is "door" or "light";
 public static IReadOnlyList<ModelFootprintCell> FootprintCells(StaticModelPlacement p)=>p.Footprint is null?throw new ArgumentException("The model footprint is missing."):p.Footprint.Count==0?[new(0,0)]:p.Footprint;
 public static bool BlocksTiles(StaticModelPlacement p)=>p.Profile is "obstacle" or "destructible" or "container";
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
  if(p.Profile is not ("pickup" or "scenery" or "obstacle" or "destructible" or "container" or "door" or "light")||p.Health is <1 or >100000||p.Capacity is <1 or >1000)throw new ArgumentException("Check the model behavior, health (1-100,000), and capacity (1-1,000).");
  ValidateBehavior(p);
  if(FootprintCells(p).Any(c=>(long)p.X+c.Dx>=width||(long)p.Y+c.Dy>=height))throw new ArgumentException("The complete model footprint extends beyond the map or floor.");
  if(p.WorldRotation is null||p.WorldRotation.Length!=3||p.WorldRotation.Any(v=>!double.IsFinite(v)||Math.Abs(v)>36000)||!double.IsFinite(p.WorldScale)||p.WorldScale is <=0 or >100)throw new ArgumentException("Invalid model world attachment.");
 }
 public static void ValidateBehavior(StaticModelPlacement p)
 {
  if(p.Footprint is null||p.Footprint.Count>64||p.Footprint.Any(c=>c is null||c.Dx is <0 or >7||c.Dy is <0 or >7)||p.Footprint.Distinct().Count()!=p.Footprint.Count)throw new ArgumentException("Use up to 64 distinct footprint tiles within an 8×8 area.");
  var cells=FootprintCells(p).ToHashSet();if(!cells.Contains(new(0,0)))throw new ArgumentException("The footprint must include its anchor tile (0,0).");var seen=new HashSet<ModelFootprintCell>{new(0,0)};var queue=new Queue<ModelFootprintCell>();queue.Enqueue(new(0,0));while(queue.TryDequeue(out var c))foreach(var n in new ModelFootprintCell[]{new(c.Dx+1,c.Dy),new(c.Dx-1,c.Dy),new(c.Dx,c.Dy+1),new(c.Dx,c.Dy-1)})if(cells.Contains(n)&&seen.Add(n))queue.Enqueue(n);if(seen.Count!=cells.Count)throw new ArgumentException("A model footprint must be connected by tile edges.");
  if(p.Profile=="pickup"&&(cells.Count!=1||p.Door is not null||p.Light is not null))throw new ArgumentException("Pickup items use one tile and have no door or light behavior.");
  if(p.Profile=="door") {if(p.Door is null||cells.Count!=1||p.Door.Direction is not ("n" or "w")||p.Door.KeyId<1)throw new ArgumentException("Doors need one anchor tile, north/west direction and a positive key ID.");}else if(p.Door is not null)throw new ArgumentException("Door settings require the door profile.");
  if(p.Profile=="light"&&p.Light is null)throw new ArgumentException("The light profile needs a native light definition.");
  if(p.Light is {} light&&(light.Rgb is null||light.Rgb.Length!=3||light.Rgb.Any(v=>!double.IsFinite(v)||v is <0 or >1)||light.Radius is <1 or >20))throw new ArgumentException("Native light RGB must be 0–1 and radius 1–20 tiles.");
  if(p.LootDistribution is not null&&(p.Profile!="container"||!Regex.IsMatch(p.LootDistribution,@"^[A-Za-z][A-Za-z0-9_]{0,119}$")||!ResourceDependencies.SafeModId(p.LootSourceId)))throw new ArgumentException("Loot requires a registered distribution on a container.");
  ResourceDependencies.Validate(p.LootRequiredMods,[]);if(p.LootDistribution is null&&p.LootRequiredMods.Count>0)throw new ArgumentException("Loot dependencies require a loot distribution.");
 }
}
public sealed record ModelFootprintCell(int Dx,int Dy);
public sealed class ModelDoorSettings { public string Direction {get;set;}="n"; public bool Open {get;set;} public bool Locked {get;set;} public int KeyId {get;set;}=1; }
public sealed class ModelLightSettings { public double[] Rgb {get;set;}=[1,1,1]; public int Radius {get;set;}=8; public bool Active {get;set;}=true; }
