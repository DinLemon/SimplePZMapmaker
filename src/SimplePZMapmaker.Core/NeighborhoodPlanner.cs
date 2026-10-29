using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Core;
public sealed class NeighborhoodConstraints {
 public string Objective {get;set;}="Residential neighborhood";
 public int X {get;set;}=16; public int Y {get;set;}=16;
 public int Width {get;set;}=224; public int Height {get;set;}=224;
 public int MaximumBuildings {get;set;}=16; public int MinimumBuildings {get;set;}=1;
 public int MinimumLevels {get;set;}=1;public int MaximumLevels {get;set;}=5; public int Spacing {get;set;}=6;
 public int Seed {get;set;}=42; public bool Roads {get;set;}=true;
 public string Layout {get;set;}="frontage";
 public int Variation {get;set;}=70; public int RoadWidth {get;set;}=3;
 public bool Courtyards {get;set;}=true; public bool Fences {get;set;}=true; public bool Parking {get;set;}=true;
 public List<string> TemplateIds {get;set;}=[];
}
public sealed class NeighborhoodPlan {
 public string Id {get;set;}=Guid.NewGuid().ToString("N"); public long BaseRevision {get;set;}
 public NeighborhoodConstraints Constraints {get;set;}=new();
 public List<PlannedBuilding> Buildings {get;set;}=[]; public List<PlannedRoad> Roads {get;set;}=[];
 public List<PlannedParcel> Parcels {get;set;}=[]; public List<PlannedSurface> Surfaces {get;set;}=[];
 public List<PlannedFence> Fences {get;set;}=[]; public List<FenceStyle> FenceStyles {get;set;}=[]; public List<PlannedDecoration> Decorations {get;set;}=[]; public Dictionary<string,string> AssetSources {get;set;}=[]; public List<string> Warnings {get;set;}=[];
}
public sealed class PlannedBuilding {public string? MemberId {get;set;} public Building Building {get;set;}=new();}
public sealed record PlannedRoad(string? MemberId,int X,int Y,int Width,int Height);
public sealed record PlannedSurface(string? MemberId,int X,int Y,int Width,int Height,Terrain Terrain,string Kind);
public sealed record PlannedFence(string? MemberId,FenceLine Line);
public sealed record PlannedDecoration(string? MemberId,WorldDecoration Decoration);
public sealed record PlannedParcel(string? MemberId,string BuildingId,int X,int Y,int Width,int Height,int EntranceX,int EntranceY,bool AccessConnected);
public static class NeighborhoodPlanner {
 static readonly ConditionalWeakTable<MapProject,ParcelPlacement> blockerCache=new();
 public static NeighborhoodPlan Create(MapProject project,NeighborhoodConstraints c,string? memberId=null,bool[]? reservations=null){
  ProjectValidator.Validate(project);ValidateConstraints(c);
  if(c.X<0||c.Y<0||(long)c.X+c.Width>project.Width||(long)c.Y+c.Height>project.Height)throw new ArgumentException("The task area extends beyond the section.");
  var templates=HousingTemplates.Catalog.Where(t=>t.Levels>=c.MinimumLevels&&t.Levels<=c.MaximumLevels&&(c.TemplateIds.Count==0||c.TemplateIds.Contains(t.Id))).ToArray();
  if(c.TemplateIds.Count==0&&c.Layout=="courtyard")templates=templates.Where(t=>t.Id.Contains("courtyard",StringComparison.Ordinal)).ToArray();
  if(c.TemplateIds.Count==0&&c.Layout=="rural")templates=templates.Where(t=>t.Category=="village"||t.Id.StartsWith("rural-",StringComparison.Ordinal)).ToArray();
  if(templates.Length==0||c.TemplateIds.Any(id=>!HousingTemplates.Catalog.Any(t=>t.Id==id)))throw new ArgumentException("No templates match the task constraints.");
  var plan=new NeighborhoodPlan{Constraints=Copy(c)};var candidate=Stage(project);Reserve(candidate,reservations);long floorBudget=candidate.Buildings.Concat(candidate.BuildingLibrary).Sum(b=>(long)(b.Design?.Floors.Sum(f=>f.Cells.Length)??0));var random=new Random(c.Seed);var edited=TerrainEditMask.Decode(project);
  var strideX=templates.Max(t=>t.Width)+c.Spacing+6;var strideY=templates.Max(t=>t.Height)+c.Spacing+c.RoadWidth+10;
  var origins=new List<(int X,int Y,int RoadY)>();var seen=new HashSet<(int,int)>();
  // Existing street frontage is preferred; generated rows are the bounded fallback.
  if(c.Layout=="frontage")for(var y=c.Y;y<c.Y+c.Height;y+=2)for(var x=c.X;x<c.X+c.Width;x+=4){
   if(project.Terrain[y*project.Width+x]!=Terrain.Road||project.Terrain[Math.Min(project.Height-1,y+2)*project.Width+x]==Terrain.Road)continue;
   if(seen.Add((x,y)))origins.Add((x+3,y+5,y));if(origins.Count>=4096)break;
  }
  for(var y=c.Y+3;y+templates.Min(t=>t.Height)+c.RoadWidth+7<c.Y+c.Height;y+=strideY)
   for(var x=c.X+6;x+templates.Min(t=>t.Width)+3<c.X+c.Width;x+=strideX)origins.Add((x,y+c.RoadWidth+4,y));
  foreach(var (x,y,roadY) in origins){
   if(plan.Buildings.Count>=c.MaximumBuildings)break;if(candidate.Buildings.Count>=ProjectValidator.MaximumBuildings){plan.Warnings.Add("The section building budget was reached; remaining template buildings were omitted.");break;}
   var t=templates[random.Next(templates.Length)];if(x+t.Width+3>=c.X+c.Width||y+t.Height+4>=c.Y+c.Height)continue;
   var b=HousingTemplates.Create(t.Id);b.X=x;b.Y=y;b.Id=StableId($"{memberId}:{c.Seed}:{x}:{y}:{t.Id}");b.Modified=true;
   var cost=b.Design!.Floors.Sum(f=>(long)f.Cells.Length);if(floorBudget+cost>20_000_000){plan.Warnings.Add("The section floor-tile budget was reached; oversized template buildings were omitted.");continue;}
   if(!Available(candidate,b,edited))continue;
   if(c.Variation>0&&random.Next(100)<c.Variation)NeighborhoodVariation.Apply(b,random.Next());
   floorBudget+=cost;candidate.Buildings.Add(b);Blockers(candidate).Add(b);plan.Buildings.Add(new(){MemberId=memberId,Building=b});
   var parcel=new PlannedParcel(memberId,b.Id,Math.Max(c.X,x-3),Math.Max(c.Y,roadY),b.Width+7,b.Height+y-roadY+5,x+b.Width/2,y-1,false);
   if(parcel.X+parcel.Width>c.X+c.Width||parcel.Y+parcel.Height>c.Y+c.Height)parcel=parcel with{Width=Math.Min(parcel.Width,c.X+c.Width-parcel.X),Height=Math.Min(parcel.Height,c.Y+c.Height-parcel.Y)};
   if(c.Roads){PaintRoad(candidate,plan,memberId,c.X+2,roadY,c.Width-4,c.RoadWidth,edited);PaintRoad(candidate,plan,memberId,c.X+2,c.Y+2,c.RoadWidth,roadY-c.Y+c.RoadWidth,edited);}
   var entrance=Entrance(b);parcel=parcel with{EntranceX=entrance.X,EntranceY=entrance.Y};
   var path=AccessPath(candidate,parcel,edited);if(path.Count>0){Blockers(candidate).ReservePassage(path);parcel=parcel with{AccessConnected=true};foreach(var point in path)Surface(candidate,plan,memberId,point.X,point.Y,Terrain.Pavement,"driveway",edited);}
   else plan.Warnings.Add($"{b.Name}: no unobstructed street access inside its parcel; the existing terrain was retained.");
   if(c.Courtyards)for(var yy=b.Y+b.Height+2;yy<Math.Min(parcel.Y+parcel.Height,b.Y+b.Height+5);yy++)for(var xx=b.X+2;xx<b.X+Math.Min(b.Width-1,10);xx++)Surface(candidate,plan,memberId,xx,yy,Terrain.Dirt,"courtyard",edited);
   if(c.Parking)for(var yy=roadY+c.RoadWidth;yy<Math.Min(b.Y-1,roadY+c.RoadWidth+3);yy++)for(var xx=b.X+b.Width-6;xx<b.X+b.Width;xx++)Surface(candidate,plan,memberId,xx,yy,Terrain.Pavement,"parking",edited);
   if(c.Fences&&c.Layout!="grid")Fence(candidate,plan,parcel,path,edited);
   plan.Parcels.Add(parcel);
  }
  if(plan.Buildings.Count<c.MinimumBuildings)throw new ArgumentException("Could not place the minimum number of buildings without overlapping water or existing objects. Enlarge the area or lower the requirement.");
  if(plan.Buildings.Count<c.MaximumBuildings)plan.Warnings.Add($"Placed {plan.Buildings.Count} of {c.MaximumBuildings} requested buildings; occupied areas and water were preserved.");
  // Collapse adjacent one-tile surfaces without changing their intent or coverage.
  plan.Surfaces=plan.Surfaces.GroupBy(s=>(s.MemberId,s.Y,s.Terrain,s.Kind)).SelectMany(g=>Runs(g.OrderBy(s=>s.X).DistinctBy(s=>s.X).ToArray())).ToList();
  plan.Warnings=plan.Warnings.Distinct().Take(140).ToList();ValidatePlan(plan);return plan;
 }
 public static void Apply(MapProject project,NeighborhoodPlan plan,string? memberId=null,bool[]? reservations=null){
  ValidatePlan(plan);var candidate=Stage(project);Reserve(candidate,reservations);var mask=TerrainEditMask.Decode(project);var originalMask=(byte[])mask.Clone();var newIds=new HashSet<string>();
  foreach(var entry in plan.Buildings.Where(e=>e.MemberId==memberId)){var b=Copy(entry.Building);BuildingDesigner.Validate(b);if(!newIds.Add(b.Id)||!Available(candidate,b,mask))throw new ArgumentException("The building proposal overlaps the current map.");candidate.Buildings.Add(b);Blockers(candidate).Add(b);}
  var painted=new HashSet<int>();
  void Paint(int x,int y,int width,int height,Terrain terrain){if(x<0||y<0||width<1||height<1||(long)x+width>candidate.Width||(long)y+height>candidate.Height)throw new ArgumentException("Invalid proposal surface bounds.");for(var yy=y;yy<y+height;yy++)for(var xx=x;xx<x+width;xx++){var index=yy*candidate.Width+xx;if(!GroundAvailable(candidate,xx,yy,originalMask)&&!painted.Contains(index))throw new ArgumentException("The road or surface overlaps an object or a manual edit.");if(Occupied(candidate,xx,yy))throw new ArgumentException("The road or surface overlaps an object.");candidate.Terrain[index]=terrain;candidate.SurfaceTiles.Remove(index);mask[index/8]|=(byte)(1<<(index%8));painted.Add(index);}}
  foreach(var road in plan.Roads.Where(e=>e.MemberId==memberId))Paint(road.X,road.Y,road.Width,road.Height,Terrain.Road);
  foreach(var surface in plan.Surfaces.Where(e=>e.MemberId==memberId))Paint(surface.X,surface.Y,surface.Width,surface.Height,surface.Terrain);
  Blockers(candidate).ReservePassage(plan.Surfaces.Where(s=>s.MemberId==memberId&&s.Kind=="driveway").SelectMany(s=>Enumerable.Range(s.Y,s.Height).SelectMany(y=>Enumerable.Range(s.X,s.Width).Select(x=>(X:x,Y:y)))));
  foreach(var entry in plan.Decorations.Where(e=>e.MemberId==memberId)){var d=Copy(entry.Decoration);foreach(var part in d.Parts)if(!GroundAvailable(candidate,d.X+part.Dx,d.Y+part.Dy,originalMask)||!Blockers(candidate).Free(d.X+part.Dx,d.Y+part.Dy))throw new ArgumentException("The parcel decoration overlaps an object or a manual edit.");candidate.Decorations.Add(d);Blockers(candidate).Add(d);}
  ParcelFences.MergeStyles(candidate,plan.FenceStyles);
  foreach(var entry in plan.Fences.Where(e=>e.MemberId==memberId)){var line=Copy(entry.Line);if(line.Points.Any(v=>v.X<0||v.Y<0||v.X>candidate.Width||v.Y>candidate.Height))throw new ArgumentException("A proposed fence extends outside the section.");foreach(var piece in FenceLines.Compose([line],candidate.FenceStyles,candidate.GateStyles,candidate.Width,candidate.Height).Pieces)if(!GroundAvailable(candidate,piece.X,piece.Y,originalMask)||!Blockers(candidate).Free(piece.X,piece.Y))throw new ArgumentException($"The proposed fence overlaps an object, road or reserved passage at {piece.X},{piece.Y}.");candidate.Fences.Add(line);}
  var used=plan.Buildings.Where(e=>e.MemberId==memberId).SelectMany(e=>NativeScene.BuildingResourceIds(e.Building)).Concat(plan.Decorations.Where(e=>e.MemberId==memberId).SelectMany(e=>e.Decoration.Parts.Select(part=>part.Tile).Append(e.Decoration.Asset))).Concat(plan.FenceStyles.SelectMany(s=>new[]{s.W,s.N,s.NW,s.Post,s.WAlt,s.NAlt}.OfType<string>())).ToHashSet(StringComparer.Ordinal);foreach(var (id,owner) in plan.AssetSources.Where(e=>used.Contains(e.Key))){if(candidate.AssetSources.TryGetValue(id,out var existing)&&existing!=owner)throw new ArgumentException("A proposal resource owner conflicts with the current project.");candidate.AssetSources[id]=owner;}
  candidate.TerrainEdits=TerrainEditMask.Encode(mask);candidate.Version=Math.Max(6,project.Version);ProjectValidator.Validate(candidate);
  project.Buildings=candidate.Buildings;project.Terrain=candidate.Terrain;project.SurfaceTiles=candidate.SurfaceTiles;project.Decorations=candidate.Decorations;project.TerrainEdits=candidate.TerrainEdits;project.Version=candidate.Version;project.AssetSources=candidate.AssetSources;project.Fences=candidate.Fences;project.FenceStyles=candidate.FenceStyles;
 }
 static IEnumerable<PlannedSurface> Runs(PlannedSurface[] row){for(var i=0;i<row.Length;i++){var first=row[i];var last=first.X;while(i+1<row.Length&&row[i+1].X==last+1){last++;i++;}yield return first with{Width=last-first.X+1};}}
 static MapProject Stage(MapProject p){var copy=Copy(p);copy.SharedInfrastructure=p.SharedInfrastructure;return copy;}
 static void Reserve(MapProject p,bool[]? reservations){if(reservations is null)return;if(reservations.Length!=p.Terrain.Length)throw new ArgumentException("Reservation raster does not match the section.");for(var i=0;i<reservations.Length;i++)if(reservations[i]&&p.Terrain[i]!=Terrain.Road)Blockers(p).Block(i%p.Width,i/p.Width);}
 static string StableId(string value)=>"plan-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
 static bool Available(MapProject p,Building b,byte[] edited){if(b.X<1||b.Y<1||b.X+b.Width>=p.Width||b.Y+b.Height>=p.Height)return false;for(var y=b.Y;y<=b.Y+b.Height;y++)for(var x=b.X;x<=b.X+b.Width;x++){var i=y*p.Width+x;if(p.Terrain[i] is Terrain.Water or Terrain.Road||Occupied(p,x,y)||Blockers(p).Passage(x,y)||p.SurfaceTiles.ContainsKey(i)||TerrainEditMask.IsEdited(edited,i))return false;}return true;}
 static ParcelPlacement Blockers(MapProject p)=>blockerCache.GetValue(p,key=>{var placement=new ParcelPlacement(key);placement.ReserveEntrances();return placement;});
 static bool Occupied(MapProject p,int x,int y)=>Blockers(p).Occupied(x,y); static bool GroundAvailable(MapProject p,int x,int y,byte[] edited){if(!Blockers(p).Free(x,y,allowRoad:true,forPassage:true))return false;var i=y*p.Width+x;return p.Terrain[i]!=Terrain.Water&&!p.SurfaceTiles.ContainsKey(i)&&!TerrainEditMask.IsEdited(edited,i);}
 static void PaintRoad(MapProject p,NeighborhoodPlan plan,string? member,int x,int y,int width,int height,byte[] mask){if(plan.Roads.Count>=8192)return;for(var yy=y;yy<y+height;yy++)for(var xx=x;xx<x+width;xx++){if(!GroundAvailable(p,xx,yy,mask)||p.Terrain[yy*p.Width+xx]==Terrain.Road)continue;var start=xx;while(xx+1<x+width&&GroundAvailable(p,xx+1,yy,mask)&&p.Terrain[yy*p.Width+xx+1]!=Terrain.Road)xx++;if(plan.Roads.Count>=8192)return;plan.Roads.Add(new(member,start,yy,xx-start+1,1));for(var a=start;a<=xx;a++)p.Terrain[yy*p.Width+a]=Terrain.Road;}}
 static void Surface(MapProject p,NeighborhoodPlan plan,string? member,int x,int y,Terrain terrain,string kind,byte[] mask){if(plan.Surfaces.Count>=200000||!GroundAvailable(p,x,y,mask)||p.Terrain[y*p.Width+x]==Terrain.Road)return;p.Terrain[y*p.Width+x]=terrain;plan.Surfaces.Add(new(member,x,y,1,1,terrain,kind));}
 static (int X,int Y) Entrance(Building b){var f=b.Design!.Floors[-b.Design.MinLevel];foreach(var e in f.Edges.Where(e=>e.Kind=="door")){if(e.Edge=="n"&&e.Y==0)return(b.X+e.X,b.Y-1);if(e.Edge=="n"&&e.Y==b.Height)return(b.X+e.X,b.Y+b.Height+1);if(e.Edge=="w"&&e.X==0)return(b.X-1,b.Y+e.Y);if(e.Edge=="w"&&e.X==b.Width)return(b.X+b.Width+1,b.Y+e.Y);}return(b.X+b.Width/2,b.Y-1);}
 static List<(int X,int Y)> AccessPath(MapProject p,PlannedParcel parcel,byte[] mask){var start=(X:parcel.EntranceX,Y:parcel.EntranceY);if(!GroundAvailable(p,start.X,start.Y,mask))return [];var queue=new Queue<(int X,int Y)>();var previous=new Dictionary<(int X,int Y),(int X,int Y)>();queue.Enqueue(start);previous[start]=start;while(queue.Count>0&&previous.Count<=10000){var current=queue.Dequeue();if(p.Terrain[current.Y*p.Width+current.X]==Terrain.Road){var path=new List<(int,int)>();while(current!=start){path.Add(current);current=previous[current];}path.Add(start);return path;}foreach(var (dx,dy) in new[]{(0,-1),(1,0),(-1,0),(0,1)}){var next=(X:current.X+dx,Y:current.Y+dy);if(next.X<parcel.X||next.Y<parcel.Y||next.X>=parcel.X+parcel.Width||next.Y>=parcel.Y+parcel.Height||previous.ContainsKey(next)||!GroundAvailable(p,next.X,next.Y,mask))continue;previous[next]=current;queue.Enqueue(next);}}return [];}
 static void Fence(MapProject p,NeighborhoodPlan plan,PlannedParcel parcel,List<(int X,int Y)> path,byte[] mask){
  var gate=path.ToHashSet();var edges=new List<(int X,int Y,bool North)>();
  for(var y=parcel.Y+2;y<parcel.Y+parcel.Height;y++)for(var x=parcel.X;x<parcel.X+parcel.Width;x++){
   var vertical=x==parcel.X||x==parcel.X+parcel.Width-1;var horizontal=y==parcel.Y+parcel.Height-1;
   if(!vertical&&!horizontal||gate.Any(g=>Math.Abs(g.X-x)+Math.Abs(g.Y-y)<=1)||Blockers(p).Passage(x,y)||!GroundAvailable(p,x,y,mask)||p.Terrain[y*p.Width+x]==Terrain.Road)continue;
   if(vertical)edges.Add((x,y,false));if(horizontal)edges.Add((x,y,true));
  }
  var style=ParcelFences.Picket;if(plan.FenceStyles.All(s=>s.Id!=style.Id))plan.FenceStyles.Add(Copy(style));
  foreach(var line in ParcelFences.Runs(parcel.BuildingId,style,edges)){plan.Fences.Add(new(parcel.MemberId,line));foreach(var piece in FenceLines.Compose([line],[style],[],p.Width,p.Height).Pieces)Blockers(p).Block(piece.X,piece.Y);}
 }
 public static void ValidatePlan(NeighborhoodPlan p){ValidateConstraints(p.Constraints);ResourceDependencies.Validate([],p.AssetSources);if(p.Buildings is null||p.Roads is null||p.Surfaces is null||p.Decorations is null||p.Fences is null||p.FenceStyles is null||p.Fences.Count>10000||p.FenceStyles.Count>128||p.Fences.Any(f=>f is null||f.Line is null)||p.Parcels is null||p.Warnings is null||p.Buildings.Count>128||p.Roads.Count>8192||p.Surfaces.Count>200000||p.Decorations.Count>8000||p.Parcels.Count>128||p.Warnings.Count>140||p.Warnings.Any(w=>w is null||w.Length>1000)||p.Roads.Any(r=>r is null||r.Width<1||r.Height<1||(long)r.Width*r.Height>4_194_304)||p.Surfaces.Any(s=>s is null||s.Kind is not("driveway" or "parking" or "courtyard")||s.Terrain is not(Terrain.Dirt or Terrain.Pavement)||s.Width<1||s.Height<1||(long)s.Width*s.Height>4_194_304)||p.Decorations.Any(d=>d is null||d.Decoration is null))throw new ArgumentException("The proposal exceeds its budget or contains invalid parcel features.");if(p.Roads.Sum(r=>(long)r.Width*r.Height)+p.Surfaces.Sum(s=>(long)s.Width*s.Height)>2_000_000)throw new ArgumentException("The proposal exceeds two million edited terrain tiles.");FenceLines.Validate(p.Fences.Select(f=>f.Line).ToList(),p.FenceStyles,[],32768);}
 public static void ValidateConstraints(NeighborhoodConstraints c){if(c is null||c.Objective is null||c.Objective.Length>800||c.Objective.Any(char.IsControl)||c.Width is <16 or >2048||c.Height is <16 or >2048||c.MaximumBuildings is <1 or >128||c.MinimumBuildings<0||c.MinimumBuildings>c.MaximumBuildings||c.MinimumLevels<1||c.MinimumLevels>c.MaximumLevels||c.MaximumLevels is <1 or >ZLevels.AboveGroundCount||c.Spacing is <4 or >64||c.TemplateIds is null||c.TemplateIds.Count>64||c.Layout is not("frontage" or "grid" or "courtyard" or "rural")||c.Variation is <0 or >100||c.RoadWidth is <3 or >12)throw new ArgumentException("Check the task area, building budget, floor count, spacing, layout, and variation.");}
 public static T Copy<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;
}
