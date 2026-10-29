using System.Security.Cryptography;
using System.Text.Json;
namespace SimplePZMapmaker.Core;

public sealed class ParcelGenerationState {
 public Dictionary<string,string> Objects {get;set;}=[];
 public Dictionary<int,ParcelGroundChange> Ground {get;set;}=[];
}
public sealed record ParcelGroundChange(Terrain Before,Terrain After);
public sealed record ParcelGenerationPreview(MapProject Project,List<string> Warnings,List<List<(int X,int Y)>> Paths);
public sealed record ParcelRegionalResources(RuralAppearance? Appearance=null,RuralPalette? Yard=null);

/// <summary>Surveyed plots are completed additively. Source houses never move implicitly.</summary>
public static class ParcelGenerator
{
 static string Hash<T>(T value)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
 static string Id(string source,string role)=>"parcel-"+ParametricBuildings.Stable(source+"|"+role);
 public static ParcelGenerationPreview Preview(MapProject project,GenerationArea area,int seed,bool regenerate=false,bool[]? reservations=null,ParcelRegionalResources? resources=null){
  ProjectValidator.Validate(project);GenerationArea.Validate(area,project);var p=NeighborhoodPlanner.Copy(project);var warnings=new List<string>();var paths=new List<List<(int,int)>>();
  var selected=p.Sites.Where(s=>s.Boundary[0].Any(v=>v.X>=area.X&&v.Y>=area.Y&&v.X<area.X+area.Width&&v.Y<area.Y+area.Height)).ToArray();
  if(selected.Length==0)warnings.Add("No surveyed parcels in this area. Import parcel boundaries or draw a site first.");
  var road=new HashSet<int>();for(var i=0;i<p.Terrain.Length;i++)if(p.Terrain[i]==Terrain.Road)road.Add(i);
  foreach(var line in p.LinearFeatures.Where(f=>f.Kind=="road"&&f.Level==0))road.UnionWith(LinearFeatureEditor.Rasterize(line,p.Width,p.Height));
  var streetDistance=Enumerable.Repeat(int.MaxValue,p.Terrain.Length).ToArray();var streetQueue=new Queue<int>();foreach(var i in road){streetDistance[i]=0;streetQueue.Enqueue(i);}
  while(streetQueue.TryDequeue(out var at)){if(streetDistance[at]>=64)continue;var x=at%p.Width;var y=at/p.Width;foreach(var (dx,dy) in new[]{(0,-1),(1,0),(0,1),(-1,0)}){var xx=x+dx;var yy=y+dy;if(xx<0||yy<0||xx>=p.Width||yy>=p.Height)continue;var next=yy*p.Width+xx;if(streetDistance[next]<=streetDistance[at]+1)continue;streetDistance[next]=streetDistance[at]+1;streetQueue.Enqueue(next);}}
  var edits=TerrainEditMask.Decode(p);var totalGround=0;
  warnings.AddRange(resources?.Appearance?.Warnings??[]);warnings.AddRange(resources?.Yard?.Warnings??[]);
  foreach(var site in selected){
   if(site.Locked){warnings.Add($"{site.Name}: locked parcel preserved.");continue;}
   if(site.Boundary.SelectMany(r=>r).Any(v=>v.X<area.X||v.Y<area.Y||v.X>area.X+area.Width||v.Y>area.Y+area.Height)){warnings.Add($"{site.Name}: parcel crosses the selected area or section. Keep full boundary evidence; complete it in a common world frame.");continue;}
   if(regenerate&&site.ParcelGeneration is {} previous)Restore(p,site,previous,warnings);
   SiteModel.DetachMissingMembers(p);var placement=new ParcelPlacement(p);placement.ReserveEntrances();
   bool Free(int x,int y,bool passage=false)=>placement.Free(x,y,allowRoad:passage,forPassage:passage)&&(reservations is null||!reservations[y*p.Width+x]);
   var minX=Math.Max(area.X,(int)Math.Floor(site.Boundary[0].Min(v=>v.X)));var maxX=Math.Min(area.X+area.Width-1,(int)Math.Ceiling(site.Boundary[0].Max(v=>v.X)));var minY=Math.Max(area.Y,(int)Math.Floor(site.Boundary[0].Min(v=>v.Y)));var maxY=Math.Min(area.Y+area.Height-1,(int)Math.Ceiling(site.Boundary[0].Max(v=>v.Y)));
   if((long)(maxX-minX+1)*(maxY-minY+1)>100_000){warnings.Add($"{site.Name}: parcel exceeds the 100,000-cell completion budget.");continue;}
   var cells=new HashSet<int>();for(var y=minY;y<=maxY;y++)for(var x=minX;x<=maxX;x++)if(SiteModel.Contains(site,x+.5,y+.5))cells.Add(y*p.Width+x);
   if(cells.Count<16){warnings.Add($"{site.Name}: parcel is too small for a yard.");continue;}
   var boundary=new List<(int X,int Y,bool North)>();foreach(var i in cells){var x=i%p.Width;var y=i/p.Width;if(!cells.Contains(i-p.Width))boundary.Add((x,y,true));if(!cells.Contains(i+p.Width))boundary.Add((x,y+1,true));if(!cells.Contains(i-1)||x==0)boundary.Add((x,y,false));if(!cells.Contains(i+1)||x==p.Width-1)boundary.Add((x+1,y,false));}
   var houses=p.Buildings.Where(b=>site.BuildingIds.Contains(b.Id)&&b.SourceKind!="parcel-generator").ToArray();
   if(houses.Length==0){warnings.Add($"{site.Name}: no surveyed main building; no house was invented.");continue;}
   var house=houses.OrderByDescending(b=>b.Width*b.Height).First();
   // A bounded exterior search finds a street, including tagged unpaved roads.
   var frontage=boundary.Where(e=>e.X>=0&&e.Y>=0&&e.X<p.Width&&e.Y<p.Height&&streetDistance[e.Y*p.Width+e.X]<=64&&Free(e.X,e.Y,true)).OrderBy(e=>streetDistance[e.Y*p.Width+e.X]).ThenBy(e=>Math.Abs(e.X-house.X)+Math.Abs(e.Y-house.Y)).Take(32).Select(e=>(Edge:e,Path:StreetPath(p,e.X,e.Y,road,placement,64)))
    .Where(e=>e.Path.Count>0).OrderBy(e=>e.Path.Count).ThenBy(e=>Math.Abs(e.Edge.X-house.X)+Math.Abs(e.Edge.Y-house.Y)).FirstOrDefault();
   if(frontage.Path is null){warnings.Add($"{site.Name}: no unobstructed street frontage within 64 tiles; no speculative street was added.");continue;}
   var gate=frontage.Edge;
   var axisX=gate.North?0:1;var axisY=gate.North?1:0;var centreX=cells.Average(i=>i%p.Width);var centreY=cells.Average(i=>i/p.Width);
   if(axisX!=0&&centreX<gate.X)axisX=-1;if(axisY!=0&&centreY<gate.Y)axisY=-1;
   var source=site.SourceId??site.Id;var generation=site.ParcelGeneration??new();var objectCountBefore=generation.Objects.Count;
   var allAccess=new List<List<(int,int)>>{frontage.Path};
   var gateGoals=new HashSet<int>{gate.Y*p.Width+gate.X};
   foreach(var b in houses)foreach(var entrance in SettlementBuildingOrientation.Entrances(b)){var access=ParcelPath(p,entrance,gateGoals,placement,cells);if(access.Count>0){placement.ReservePassage(access);allAccess.Add(access);}}
   void Ground(int x,int y,Terrain terrain){if(!cells.Contains(y*p.Width+x)||!Free(x,y,true))return;var i=y*p.Width+x;if(p.Terrain[i]==terrain)return;if(totalGround++>=200_000)return;if(!generation.Ground.TryGetValue(i,out var prior))generation.Ground[i]=new(p.Terrain[i],terrain);else generation.Ground[i]=prior with{After=terrain};p.Terrain[i]=terrain;}
   // Ancillaries sit behind the main house, not in the street-side garden.
   var depth=axisX!=0?maxX-minX:maxY-minY;var mainDepth=(house.X+house.Width/2d-gate.X)*axisX+(house.Y+house.Height/2d-gate.Y)*axisY;
   foreach(var (role,w,h) in new[]{("shed",4,4),("bathhouse",3,3),("toilet",2,2),("greenhouse",3,5)}){
    var id=Id(source,role);if(p.Buildings.Any(b=>b.Id==id))continue;
    var spots=cells.Where(i=>{var x=i%p.Width;var y=i/p.Width;var d=(x-gate.X)*axisX+(y-gate.Y)*axisY;return d>mainDepth+house.Height/2+4&&d<depth*.8;}).OrderBy(i=>ParametricBuildings.Stable(source+"|"+role+"|"+(i%p.Width)+","+(i/p.Width)+"|"+seed),StringComparer.Ordinal).Take(4096);
    foreach(var i in spots){var b=new Building{Id=id,X=i%p.Width,Y=i/p.Width,Width=w,Height=h,Kind=role is "shed" or "greenhouse"?"warehouse":"house",Name=role,SourceId="gen-"+ParametricBuildings.Stable(source)+"-"+role+"-0",SourceKind="parcel-generator",LevelsOrigin="inferred"};b.Design=BuildingDesigner.Generate(b,1,BitConverter.ToInt32(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source+role+seed)),0));if(role is "bathhouse" or "toilet")foreach(var f in b.Design.Floors)foreach(var key in f.Rooms.Keys.ToArray())f.Rooms[key]="bathroom";
     if(role is "bathhouse" or "toilet")for(var z=0;z<b.Design.Floors.Count;z++)BuildingDesigner.Furnish(b,z,new Random(b.Design.Variant));
     if(resources?.Appearance is {} appearance)b=TransferRegionalProfiles.ApplyRuralAppearance(b,appearance);
     if(!SiteModel.FullyContainsBuilding(site,b)||!BuildingDesigner.Occupied(b).All(c=>Free(b.X+c%(w+1),b.Y+c/(w+1)))||!MapGenerator.CanPlace(p,b))continue;
     p.Buildings.Add(b);var afterPlacement=new ParcelPlacement(p);var access=SettlementBuildingOrientation.Entrances(b).Select(e=>ParcelPath(p,e,gateGoals,afterPlacement,cells)).ToArray();if(access.Length==0||access.Any(path=>path.Count==0)){p.Buildings.Remove(b);continue;}placement.Add(b);foreach(var path in access){placement.ReservePassage(path);allAccess.Add(path);}site.BuildingIds.Add(id);generation.Objects["building:"+id]=Hash(b);break;
    }
   }
   // Reserve every actual door-to-gate route before garden surfaces or fences.
   foreach(var b in p.Buildings.Where(b=>site.BuildingIds.Contains(b.Id)))foreach(var entrance in SettlementBuildingOrientation.Entrances(b)){
    var path=ParcelPath(p,entrance,gateGoals,placement,cells);if(path.Count==0){warnings.Add($"{site.Name}/{b.Name}: an entrance has no free gate route; existing geometry retained.");continue;}
    placement.ReservePassage(path);allAccess.Add(path);foreach(var point in path)Ground(point.X,point.Y,Terrain.Pavement);
   }
   placement.ReservePassage(frontage.Path);paths.AddRange(allAccess);
   // Small front gardens and domestic yard details are inferred, never evidence.
   foreach(var (role,asset,front) in new[]{("woodpile","crafted_02_85",false),("flower-0","vegetation_ornamental_01_16",true),("flower-1","vegetation_ornamental_01_17",true),("flower-2","vegetation_ornamental_01_18",true)}){
    var id=Id(source,role);if(p.Decorations.Any(d=>d.Id==id))continue;
    foreach(var i in cells.Order()){var x=i%p.Width;var y=i/p.Width;var d=(x-gate.X)*axisX+(y-gate.Y)*axisY;
     if(!Free(x,y)||!(front?d>=1&&d<=4:d>mainDepth+4&&d<mainDepth+18))continue;
     var decoration=new WorldDecoration{Id=id,X=x,Y=y,Asset=asset,Parts=[new(0,0,asset,asset=="crafted_02_85"?(byte)1:(byte)0)]};p.Decorations.Add(decoration);placement.Add(decoration);site.DecorationIds.Add(id);generation.Objects["decoration:"+id]=Hash(decoration);break;
    }
   }
   foreach(var i in cells){var x=i%p.Width;var y=i/p.Width;var d=(x-gate.X)*axisX+(y-gate.Y)*axisY;
    if(placement.Passage(x,y))continue;if(d>depth*.65&&d<depth-2&&((axisX==0?y:x)%4)<2)Ground(x,y,Terrain.Dirt);
    else if(d>mainDepth+2&&d<Math.Min(depth*.65,mainDepth+15))Ground(x,y,Terrain.Dirt);
   }
   var streetStyle=resources?.Yard?.Fence??ParcelFences.Picket;
   ParcelFences.MergeStyles(p,[ParcelFences.Picket,streetStyle]);
   if(resources?.Yard is {} palette){foreach(var (tile,owner) in palette.AssetSources){if(p.AssetSources.TryGetValue(tile,out var priorOwner)&&priorOwner!=owner)throw new ArgumentException("Parcel resource owner conflicts with the project.");p.AssetSources[tile]=owner;}p.RequiredMods=p.RequiredMods.Concat(palette.RequiredMods).Distinct().ToList();}
   // A low garden fence separates the 2–4 tile front strip; every reserved path stays open.
   var gardenEdges=cells.Select(i=>(X:i%p.Width,Y:i/p.Width)).Where(c=>Math.Abs((c.X-gate.X)*axisX+(c.Y-gate.Y)*axisY-4)<.5&&Free(c.X,c.Y)&&!placement.Passage(c.X,c.Y)).Select(c=>(c.X,c.Y,North:gate.North));
   foreach(var garden in ParcelFences.Runs(Id(source,"front-garden"),ParcelFences.Picket,gardenEdges))if(generation.Objects.Count<240&&!p.Fences.Any(f=>f.Id==garden.Id)){garden.SourceId="gen-"+ParametricBuildings.Stable(source)+"-garden";garden.SourceTags["pz:origin"]="inferred front garden";p.Fences.Add(garden);generation.Objects["fence:"+garden.Id]=Hash(garden);}
   if(p.GateStyles.All(g=>g.Id!=GeographicImporter.DefaultWicket.Id))p.GateStyles.Add(NeighborhoodPlanner.Copy(GeographicImporter.DefaultWicket));
   var prefix=Id(source,"fence");var edges=boundary.Where(e=>e.X<p.Width&&e.Y<p.Height&&Free(e.X,e.Y)&&!placement.Passage(e.X,e.Y)).ToList();
   // Leave a three-tile vehicle opening plus a real native one-tile wicket.
   var gaps=new HashSet<(int,int,bool)>();for(var n=0;n<3;n++)gaps.Add((gate.X+(gate.North?n:0),gate.Y+(gate.North?0:n),gate.North));
   var wicketEdge=(gate.X+(gate.North?3:0),gate.Y+(gate.North?0:3),gate.North);
   edges=edges.Where(e=>!gaps.Contains(e)).ToList();
   foreach(var line in ParcelFences.Runs(prefix,streetStyle,edges)){
    if(generation.Objects.Count>=250){warnings.Add($"{site.Name}: fence detail exceeds the parcel object budget; remaining edges are not generated.");break;}
    if(p.Fences.Any(f=>f.Id==line.Id))continue;var start=line.Points[0];var finish=line.Points[^1];var at=gate.North?wicketEdge.Item1-start.X:wicketEdge.Item2-start.Y;
    if((line.Points[0].Y==line.Points[^1].Y)==gate.North&&at>=0&&at<Math.Abs(finish.X-start.X)+Math.Abs(finish.Y-start.Y)&&((gate.North&&start.Y==wicketEdge.Item2)||(!gate.North&&start.X==wicketEdge.Item1)))line.Gates.Add(new(){At=at,Style=GeographicImporter.DefaultWicket.Id});
    line.SourceId="gen-"+ParametricBuildings.Stable(source)+"-fence";line.SourceTags["pz:origin"]="inferred parcel completion";p.Fences.Add(line);generation.Objects["fence:"+line.Id]=Hash(line);
   }
   if(objectCountBefore==0)warnings.Add($"Rural yards inferred from the regional profile, not surveyed interiors. Vehicle entrance is a 3-tile opening; a closed vehicle gate needs a configured matching wide gate style.");
   site.ParcelGeneration=generation;
  }
  SiteModel.DetachMissingMembers(p);ProjectValidator.Validate(p);return new(p,warnings.Distinct().Take(128).ToList(),paths);
 }
 static List<(int X,int Y)> StreetPath(MapProject p,int x,int y,HashSet<int> road,ParcelPlacement placement,int reach)=>Find(p,(x,y),road,placement,null,reach);
 static List<(int X,int Y)> ParcelPath(MapProject p,(int X,int Y) start,HashSet<int> goal,ParcelPlacement placement,HashSet<int> cells)=>Find(p,start,goal,placement,cells,128);
 static List<(int X,int Y)> Find(MapProject p,(int X,int Y) start,HashSet<int> goal,ParcelPlacement placement,HashSet<int>? cells,int reach){
  var q=new Queue<(int X,int Y)>();var previous=new Dictionary<(int,int),(int,int)>();if(!placement.Inside(start.X,start.Y))return [];q.Enqueue(start);previous[start]=start;
  while(q.TryDequeue(out var at)&&previous.Count<16_384){if(goal.Contains(at.Y*p.Width+at.X)){var result=new List<(int,int)>{at};while(at!=start){at=previous[at];result.Add(at);}result.Reverse();return result;}
   foreach(var (dx,dy) in new[]{(0,-1),(1,0),(0,1),(-1,0)}){var next=(X:at.X+dx,Y:at.Y+dy);if(!placement.Inside(next.X,next.Y)||Math.Abs(next.X-start.X)+Math.Abs(next.Y-start.Y)>reach||previous.ContainsKey(next)||cells is not null&&!cells.Contains(next.Y*p.Width+next.X)&&!goal.Contains(next.Y*p.Width+next.X)||!goal.Contains(next.Y*p.Width+next.X)&&!placement.Free(next.X,next.Y,true,true))continue;previous[next]=at;q.Enqueue(next);}
  }return [];
 }
 static void Restore(MapProject p,SiteParcel site,ParcelGenerationState state,List<string> warnings){
  var retained=new Dictionary<string,string>();var lockedCells=SiteModel.LockedCells(p).ToHashSet();bool Protected(int x,int y)=>x>=0&&y>=0&&x<p.Width&&y<p.Height&&lockedCells.Contains(y*p.Width+x)||p.Generation?.ProtectedAreas.Any(a=>a.Contains(x,y))==true;
  foreach(var (key,hash) in state.Objects){var parts=key.Split(':',2);if(parts.Length!=2)continue;var id=parts[1];
   if(parts[0]=="building"&&p.Buildings.FirstOrDefault(b=>b.Id==id) is {} b){
    if(!site.BuildingIds.Contains(id)||!SiteModel.FullyContainsBuilding(site,b)){warnings.Add($"{b.Name}: detached or moved member retained outside generator ownership.");continue;}
    var pinned=BuildingDesigner.Occupied(b).Any(c=>Protected(b.X+c%(b.Width+1),b.Y+c/(b.Width+1)))||p.Generation?.LockedBuildingIds.Contains(id)==true||p.Generation?.ProtectedAreas.Any(a=>a.Intersects(b))==true||p.Sites.Any(s=>s.Locked&&s.BuildingIds.Contains(id));
    if(!pinned&&!b.Modified&&Hash(b)==hash)p.Buildings.Remove(b);else{retained[key]=hash;warnings.Add($"{b.Name}: pinned, protected or edited generated building preserved.");}
   }
   if(parts[0]=="fence"&&p.Fences.FirstOrDefault(f=>f.Id==id) is {} f){if(Hash(f)==hash&&!FenceLines.Compose([f],p.FenceStyles,p.GateStyles,p.Width,p.Height).Pieces.Any(piece=>Protected(piece.X,piece.Y)))p.Fences.Remove(f);else retained[key]=hash;}
   if(parts[0]=="decoration"&&p.Decorations.FirstOrDefault(d=>d.Id==id) is {} d){if(!site.DecorationIds.Contains(id)||d.Parts.Any(part=>!SiteModel.Contains(site,d.X+part.Dx+.5,d.Y+part.Dy+.5)))continue;if(Hash(d)==hash&&!d.Parts.Any(part=>Protected(d.X+part.Dx,d.Y+part.Dy))&&!p.Sites.Any(s=>s.Locked&&s.DecorationIds.Contains(id)))p.Decorations.Remove(d);else retained[key]=hash;}
  }
  var edits=TerrainEditMask.Decode(p);var ground=new Dictionary<int,ParcelGroundChange>();foreach(var (i,c) in state.Ground){if(i>=0&&i<p.Terrain.Length&&SiteModel.Contains(site,i%p.Width+.5,i/p.Width+.5)&&p.Terrain[i]==c.After&&!TerrainEditMask.IsEdited(edits,i)&&!p.SurfaceTiles.ContainsKey(i)&&!Protected(i%p.Width,i/p.Width))p.Terrain[i]=c.Before;else if(i>=0&&i<p.Terrain.Length&&SiteModel.Contains(site,i%p.Width+.5,i/p.Width+.5))ground[i]=c;}
  state.Objects=retained;state.Ground=ground;
 }
}
