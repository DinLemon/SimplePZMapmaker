using System.Security.Cryptography;
using System.Text;
namespace SimplePZMapmaker.Core;

public sealed record FuelStationSource(string SourceId,IReadOnlyList<IReadOnlyList<(double X,double Y)>> Rings,bool Polygon,bool ExplicitPump=false);
public sealed record FuelStationPlacement(string SourceId,WorldDecoration Pump,string Facing,ZoneRectangle Forecourt,ZoneRectangle Access,ZoneRectangle Connection,bool InferredBoundary);
public sealed record FuelStationPlan(IReadOnlyList<FuelStationPlacement> Placements,int PreservedPumps,int SkippedSources,long Work,bool Limited,IReadOnlyList<string> Notes);

/// <summary>One native gasoline pump per source, on a wholly contained 4x5 (E) or 5x4 (S) forecourt.
/// Its 3x5/5x3 driving strip joins an existing clear 3x3 ground-road patch by a straight corridor.
/// No game state is initialized here: the native sprite's fuelAmount, sandbox and electricity rules apply.</summary>
public static class FuelStationLayout
{
 public const string PumpEast="location_shop_fossoil_01_12",PumpSouth="location_shop_fossoil_01_14";
 public const int MaximumSources=128,MaximumVertices=1024,MaximumBoundsTiles=16384,MaximumCandidatesPerSource=2048,MaximumConnectionDistance=24,InferredBoundarySize=12,MaximumNotes=64;
 public const long MaximumWork=2_000_000;
 public static string SourcePrefix(string sourceId)=>"fuel-"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId)))[..32]+"-";
 /// <summary>Strict native single-part pump shape, for the parent's asset-validation integration.</summary>
 public static bool IsNativePump(WorldDecoration d)=>d.Id.StartsWith("fuel-",StringComparison.Ordinal)&&(d.Asset is PumpEast or PumpSouth)&&d.Parts is [{Dx:0,Dy:0,Flags:1}]&&d.Parts[0].Tile==d.Asset;
 // The digest records the generated baseline without adding a second decoration model. Authored
 // movement/resource edits keep their ID, so an inactive source cannot silently erase those edits.
 static string Fingerprint(WorldDecoration d)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new{d.X,d.Y,d.Asset,d.Parts}))))[..16];
 public static bool IsUnchangedGeneratedPump(WorldDecoration d)=>IsNativePump(d)&&d.Id.Length==59&&d.Id.AsSpan(38,5).SequenceEqual("0-v1-")&&d.Id[43..]==Fingerprint(d);

 sealed record Area(FuelStationSource Source,(double X,double Y)[][] Rings,ZoneRectangle Bounds,int Existing);
 public static FuelStationPlan Plan(MapProject project,IReadOnlyList<FuelStationSource> sources,long workBudget=MaximumWork,CancellationToken cancellationToken=default)
 {
  workBudget=Math.Clamp(workBudget,0,MaximumWork);long work=0;var limited=false;var notes=new List<string>();var output=new List<FuelStationPlacement>();int preserved=0,skipped=Math.Max(0,sources.Count-MaximumSources);
  var reasons=new SortedDictionary<string,int>(StringComparer.Ordinal);
  void Note(string value){if(notes.Count<MaximumNotes-2)notes.Add(value);}
  void Skip(string reason){skipped++;reasons[reason]=reasons.GetValueOrDefault(reason)+1;}
  bool Spend(long amount){cancellationToken.ThrowIfCancellationRequested();if(amount>workBudget-work){limited=true;return false;}work+=amount;return true;}
  Note("Native gasoline profile: fuelAmount=20000 comes from the game sprite; fuel availability, power and sandbox settings remain native. No forced fuel, power or initLua.");
  FuelStationPlan Finish(){if(sources.Count>MaximumSources)limited=true;if(reasons.Count>0)notes.Add("Fuel sources skipped: "+string.Join("; ",reasons.Select(r=>$"{r.Key} ({r.Value})"))+".");
   notes.Add($"Fuel stations: {output.Count} new pumps, {preserved} preserved pumps (authored IDs retained; access not revalidated), {skipped} skipped sources; work {work}/{workBudget}"+(limited?"; bounded limit reached.":"."));
   return new(output,preserved,skipped,work,limited,notes);}
  // Do not truncate away explicit pump observations and then invent a station pump in their place.
  if(sources.Count>MaximumSources){skipped=sources.Count;reasons["source limit; no partial station inference"]=sources.Count;return Finish();}
  if(!Spend(sources.Count)){skipped=sources.Count;return Finish();}
  if(!Spend(project.Decorations.Count)){skipped=sources.Count;return Finish();}
  var existing=project.Decorations.Where(d=>d.Id.Length>=38&&d.Id.StartsWith("fuel-",StringComparison.Ordinal)).GroupBy(d=>d.Id[..38]).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
  var areas=new List<Area>();var seen=new HashSet<string>(StringComparer.Ordinal);
  foreach(var source in sources.Take(MaximumSources).OrderByDescending(s=>s.ExplicitPump).ThenBy(s=>s.SourceId,StringComparer.Ordinal)){
   if(string.IsNullOrWhiteSpace(source.SourceId)||source.SourceId.Length>100||source.SourceId.Any(char.IsControl)){Skip("invalid source identity");continue;}
   if(!seen.Add(source.SourceId))continue;var count=existing.GetValueOrDefault(SourcePrefix(source.SourceId));preserved+=count;
   if(source.Rings.Count==0||source.Polygon&&source.ExplicitPump){Skip("unsupported source geometry");continue;}
   long vertices=0;if(source.Rings.Count>MaximumVertices){Skip("geometry ring limit");continue;}
   foreach(var ring in source.Rings){vertices+=ring.Count;if(vertices>MaximumVertices)break;}
   if(vertices>MaximumVertices||!Spend(vertices)){Skip("geometry/work budget");if(limited)break;continue;}
   if(source.Rings.Any(r=>r.Count<(source.Polygon?4:1)||r.Any(v=>!double.IsFinite(v.X)||!double.IsFinite(v.Y)||Math.Abs(v.X)>2_000_000||Math.Abs(v.Y)>2_000_000))||
    !source.Polygon&&(source.Rings.Count!=1||source.Rings[0].Count!=1)||source.Polygon&&source.Rings.Any(r=>r[0]!=r[^1])){Skip("invalid rings or non-point POI");continue;}
   static double Snap(double v)=>Math.Abs(v-Math.Round(v))<1e-7?Math.Round(v):v;
   var rings=source.Rings.Select(r=>r.Select(v=>(X:Snap(v.X),Y:Snap(v.Y))).ToArray()).ToArray();ZoneRectangle bounds;
   if(source.Polygon){var outer=rings[0];var left=(int)Math.Clamp(Math.Floor(outer.Min(v=>v.X)),0,project.Width);var top=(int)Math.Clamp(Math.Floor(outer.Min(v=>v.Y)),0,project.Height);
    bounds=new(left,top,(int)Math.Clamp(Math.Ceiling(outer.Max(v=>v.X)),0,project.Width)-left,(int)Math.Clamp(Math.Ceiling(outer.Max(v=>v.Y)),0,project.Height)-top);
   }else{var point=rings[0][0];bounds=new((int)Math.Floor(point.X)-6,(int)Math.Floor(point.Y)-6,InferredBoundarySize,InferredBoundarySize);}
   if(bounds.Width<1||bounds.Height<1||(long)bounds.Width*bounds.Height>MaximumBoundsTiles){Skip("empty or oversized station bounds");continue;}
   areas.Add(new(source,rings,bounds,count));
  }
  if(limited)return Finish();
  if(areas.All(a=>a.Existing>0))return Finish();
  // One shared occupancy policy for every object, entrance route and all temporary reservations.
  var placement=new ParcelPlacement(project);placement.ReserveEntrances();var vehicles=project.GameZones.Where(z=>z.Vehicle).ToArray();
  bool Free(int x,int y,bool allowRoad){
   if(!Spend(1)||!placement.Free(x,y,allowRoad)||project.Terrain[y*project.Width+x]==Terrain.Forest)return false;
   foreach(var zone in vehicles){if(!Spend(1+(zone.Polygon?.Count??0)))return false;
    if(ZoneGeometry.Covers(zone,x,y))return false;}
   return true;
  }
  bool FreeRectangle(ZoneRectangle r,bool allowRoad){for(var y=r.Y;y<r.Y+r.Height;y++)for(var x=r.X;x<r.X+r.Width;x++)if(!Free(x,y,allowRoad))return false;return true;}
  bool Contains(Area area,ZoneRectangle r){
   if(r.X<area.Bounds.X||r.Y<area.Bounds.Y||(long)r.X+r.Width>(long)area.Bounds.X+area.Bounds.Width||(long)r.Y+r.Height>(long)area.Bounds.Y+area.Bounds.Height)return false;
   if(!area.Source.Polygon)return true;if(!Spend(area.Rings.Sum(ring=>(long)ring.Length)))return false;
   return WholeRectangle(area.Rings,r);
  }
  bool Associated(Area station,Area point){
   var (x,y)=point.Rings[0][0];if(x<station.Bounds.X||y<station.Bounds.Y||x>=(long)station.Bounds.X+station.Bounds.Width||y>=(long)station.Bounds.Y+station.Bounds.Height)return false;
   if(!station.Source.Polygon)return true;if(!Spend(station.Rings.Sum(r=>(long)r.Length)))return false;
   return ContainsFilledPoint(station.Rings,x,y);
  }
  ZoneRectangle? Connect(Area area,ZoneRectangle access,bool vertical){
   foreach(var sign in new[]{-1,1})for(var depth=1;depth<=MaximumConnectionDistance&&!limited;depth++){
    var row=vertical?new ZoneRectangle(access.X,sign<0?access.Y-depth:access.Y+access.Height+depth-1,3,1):new ZoneRectangle(sign<0?access.X-depth:access.X+access.Width+depth-1,access.Y,1,3);
    if(!FreeRectangle(row,true))break;
    if(area.Source.Polygon&&area.Rings.Length>1){if(!Spend(area.Rings.Sum(r=>(long)r.Length)))break;if(!AvoidsHoles(area.Rings,row))break;}
    if(Cells(row).All(c=>project.Terrain[c.Y*project.Width+c.X]==Terrain.Road)){
     var terminal=vertical?new ZoneRectangle(row.X,sign<0?row.Y-2:row.Y,3,3):new ZoneRectangle(sign<0?row.X-2:row.X,row.Y,3,3);
     if(FreeRectangle(terminal,true)&&Cells(terminal).All(c=>project.Terrain[c.Y*project.Width+c.X]==Terrain.Road))
      return vertical?new(access.X,sign<0?row.Y:access.Y+access.Height,3,depth):new(sign<0?row.X:access.X+access.Width,access.Y,depth,3);
    }
   }
   return null;
  }
  FuelStationPlacement? Candidate(Area area,int x,int y,string facing){
   var vertical=facing=="E";var forecourt=vertical?new ZoneRectangle(x,y-2,4,5):new ZoneRectangle(x-2,y,5,4);
   if(!Contains(area,forecourt)||!FreeRectangle(forecourt,false))return null;
   var access=vertical?new ZoneRectangle(x+1,y-2,3,5):new ZoneRectangle(x-2,y+1,5,3);var connection=Connect(area,access,vertical);if(connection is null)return null;
   var tile=vertical?PumpEast:PumpSouth;var pump=new WorldDecoration{Id=SourcePrefix(area.Source.SourceId)+"0",X=x,Y=y,Asset=tile,Parts=[new(0,0,tile,1)]};
   pump.Id=SourcePrefix(area.Source.SourceId)+"0-v1-"+Fingerprint(pump);
   return new(area.Source.SourceId,pump,facing,forecourt,access,connection,!area.Source.Polygon);
  }
  var visited=0;foreach(var area in areas){
   if(limited)break;visited++;if(area.Existing>0)continue;
   if(!Spend(areas.Count))break;
   // An explicit surveyed pump suppresses automatic station-centre estimates, even if it cannot be placed safely.
   if(!area.Source.ExplicitPump&&areas.Any(a=>a.Source.ExplicitPump&&Associated(area,a))){Note($"Fuel {area.Source.SourceId}: explicit pump source supplied; no additional station pump inferred.");continue;}
   if(!area.Source.Polygon&&!area.Source.ExplicitPump&&areas.Any(a=>a.Source.Polygon&&Associated(a,area))){Note($"Fuel {area.Source.SourceId}: point POI belongs to a station polygon; no duplicate pump inferred.");continue;}
   if(project.Decorations.Count+output.Count>=ProjectValidator.MaximumDecorations){Skip("decoration capacity");continue;}
   FuelStationPlacement? chosen=null;int attempts=0;
   if(area.Source.ExplicitPump){var point=area.Rings[0][0];var containing=areas.Where(a=>a.Source.Polygon&&Associated(a,area)).OrderBy(a=>(long)a.Bounds.Width*a.Bounds.Height).FirstOrDefault();
    // Keep the observed pump location. A containing station's actual holes constrain its forecourt.
    var bounded=containing is null?area:area with{Rings=containing.Rings,Bounds=containing.Bounds,Source=area.Source with{Polygon=true}};
    foreach(var facing in new[]{"E","S"}){if(!Spend(1))break;chosen=Candidate(bounded,(int)Math.Floor(point.X),(int)Math.Floor(point.Y),facing);if(chosen is not null){chosen=chosen with{InferredBoundary=containing is null};break;}}
   }else{
    for(var y=area.Bounds.Y;y<area.Bounds.Y+area.Bounds.Height&&chosen is null&&!limited&&attempts<MaximumCandidatesPerSource;y++)
     for(var x=area.Bounds.X;x<area.Bounds.X+area.Bounds.Width&&chosen is null&&!limited&&attempts<MaximumCandidatesPerSource;x++)foreach(var facing in new[]{"E","S"}){
      if(++attempts>MaximumCandidatesPerSource||!Spend(1))break;chosen=Candidate(area,x,y,facing);if(chosen is not null)break;
     }
   }
   if(chosen is null){Skip(attempts>=MaximumCandidatesPerSource?"candidate search bound; no safe placement established":"no contained forecourt with a clear straight 3-tile road connection");continue;}
   if(notes.Count+(chosen.InferredBoundary?2:1)>MaximumNotes-2){Skip("provenance note limit");limited=true;break;}
   output.Add(chosen);foreach(var r in new[]{chosen.Forecourt,chosen.Connection})foreach(var c in Cells(r))if(project.Terrain[c.Y*project.Width+c.X]!=Terrain.Road)placement.Block(c.X,c.Y);
   static string Rect(ZoneRectangle r)=>$"{r.Width}x{r.Height} at ({r.X},{r.Y})";
   Note($"Fuel {area.Source.SourceId}: {(area.Source.ExplicitPump?"observed point quantized to a tile":"one pump location inferred")}; facing {chosen.Facing}; forecourt {Rect(chosen.Forecourt)}, access {Rect(chosen.Access)}, straight connection {Rect(chosen.Connection)}. Clear ground will be paved; existing road stays unchanged.");
   if(chosen.InferredBoundary)Note($"Fuel {area.Source.SourceId}: no observed boundary; inferred search boundary {Rect(area.Bounds)}. This is a generated assumption, not source geometry.");
  }
  if(limited&&visited<areas.Count){var remaining=areas.Count-visited;skipped+=remaining;reasons["unprocessed at bounded limit"]=remaining;}
  return Finish();
 }
 internal static bool ContainsFilledPoint(IReadOnlyList<IReadOnlyList<(double X,double Y)>> rings,double x,double y){
   for(var r=0;r<rings.Count;r++){var ring=rings[r];var inside=false;
    for(int i=0,j=ring.Count-1;i<ring.Count;j=i++){
     var a=ring[j];var b=ring[i];var dx=b.X-a.X;var dy=b.Y-a.Y;var length=dx*dx+dy*dy;
     var t=length==0?0:Math.Clamp(((x-a.X)*dx+(y-a.Y)*dy)/length,0,1);
     if(Math.Abs(a.X+t*dx-x)<1e-8&&Math.Abs(a.Y+t*dy-y)<1e-8)return r==0;
     if((a.Y>y)!=(b.Y>y)&&x<(b.X-a.X)*(y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
    }
    if(r==0?!inside:inside)return false;
   }
   return true;
  }
 internal static IEnumerable<(int X,int Y)> Cells(ZoneRectangle r){for(var y=r.Y;y<r.Y+r.Height;y++)for(var x=r.X;x<r.X+r.Width;x++)yield return(x,y);}
 static bool WholeRectangle((double X,double Y)[][] rings,ZoneRectangle rectangle){
  var px=rectangle.X+rectangle.Width*.5;var py=rectangle.Y+rectangle.Height*.5;
  for(var r=0;r<rings.Length;r++){var inside=false;var ring=rings[r];for(int i=0,j=ring.Length-1;i<ring.Length;j=i++){
    var a=ring[j];var b=ring[i];if(EntersRectangle(a,b,rectangle))return false;
    if((a.Y>py)!=(b.Y>py)&&px<(b.X-a.X)*(py-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
   }if(r==0?!inside:inside)return false;}
  return true;
 }
 static bool AvoidsHoles((double X,double Y)[][] rings,ZoneRectangle rectangle){
  var px=rectangle.X+rectangle.Width*.5;var py=rectangle.Y+rectangle.Height*.5;
  for(var r=1;r<rings.Length;r++){var inside=false;var ring=rings[r];for(int i=0,j=ring.Length-1;i<ring.Length;j=i++){
    var a=ring[j];var b=ring[i];if(EntersRectangle(a,b,rectangle))return false;
    if((a.Y>py)!=(b.Y>py)&&px<(b.X-a.X)*(py-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
   }if(inside)return false;}
  return true;
 }
 static bool EntersRectangle((double X,double Y) a,(double X,double Y) b,ZoneRectangle r){
  const double e=1e-8;var dx=b.X-a.X;var dy=b.Y-a.Y;double start=0,end=1;
  bool Clip(double p,double q){if(p==0)return q>=0;var t=q/p;if(p<0){if(t>end)return false;start=Math.Max(start,t);}else{if(t<start)return false;end=Math.Min(end,t);}return true;}
  return Clip(-dx,a.X-r.X-e)&&Clip(dx,r.X+r.Width-e-a.X)&&Clip(-dy,a.Y-r.Y-e)&&Clip(dy,r.Y+r.Height-e-a.Y);
 }
}
