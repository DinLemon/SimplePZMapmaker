using System.Text.Json;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Core;

public sealed class NeighborhoodConstraints {
 public string Objective {get;set;}="Жилой квартал";
 public int X {get;set;}=16; public int Y {get;set;}=16;
 public int Width {get;set;}=224; public int Height {get;set;}=224;
 public int MaximumBuildings {get;set;}=16; public int MinimumBuildings {get;set;}=1;
 public int MaximumLevels {get;set;}=5; public int Spacing {get;set;}=6;
 public int Seed {get;set;}=42;
 public bool Roads {get;set;}=true;
 public List<string> TemplateIds {get;set;}=[];
}
public sealed class NeighborhoodPlan {
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public long BaseRevision {get;set;}
 public NeighborhoodConstraints Constraints {get;set;}=new();
 public List<PlannedBuilding> Buildings {get;set;}=[];
 public List<PlannedRoad> Roads {get;set;}=[];
 public List<string> Warnings {get;set;}=[];
}
public sealed class PlannedBuilding {
 public string? MemberId {get;set;}
 public Building Building {get;set;}=new();
}
public sealed record PlannedRoad(string? MemberId,int X,int Y,int Width,int Height);
public static class NeighborhoodPlanner {
 public static NeighborhoodPlan Create(MapProject project,NeighborhoodConstraints constraints,string? memberId=null) {
  ProjectValidator.Validate(project);ValidateConstraints(constraints);
  if(constraints.X<0||constraints.Y<0||constraints.X+constraints.Width>project.Width||constraints.Y+constraints.Height>project.Height)throw new ArgumentException("Область задания выходит за пределы секции.");
  var templates=HousingTemplates.Catalog.Where(t=>t.Levels<=constraints.MaximumLevels&&(constraints.TemplateIds.Count==0||constraints.TemplateIds.Contains(t.Id))).ToArray();
  if(templates.Length==0||constraints.TemplateIds.Any(id=>!HousingTemplates.Catalog.Any(t=>t.Id==id)))throw new ArgumentException("Для ограничений задания нет подходящих шаблонов.");
  var plan=new NeighborhoodPlan{Constraints=Copy(constraints)};var candidate=new MapProject{Width=project.Width,Height=project.Height,Terrain=project.Terrain,Buildings=project.Buildings.ToList(),Spawns=project.Spawns,StaticModels=project.StaticModels,Decorations=project.Decorations,SurfaceTiles=project.SurfaceTiles};var random=new Random(constraints.Seed);var edited=TerrainEditMask.Decode(project);
  var strideX=templates.Max(t=>t.Width)+constraints.Spacing;var strideY=templates.Max(t=>t.Height)+constraints.Spacing;
  for(var y=constraints.Y;y+templates.Min(t=>t.Height)<=constraints.Y+constraints.Height&&plan.Buildings.Count<constraints.MaximumBuildings;y+=strideY)
   for(var x=constraints.X;x+templates.Min(t=>t.Width)<=constraints.X+constraints.Width&&plan.Buildings.Count<constraints.MaximumBuildings;x+=strideX){
    var t=templates[random.Next(templates.Length)];if(x+t.Width>constraints.X+constraints.Width||y+t.Height>constraints.Y+constraints.Height)continue;
    var b=HousingTemplates.Create(t.Id);b.X=x;b.Y=y;b.Modified=true;
    if(!Available(candidate,b))continue;
    candidate.Buildings.Add(b);plan.Buildings.Add(new(){MemberId=memberId,Building=b});
   }
  if(plan.Buildings.Count<constraints.MinimumBuildings)throw new ArgumentException("Не удалось разместить минимальное число зданий без пересечения воды и существующих объектов. Увеличьте область или уменьшите требование.");
  if(plan.Buildings.Count<constraints.MaximumBuildings)plan.Warnings.Add($"Размещено{plan.Buildings.Count} из запрошенных{constraints.MaximumBuildings}; занятые участки и вода сохранены.");
  if(constraints.Roads)foreach(var row in plan.Buildings.Select(b=>b.Building.Y).Distinct()){
   var y=row-3;if(y<constraints.Y)continue;
   for(var x=constraints.X;x<constraints.X+constraints.Width;x++)if(GroundAvailable(candidate,x,y,edited)){
    var begin=x;while(x+1<constraints.X+constraints.Width&&GroundAvailable(candidate,x+1,y,edited))x++;
    plan.Roads.Add(new(memberId,begin,y,x-begin+1,1));
   }
  }
  return plan;
 }
 public static void Apply(MapProject project,NeighborhoodPlan plan,string? memberId=null) {
  ValidateConstraints(plan.Constraints);if(plan.Buildings.Count>128||plan.Roads.Count>8192)throw new ArgumentException("План превышает бюджет квартала.");
  foreach(var entry in plan.Buildings.Where(e=>e.MemberId==memberId)){
   var b=Copy(entry.Building);BuildingDesigner.Validate(b);if(!Available(project,b))throw new ArgumentException("План здания пересекается с текущей картой.");project.Buildings.Add(b);
  }
  var mask=TerrainEditMask.Decode(project);
  foreach(var road in plan.Roads.Where(e=>e.MemberId==memberId)){
   if(road.Width<1||road.Height<1||road.Width>2048||road.Height>2048)throw new ArgumentException("Дорога плана повреждена.");
   for(var y=road.Y;y<road.Y+road.Height;y++)for(var x=road.X;x<road.X+road.Width;x++){
    if(!GroundAvailable(project,x,y,mask))throw new ArgumentException("Дорога пересекается с объектом или ручной правкой.");var i=y*project.Width+x;project.Terrain[i]=Terrain.Road;project.SurfaceTiles.Remove(i);mask[i/8]|=(byte)(1<<(i%8));
   }
  }
  project.TerrainEdits=TerrainEditMask.Encode(mask);project.Version=Math.Max(6,project.Version);ProjectValidator.Validate(project);
 }
 static bool Available(MapProject project,Building b) {
  if(b.X<1||b.Y<1||b.X+b.Width>=project.Width||b.Y+b.Height>=project.Height)return false;
  for(var y=b.Y;y<=b.Y+b.Height;y++)for(var x=b.X;x<=b.X+b.Width;x++)if(project.Terrain[y*project.Width+x] is Terrain.Water or Terrain.Road||Occupied(project,x,y))return false;
  return true;
 }
 static bool Occupied(MapProject p,int x,int y)=>p.Buildings.Any(b=>x>=b.X&&y>=b.Y&&x<=b.X+b.Width&&y<=b.Y+b.Height)||p.Spawns.Any(s=>s.X==x&&s.Y==y)||p.StaticModels.Any(m=>m.X==x&&m.Y==y)||p.Decorations.Any(d=>d.Parts.Any(part=>d.X+part.Dx==x&&d.Y+part.Dy==y));
 static bool GroundAvailable(MapProject p,int x,int y,byte[] edited) {
  if(x<0||y<0||x>=p.Width||y>=p.Height||Occupied(p,x,y))return false;
  var i=y*p.Width+x;if(p.Terrain[i]==Terrain.Water||p.SurfaceTiles.ContainsKey(i))return false;
  if(TerrainEditMask.IsEdited(edited,i))return false;
  return true;
 }
 public static void ValidateConstraints(NeighborhoodConstraints c) {
  if(c is null||c.Objective is null||c.Objective.Length>800||c.Objective.Any(char.IsControl)||c.Width is <16 or >2048||c.Height is <16 or >2048||c.MaximumBuildings is <1 or >128||c.MinimumBuildings<0||c.MinimumBuildings>c.MaximumBuildings||c.MaximumLevels is <1 or >16||c.Spacing is <4 or >64||c.TemplateIds is null||c.TemplateIds.Count>64)throw new ArgumentException("Проверьте область, бюджет зданий, этажность и отступы задания.");
 }
 public static T Copy<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;
}
