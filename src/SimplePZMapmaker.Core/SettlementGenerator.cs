namespace SimplePZMapmaker.Core;

/// <summary>Additive, resource-independent street frontage generation, including clear pedestrian access.</summary>
public static class SettlementGenerator
{
 readonly record struct Site(int X,int Y,int Dx,int Dy);
 static readonly (int X,int Y)[] Steps=[(0,-1),(1,0),(0,1),(-1,0)];

 /// <summary>Combines density (0–100) with rules.Density. Never removes buildings, props, infrastructure, edits, or resource assignments.</summary>
 public static SettlementPopulationResult Populate(MapProject project,SettlementRules rules,int seed,int density)
 {
  SettlementRules.Validate(rules);
  if(density is <0 or >100)throw new ArgumentException("Settlement density must be between 0 and 100.");
  if(project is null||project.Width is <256 or >2048||project.Height is <256 or >2048||project.Terrain.Length!=project.Width*project.Height)throw new ArgumentException("Invalid settlement terrain dimensions.");
  var result=new SettlementPopulationResult();var effectiveDensity=density*rules.Density/100;
  if(effectiveDensity==0)return result;
  var edited=TerrainEditMask.Decode(project);var occupied=new bool[project.Terrain.Length];var reserved=new bool[occupied.Length];
  void Block(int x,int y){if(x>=0&&y>=0&&x<project.Width&&y<project.Height)occupied[y*project.Width+x]=true;}
  foreach(var b in project.Buildings)foreach(var i in BuildingDesigner.Occupied(b))Block(b.X+i%(b.Width+1),b.Y+i/(b.Width+1));
  foreach(var d in project.Decorations)foreach(var part in d.Parts)Block(d.X+part.Dx,d.Y+part.Dy);
  foreach(var m in project.StaticModels)foreach(var cell in StaticModelPlacement.FootprintCells(m))Block(m.X+cell.Dx,m.Y+cell.Dy);
  foreach(var spawn in project.Spawns)Block(spawn.X,spawn.Y);
  if(project.Generation is {} control)foreach(var area in control.ProtectedAreas){GenerationArea.Validate(area,project);for(var y=area.Y;y<area.Y+area.Height;y++)for(var x=area.X;x<area.X+area.Width;x++)Block(x,y);}
  foreach(var feature in project.LinearFeatures)foreach(var i in LinearFeatureEditor.Rasterize(feature,project.Width,project.Height)){reserved[i]=true;if(feature.Level!=0||feature.Kind is "river" or "railway")occupied[i]=true;}
  if(InfrastructureGeometry.HasFeatures(project)){var infrastructure=InfrastructureGeometry.Build(project);foreach(var t in infrastructure.Tiles)Block(t.X,t.Y);foreach(var h in infrastructure.Holes)Block(h.X,h.Y);}
  // Explicit surface resources remain owned by the author, even on apparently empty terrain.
  bool Free(int x,int y,bool footprint=false)
  {
   if(x<1||y<1||x>=project.Width-1||y>=project.Height-1)return false;var i=y*project.Width+x;
   var street=project.Terrain[i] is Terrain.Road or Terrain.Pavement;
   return !occupied[i]&&project.Terrain[i]!=Terrain.Water&&(!TerrainEditMask.IsEdited(edited,i)&&!project.SurfaceTiles.ContainsKey(i)||!footprint&&street)&&(!footprint||!street&&!reserved[i]);
  }
  var sites=Sites(project).ToArray();
  if(sites.Length==0){result.Warnings.Add("No usable road frontage in the selected area; add roads before generating a settlement.");return result;}
  var ids=project.Buildings.Select(b=>b.Id).ToHashSet(StringComparer.Ordinal);var prototypes=new Dictionary<string,Building>(StringComparer.Ordinal);long floorBudget=project.Buildings.Sum(b=>(long)(b.Design?.Floors.Sum(f=>f.Cells.Length)??0));
  foreach(var site in sites)
  {
   if(project.Buildings.Count>=4000||floorBudget>=19_900_000){result.Warnings.Add("Settlement stopped at the project building or floor-tile budget.");break;}
   var siteSeed=Hash(seed,site.X,site.Y,site.Dx*3+site.Dy);if((uint)siteSeed%100>=effectiveDensity)continue;
   result.AttemptedSites++;
   var id=$"settlement-{seed}-{site.X}-{site.Y}-{site.Dx+1}{site.Dy+1}";if(ids.Contains(id))continue;
   // Most frontage samples are already occupied after the first placements. Reject those before designing interiors.
   if(!Free(site.X+site.Dx*(rules.Setback+3),site.Y+site.Dy*(rules.Setback+3),true))continue;
   var zone=rules.LandUse;var district=Hash(seed,site.X/96,site.Y/96,17);
   if(zone=="mixed")zone=((uint)district%7) switch{0=>"industrial",1=>"services",_=>"residential"};
   var era=rules.Era=="mixed"?new[]{"prewar","soviet","modern"}[(uint)district%3]:rules.Era;
   var face=site.Dx==0?(site.Dy>0?"n":"s"):(site.Dx>0?"w":"e");
   var b=Create(rules,zone,era,siteSeed,face,prototypes);
   b.X=site.Dx==0?site.X-b.Width/2:site.Dx>0?site.X+rules.Setback+1:site.X-rules.Setback-b.Width;
   b.Y=site.Dy==0?site.Y-b.Height/2:site.Dy>0?site.Y+rules.Setback+1:site.Y-rules.Setback-b.Height;
   b.Id=id;b.Modified=false;b.LevelsOrigin="inferred";b.SourceKind="settlement-generator";
   if(b.X<1||b.Y<1||b.X+b.Width>=project.Width||b.Y+b.Height>=project.Height)continue;
   var available=true;
   for(var y=b.Y-1;y<=b.Y+b.Height+1&&available;y++)for(var x=b.X-1;x<=b.X+b.Width+1;x++)if(!Free(x,y,true)){available=false;break;}
   if(!available)continue;
   // Require the configured clearance from every road, including nearby cross streets.
   for(var y=Math.Max(0,b.Y-rules.Setback+1);y<Math.Min(project.Height,b.Y+b.Height+rules.Setback-1)&&available;y++)
    for(var x=Math.Max(0,b.X-rules.Setback+1);x<Math.Min(project.Width,b.X+b.Width+rules.Setback-1);x++)
     if(project.Terrain[y*project.Width+x]==Terrain.Road&&Distance(b,x,y)<rules.Setback){available=false;break;}
   if(!available)continue;
   var paths=new HashSet<int>();
   foreach(var entrance in SettlementBuildingOrientation.Entrances(b))
   {
    var path=FindPath(project,b,entrance,Free,rules.Setback+18);if(path.Count==0){available=false;break;}foreach(var i in path)paths.Add(i);
   }
   if(!available||paths.Count==0)continue;
   var cost=(long)b.Design!.Floors.Count*b.Width*b.Height;if(floorBudget+cost>20_000_000)continue;
   project.Buildings.Add(b);ids.Add(id);floorBudget+=cost;result.PlacedBuildings++;
   foreach(var i in BuildingDesigner.Occupied(b))Block(b.X+i%(b.Width+1),b.Y+i/(b.Width+1));
   foreach(var i in paths){if(project.Terrain[i]!=Terrain.Road&&BuildingDesigner.Cell(b,b.Design.Floors[-b.Design.MinLevel],i%project.Width-b.X,i/project.Width-b.Y)=='.')project.Terrain[i]=Terrain.Pavement;reserved[i]=true;}
   // Rear gardens and shared courts are real reserved ground, so later frontage cannot consume them.
   if(rules.Courtyards)
   {
    var x0=site.Dx>0?b.X+b.Width+2:site.Dx<0?b.X-5:b.X+2;
    var y0=site.Dy>0?b.Y+b.Height+2:site.Dy<0?b.Y-5:b.Y+2;
    var width=site.Dx==0?Math.Max(3,b.Width-4):4;var height=site.Dy==0?Math.Max(3,b.Height-4):4;
    for(var y=y0;y<y0+height;y++)for(var x=x0;x<x0+width;x++)if(Free(x,y,true)){var i=y*project.Width+x;project.Terrain[i]=Terrain.Dirt;reserved[i]=true;}
   }
  }
  if(result.PlacedBuildings==0)result.Warnings.Add("No buildings fitted the free road frontage with the selected setback, floor range, and protected geometry.");
  return result;
 }

 static IEnumerable<Site> Sites(MapProject p)
 {
  // Sample cardinal street edges, not road centers. This works for imported and irregular streets too.
  for(var y=2;y<p.Height-2;y++)for(var x=2;x<p.Width-2;x++)
  {
   if(p.Terrain[y*p.Width+x]!=Terrain.Road)continue;
   foreach(var (dx,dy) in Steps)
    if((dx==0?x:y)%8==0&&p.Terrain[(y+dy)*p.Width+x+dx]!=Terrain.Road)yield return new(x,y,dx,dy);
  }
 }
 static int Distance(Building b,int x,int y)=>Math.Max(b.X-x,Math.Max(0,x-(b.X+b.Width-1)))+Math.Max(b.Y-y,Math.Max(0,y-(b.Y+b.Height-1)));
 static List<int> FindPath(MapProject p,Building b,(int X,int Y) start,Func<int,int,bool,bool> free,int reach)
 {
  var floor=b.Design!.Floors[-b.Design.MinLevel];var walls=floor.Edges.Where(e=>e.Kind is "wall" or "window").Select(e=>(e.X,e.Y,e.Edge)).ToHashSet();
  var stairs=floor.Objects.Where(o=>o.Kind=="stairs").SelectMany(BuildingObjects.Cells).ToHashSet();
  bool Traversable(int x,int y){var local=(X:x-b.X,Y:y-b.Y);var cell=BuildingDesigner.Cell(b,floor,local.X,local.Y);return cell=='.'||floor.Rooms.GetValueOrDefault(cell.ToString())=="hall"&&!stairs.Contains(local);}
  var queue=new Queue<int>();var previous=new Dictionary<int,int>();var first=start.Y*p.Width+start.X;
  if(!free(start.X,start.Y,false))return [];queue.Enqueue(first);previous[first]=first;
  while(queue.TryDequeue(out var current)&&previous.Count<=24000)
  {
   if(p.Terrain[current]==Terrain.Road){var path=new List<int>{current};while(current!=first){current=previous[current];path.Add(current);}return path;}
   var x=current%p.Width;var y=current/p.Width;
   foreach(var (dx,dy) in Steps){var nx=x+dx;var ny=y+dy;var edge=dx==0?(x-b.X,Math.Max(y,ny)-b.Y,"n"):(Math.Max(x,nx)-b.X,y-b.Y,"w");if(nx<Math.Max(1,b.X-reach)||ny<Math.Max(1,b.Y-reach)||nx>Math.Min(p.Width-2,b.X+b.Width+reach)||ny>Math.Min(p.Height-2,b.Y+b.Height+reach)||!free(nx,ny,false)||!Traversable(nx,ny)||walls.Contains(edge))continue;var next=ny*p.Width+nx;if(previous.TryAdd(next,current))queue.Enqueue(next);}
  }
  return [];
 }
 static Building Create(SettlementRules r,string zone,string era,int seed,string face,Dictionary<string,Building> prototypes)
 {
  var rng=new Random(seed);var kind=zone switch{"industrial"=>new[]{"industrial","warehouse","garage"}[rng.Next(3)],"services"=>new[]{"school","shop","clinic"}[rng.Next(3)],"rural"=>rng.Next(5)==0?"warehouse":"house",_=>era=="soviet"?"apartments":rng.Next(3)==0?"apartments":"house"};
  string[] choices=kind switch
  {
   "house" when zone=="rural"=>["village-izba","village-log-long","village-veranda","village-farmstead"],
   "house" when era=="modern"=>["village-veranda","village-brick-cottage","settlement-duplex"],
   "house"=>["village-izba","village-brick-cottage","village-log-long"],
   "apartments" when era=="soviet"=>["urban-panel-five","urban-brick-five","settlement-two-storey"],
   "apartments" when era=="modern"=>["urban-compact-six","urban-townhouse-row","urban-panel-nine"],
   "apartments" when r.Courtyards=>["urban-classic-three","settlement-two-storey","settlement-courtyard","urban-perimeter-four"],
   "apartments"=>["urban-classic-three","settlement-two-storey", "settlement-duplex"],
   "school"=>["settlement-school"],"clinic"=>["settlement-clinic"],"shop"=>["settlement-shop"],
   "warehouse" when zone=="rural"=>["village-barn"],_=>[]
  };
  var templates=HousingTemplates.Catalog.Where(t=>choices.Contains(t.Id)&&t.Levels>=r.MinimumFloors&&t.Levels<=r.MaximumFloors).ToArray();
  Building b;
  if(templates.Length>0){var template=templates[rng.Next(templates.Length)];var key=template.Id+":"+face;if(!prototypes.TryGetValue(key,out var prototype)){prototype=HousingTemplates.Create(template.Id);if(template.Id is "settlement-courtyard" or "urban-perimeter-four"){
    // The library's enclosed courtyard needs a real door from the common hall before it can serve a generated district.
    var ground=prototype.Design!.Floors[-prototype.Design.MinLevel];var gate=ground.Edges.Single(e=>e.X==prototype.Width/2&&e.Y==8&&e.Edge=="n");gate.Kind="door";
   }SettlementBuildingOrientation.Face(prototype,face);prototypes[key]=prototype;}b=NeighborhoodPlanner.Copy(prototype);if(zone=="rural")b.Kind=kind;}
  else
  {
   var width=kind is "apartments" or "industrial" or "warehouse"?24+rng.Next(3)*4:14+rng.Next(3)*2;var height=kind is "industrial" or "warehouse"?20:16;
   b=new(){Kind=kind,Width=width,Height=height,Name=BuildingDesigner.Names[kind]};
   var preferred=zone=="rural"||kind=="house"?1:era=="prewar"?2:era=="soviet"?5:3;
   var levels=Math.Clamp(preferred,r.MinimumFloors,r.MaximumFloors);b.Design=BuildingDesigner.Generate(b,levels,seed);SettlementAccess.ConnectLandings(b,seed);SettlementBuildingOrientation.Face(b,face);
  }
  b.Design!.Variant=seed;
  var style=zone=="industrial"?"industrial":era=="modern"?"modern":era=="soviet"?"modern":b.Kind=="house"?"cottage":"brick";
  VisualStyles.Apply(b,style);
  foreach(var f in b.Design.Floors)foreach(var e in f.Edges){e.WallTile=null;e.OpeningTile=null;e.OverlayTile=null;}
  return b;
 }
 static int Hash(int seed,int x,int y,int salt){unchecked{uint h=(uint)seed^0x9e3779b9u;h=(h^(uint)x)*0x85ebca6bu;h=(h^(uint)y)*0xc2b2ae35u;h=(h^(uint)salt)*0x27d4eb2du;h^=h>>16;return(int)h;}}
}
