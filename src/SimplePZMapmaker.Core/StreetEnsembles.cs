using System.Security.Cryptography;
using System.Text.Json;
namespace SimplePZMapmaker.Core;
public sealed class StreetEnsembleOptions {
 public int X {get;set;}=16;public int Y {get;set;}=16;public int Width {get;set;}=100;public int Height {get;set;}=80;
 public string Preset {get;set;}="residential";public int Seed {get;set;}=42;
 public int Entrances {get;set;}=2;public int Floors {get;set;}=3;public int ApartmentsPerFloor {get;set;}=2;
 public bool Fences {get;set;}=true;public bool Parking {get;set;}=true;public bool Greenery {get;set;}=true;public bool Lights {get;set;}=true;
}
public sealed record EnsembleResource(string Role,string Asset,List<BuildingObjects.Part> Parts,string? ModId=null,List<string>? Requirements=null);
public sealed record EnsemblePoint(int X,int Y);
public sealed record EnsembleSurface(int X,int Y,int Width,int Height,Terrain Terrain,string Kind);
public sealed class StreetEnsemblePlan {
 public string Id {get;set;}="";public string BaseFingerprint {get;set;}="";public StreetEnsembleOptions Options {get;set;}=new();
 public List<FenceLine> Fences {get;set;}=[];public List<FenceStyle> FenceStyles {get;set;}=[];public List<Building> Buildings {get;set;}=[];public List<WorldDecoration> Decorations {get;set;}=[];public List<StaticModelPlacement> StaticModels {get;set;}=[];
 public List<GateStyle> GateStyles {get;set;}=[];
 public List<EnsembleSurface> Surfaces {get;set;}=[];public List<List<EnsemblePoint>> AccessPaths {get;set;}=[];
 public Dictionary<string,string> AssetSources {get;set;}=[];public List<string> RequiredMods {get;set;}=[];public List<string> Warnings {get;set;}=[];
}
/// <summary>Bounded parcel generation; preview never changes its input and apply returns a complete validated snapshot.</summary>
public static class StreetEnsembles {
 public static string Fingerprint(MapProject p)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(p)));
 public static StreetEnsemblePlan Create(MapProject project,StreetEnsembleOptions o,IReadOnlyList<EnsembleResource> resources,StaticModelPlacement? nativeLight=null){
  ProjectValidator.Validate(project);Validate(o,project.Width,project.Height);if(resources is null||resources.Count>32)throw new ArgumentException("Choose a bounded resource palette.");
  if(o.Preset=="emergency-shelter")return EmergencyAccommodation.Create(project,o,resources);
  var plan=new StreetEnsemblePlan{Options=NeighborhoodPlanner.Copy(o),BaseFingerprint=Fingerprint(project)};plan.Id=ParametricBuildings.Stable(plan.BaseFingerprint+JsonSerializer.Serialize(o));
  var generated=ParametricBuildings.Create(new(){Entrances=o.Entrances,Floors=o.Floors,ApartmentsPerFloor=o.ApartmentsPerFloor,Layout=o.Preset=="courtyard"?"courtyard":o.Preset=="garden"?"staggered":"slab",Facade=o.Preset=="garden"?"wood":o.Preset=="courtyard"?"plaster":"brick",Seed=o.Seed});var b=generated.Building;b.X=o.X+6;b.Y=o.Y+15;
  if(b.X+b.Width+6>o.X+o.Width||b.Y+b.Height+10>o.Y+o.Height)throw new ArgumentException("The requested building and its parking, yard and access paths do not fit this rectangle. Enlarge it or reduce the building parameters.");
  var placement=new ParcelPlacement(project);placement.ReserveEntrances();var reserved=new HashSet<(int X,int Y)>();var propCells=new HashSet<(int X,int Y)>();
  bool Inside(int x,int y)=>x>=o.X&&y>=o.Y&&x<o.X+o.Width&&y<o.Y+o.Height;
  bool Free(int x,int y,bool forPassage=false)=>Inside(x,y)&&placement.Free(x,y,allowRoad:true,forPassage:forPassage);
  foreach(var occupiedIndex in BuildingDesigner.Occupied(b)){var x=b.X+occupiedIndex%(b.Width+1);var y=b.Y+occupiedIndex/(b.Width+1);if(!Free(x,y)||project.Terrain[y*project.Width+x]==Terrain.Road)throw new ArgumentException("The ensemble building overlaps water, an existing object, road or manually authored surface. Move its rectangle.");reserved.Add((x,y));}
  plan.Buildings.Add(b);
  var painted=new Dictionary<(int X,int Y),EnsembleSurface>();
  void Paint(int x,int y,Terrain terrain,string kind,bool required=false){if(!Free(x,y,forPassage:true)||reserved.Contains((x,y))){if(required)throw new ArgumentException("A required street or entrance path crosses water, an existing object or a manually authored surface.");return;}painted[(x,y)]=new(x,y,1,1,terrain,kind);}
  // One continuous street, sidewalk and parking apron form a shared frontage.
  for(var y=o.Y+2;y<o.Y+6;y++)for(var x=o.X+2;x<o.X+o.Width-2;x++)Paint(x,y,Terrain.Road,"street",true);
  for(var x=o.X+2;x<o.X+o.Width-2;x++){Paint(x,o.Y+6,Terrain.Pavement,"sidewalk",true);Paint(x,o.Y+7,Terrain.Pavement,"sidewalk",true);}
  foreach(var edge in b.Design!.Floors[0].Edges.Where(e=>e.Kind=="door"&&e.Edge=="n"&&BuildingDesigner.Cell(b,b.Design.Floors[0],e.X,e.Y-1)=='.')){
   var x=b.X+edge.X;var end=b.Y+edge.Y;var path=new List<EnsemblePoint>();for(var y=o.Y+5;y<end;y++){for(var xx=x-1;xx<=x;xx++)Paint(xx,y,Terrain.Pavement,"access",true);path.Add(new(x,y));reserved.Add((x,y));reserved.Add((x-1,y));}plan.AccessPaths.Add(path);
  }
  if(o.Parking)for(var y=o.Y+9;y<o.Y+13;y++)for(var x=b.X;x<b.X+b.Width;x++)Paint(x,y,Terrain.Pavement,"parking");
  for(var y=b.Y+b.Height+2;y<b.Y+b.Height+8;y++)for(var x=b.X+2;x<b.X+b.Width-2;x++)Paint(x,y,o.Preset=="garden"?Terrain.Grass:Terrain.Dirt,"yard");
  bool Add(string role,int x,int y){var resource=resources.FirstOrDefault(r=>r.Role==role);if(resource is null)return false;VisualStyles.ValidateParts(resource.Asset,resource.Parts);var cells=resource.Parts.Select(p=>(X:x+p.Dx,Y:y+p.Dy)).ToArray();if(cells.Any(c=>!Free(c.X,c.Y)||reserved.Contains(c)||propCells.Contains(c)||painted.TryGetValue(c,out var surface)&&surface.Kind is "street" or "sidewalk" or "access" or "parking"))return false;var d=new WorldDecoration{Id=ParametricBuildings.Stable(plan.Id+role+x+":"+y),X=x,Y=y,Asset=resource.Asset,Parts=NeighborhoodPlanner.Copy(resource.Parts)};plan.Decorations.Add(d);foreach(var c in cells)propCells.Add(c);foreach(var part in resource.Parts)if(resource.ModId is {} owner)plan.AssetSources[part.Tile]=owner;if(resource.ModId is {} mod)plan.AssetSources[resource.Asset]=mod;plan.RequiredMods=plan.RequiredMods.Concat(resource.Requirements??[]).Concat(resource.ModId is {} id?[id]:[]).Distinct().ToList();return true;}
  if(o.Fences){
   var north=resources.FirstOrDefault(r=>r.Role=="fence-n");var west=resources.FirstOrDefault(r=>r.Role=="fence-w");
   if(north is null||west is null||north.Parts.Count!=1||west.Parts.Count!=1)plan.Warnings.Add("Complete directional fence resources were not available; fences were omitted.");
   else{
    var style=new FenceStyle{Id="ensemble-"+ParametricBuildings.Stable(west.Asset+":"+north.Asset),W=west.Parts[0].Tile,N=north.Parts[0].Tile};
    var edges=new List<(int X,int Y,bool North)>();
    void Edge(int x,int y,bool n){if(!Free(x,y)||reserved.Contains((x,y))||propCells.Contains((x,y))||painted.TryGetValue((x,y),out var surface)&&surface.Kind is "street" or "sidewalk" or "access" or "parking")return;edges.Add((x,y,n));}
    for(var x=o.X+2;x<o.X+o.Width-2;x++){Edge(x,o.Y+8,true);Edge(x,o.Y+o.Height-3,true);}for(var y=o.Y+9;y<o.Y+o.Height-3;y++){Edge(o.X+2,y,false);Edge(o.X+o.Width-3,y,false);}
    plan.FenceStyles.Add(style);plan.Fences=ParcelFences.Runs(plan.Id,style,edges);
    foreach(var e in edges)propCells.Add((e.X,e.Y));
    foreach(var r in new[]{north,west}){if(r.ModId is {} owner){plan.AssetSources[r.Asset]=owner;foreach(var part in r.Parts)plan.AssetSources[part.Tile]=owner;}plan.RequiredMods=plan.RequiredMods.Concat(r.Requirements??[]).Concat(r.ModId is {} id?[id]:[]).Distinct().ToList();}
   }
  }
  if(o.Greenery){for(var y=b.Y+2;y<o.Y+o.Height-5;y+=6)for(var x=b.X+b.Width+4;x<o.X+o.Width-5;x+=6)Add("greenery",x,y);for(var x=b.X+2;x<b.X+b.Width-2;x+=6)Add("greenery",x,b.Y+b.Height+8);}
  for(var x=b.X+2;x<b.X+b.Width-3;x+=8)Add("bench",x,b.Y+b.Height+1);
  if(o.Lights){if(nativeLight is not null){foreach(var x in new[]{b.X-2,b.X+b.Width+3}){var m=NeighborhoodPlanner.Copy(nativeLight);m.Id=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plan.Id+"light"+x)))[..32].ToLowerInvariant();m.X=x;m.Y=o.Y+9;m.Z=0;m.OffsetZ=.9;m.Profile="light";m.Light=new(){Radius=10,Rgb=[1,.86,.65],Active=true};var cells=StaticModelPlacement.FootprintCells(m).Select(c=>(X:m.X+c.Dx,Y:m.Y+c.Dy)).ToArray();if(cells.Any(c=>!Free(c.X,c.Y)||reserved.Contains(c)||propCells.Contains(c)||painted.TryGetValue(c,out var surface)&&surface.Kind is "street" or "sidewalk" or "access" or "parking"))continue;Add("lamp",m.X,m.Y);foreach(var c in cells)propCells.Add(c);plan.StaticModels.Add(m);plan.RequiredMods=plan.RequiredMods.Concat(m.RequiredMods).Distinct().ToList();}}else plan.Warnings.Add("No registered lamp model with valid geometry was available. Native lights were omitted.");}
  plan.Surfaces=painted.Values.GroupBy(s=>(s.Y,s.Terrain,s.Kind)).SelectMany(g=>Runs(g.OrderBy(s=>s.X).ToArray())).ToList();
  if(plan.Decorations.Count>3000)throw new ArgumentException("The ensemble exceeds its decoration budget.");return plan;
 }
 static IEnumerable<EnsembleSurface> Runs(EnsembleSurface[] row){for(var i=0;i<row.Length;i++){var first=row[i];var last=first.X;while(i+1<row.Length&&row[i+1].X==last+1){i++;last++;}yield return first with{Width=last-first.X+1};}}
 public static MapProject Apply(MapProject project,StreetEnsemblePlan plan){
  ProjectValidator.Validate(project);if(plan is null||plan.BaseFingerprint!=Fingerprint(project))throw new ArgumentException("The map changed after preview. Generate a fresh ensemble preview before applying it.");Validate(plan.Options,project.Width,project.Height);
  if(plan.Buildings is null||plan.Buildings.Count is <1 or >4||plan.Decorations is null||plan.Fences is null||plan.FenceStyles is null||plan.Fences.Count>4096||plan.FenceStyles.Count>128||plan.Decorations.Count>3000||plan.Surfaces is null||plan.Surfaces.Count>4000||plan.StaticModels is null||plan.StaticModels.Count>32||plan.AccessPaths is null||plan.AccessPaths.Count>8)throw new ArgumentException("Invalid ensemble budget.");
  var o=plan.Options;bool Inside(int x,int y)=>x>=o.X&&y>=o.Y&&x<o.X+o.Width&&y<o.Y+o.Height;var candidate=NeighborhoodPlanner.Copy(project);var placement=new ParcelPlacement(project);placement.ReserveEntrances();var mask=TerrainEditMask.Decode(project);
  void Check(int x,int y,bool forPassage=false){if(!Inside(x,y)||!placement.Free(x,y,allowRoad:true,forPassage:forPassage))throw new ArgumentException("The proposal crosses its preview bounds or overlaps an existing feature.");}
  var buildingCells=new HashSet<(int,int)>();foreach(var b in plan.Buildings){BuildingDesigner.Validate(b);foreach(var index in BuildingDesigner.Occupied(b)){var x=b.X+index%(b.Width+1);var y=b.Y+index/(b.Width+1);Check(x,y);if(!buildingCells.Add((x,y)))throw new ArgumentException("Proposed buildings overlap.");}candidate.Buildings.Add(NeighborhoodPlanner.Copy(b));}
  foreach(var surface in plan.Surfaces){if(surface.Width<1||surface.Height<1||surface.Width>o.Width||surface.Height>o.Height||surface.Terrain is not(Terrain.Road or Terrain.Pavement or Terrain.Dirt or Terrain.Grass)||surface.Kind is not("street" or "sidewalk" or "parking" or "access" or "yard"))throw new ArgumentException("Invalid ensemble surface.");for(var y=surface.Y;y<surface.Y+surface.Height;y++)for(var x=surface.X;x<surface.X+surface.Width;x++){Check(x,y,forPassage:true);if(buildingCells.Contains((x,y)))throw new ArgumentException("A proposed surface overlaps a building.");var index=y*candidate.Width+x;candidate.Terrain[index]=surface.Terrain;}}
  placement.ReservePassage(plan.Surfaces.Where(s=>s.Kind is "street" or "sidewalk" or "access" or "parking").SelectMany(s=>Enumerable.Range(s.Y,s.Height).SelectMany(y=>Enumerable.Range(s.X,s.Width).Select(x=>(X:x,Y:y)))));
  var props=new HashSet<(int,int)>();foreach(var d in plan.Decorations){VisualStyles.ValidateParts(d.Asset,d.Parts);foreach(var part in d.Parts){var point=(d.X+part.Dx,d.Y+part.Dy);Check(point.Item1,point.Item2);if(buildingCells.Contains(point)||!props.Add(point))throw new ArgumentException("A complete prop footprint overlaps a building or another prop.");}candidate.Decorations.Add(NeighborhoodPlanner.Copy(d));}
  foreach(var m in plan.StaticModels){StaticModelPlacement.Validate(m,candidate.Width,candidate.Height,0,0);foreach(var part in StaticModelPlacement.FootprintCells(m)){var point=(m.X+part.Dx,m.Y+part.Dy);Check(point.Item1,point.Item2);if(buildingCells.Contains(point)||!props.Add(point)&&!(m.Profile=="light"&&plan.Decorations.Any(d=>d.Asset.StartsWith("lighting_",StringComparison.Ordinal)&&d.Parts.Any(part=>d.X+part.Dx==point.Item1&&d.Y+part.Dy==point.Item2))))throw new ArgumentException("A complete model footprint overlaps another feature.");}candidate.StaticModels.Add(NeighborhoodPlanner.Copy(m));}
  if(plan.GateStyles is null||plan.GateStyles.Count>128)throw new ArgumentException("Invalid ensemble gate styles.");
  foreach(var style in plan.GateStyles){var prior=candidate.GateStyles.FirstOrDefault(s=>s.Id==style.Id);if(prior is null)candidate.GateStyles.Add(NeighborhoodPlanner.Copy(style));else if(JsonSerializer.Serialize(prior)!=JsonSerializer.Serialize(style))throw new ArgumentException("Ensemble gate style conflicts with the project.");}
  ParcelFences.MergeStyles(candidate,plan.FenceStyles);FenceLines.Validate(plan.Fences,plan.FenceStyles,plan.GateStyles,Math.Max(candidate.Width,candidate.Height));
  if(plan.Fences.SelectMany(f=>f.Points).Any(p=>!Inside(p.X,p.Y)))throw new ArgumentException("A proposed fence extends outside its preview area.");
  foreach(var piece in FenceLines.Compose(plan.Fences,plan.FenceStyles,plan.GateStyles,candidate.Width,candidate.Height).Pieces){Check(piece.X,piece.Y,forPassage:piece.Flags==0);if(buildingCells.Contains((piece.X,piece.Y))||props.Contains((piece.X,piece.Y)))throw new ArgumentException("A proposed fence overlaps another feature.");if(piece.Flags!=0)props.Add((piece.X,piece.Y));}
  candidate.Fences.AddRange(NeighborhoodPlanner.Copy(plan.Fences));
  foreach(var path in plan.AccessPaths){if(path is null||path.Count<1||path.Count>(o.Preset=="emergency-shelter"?o.Width+o.Height:o.Height))throw new ArgumentException("Invalid entrance path.");foreach(var point in path)if(!Inside(point.X,point.Y)||buildingCells.Contains((point.X,point.Y))||props.Contains((point.X,point.Y))||candidate.Terrain[point.Y*candidate.Width+point.X]!=Terrain.Pavement)throw new ArgumentException("A proposed prop blocks pedestrian access.");if(o.Preset=="emergency-shelter"&&path.Zip(path.Skip(1)).Any(pair=>Math.Abs(pair.First.X-pair.Second.X)+Math.Abs(pair.First.Y-pair.Second.Y)!=1))throw new ArgumentException("An emergency pedestrian route must be continuous.");}
  foreach(var s in plan.Surfaces)for(var y=s.Y;y<s.Y+s.Height;y++)for(var x=s.X;x<s.X+s.Width;x++){var i=y*candidate.Width+x;mask[i/8]|=(byte)(1<<(i%8));}
  candidate.TerrainEdits=TerrainEditMask.Encode(mask);candidate.RequiredMods=candidate.RequiredMods.Concat(plan.RequiredMods).Distinct().ToList();foreach(var (id,owner) in plan.AssetSources){if(candidate.AssetSources.TryGetValue(id,out var prior)&&prior!=owner)throw new ArgumentException("Ensemble resource ownership conflicts with the project.");candidate.AssetSources[id]=owner;}candidate.Version=Math.Max(6,candidate.Version);ProjectValidator.Validate(candidate);return candidate;
 }
 static void Validate(StreetEnsembleOptions o,int w,int h){if(o is null||o.X<0||o.Y<0||o.Width is <40 or >256||o.Height is <40 or >256||(long)o.X+o.Width>w||(long)o.Y+o.Height>h||o.Preset is not("residential" or "courtyard" or "garden" or "emergency-shelter"))throw new ArgumentException("Choose a residential, courtyard, garden or emergency-shelter ensemble inside a 40–256 tile rectangle.");ParametricBuildings.ValidateOptions(new(){Entrances=o.Entrances,Floors=o.Floors,ApartmentsPerFloor=o.ApartmentsPerFloor});}
}
