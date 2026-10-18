namespace SimplePZMapmaker.Core;

/// <summary>Game zones from source tags (docs/TRANSFER-CONTRACT.md, section 7). Land use becomes TownZone, Forest/DeepForest, Farm or
/// Vegitation polygons; parking areas and pz:zone=Vehicle become ParkingStall vehicle spawns; pz:zone names any zone type explicitly,
/// with pz:zombies (or name) for a ZombiesType definition. Zones that would overlap, cross water or buildings are kept out and counted.</summary>
public static partial class GeographicImporter
{
 /// <summary>Vanilla vehicle zone names (Build 42 VehicleZoneDistribution). Other names need a mod that registers them.</summary>
 public static readonly HashSet<string> VanillaVehicleZones=["parkingstall","trailerpark","bad","medium","good","luxuryDealership","sport","junkyard","trafficjamw","trafficjame","trafficjams","trafficjamn","rtrafficjamw","rtrafficjame","rtrafficjams","rtrafficjamn","police","prison","fire","ranger","mccoy","carpenter","postal","spiffo","ambulance","radio","fossoil","scarlet","massgenfac","transit","network3","kyheralds","lectromax","knoxdisti","advertising","airportshuttle","airportservice","farm","burnt","normalburnt","specialburnt"];

 static string? ZoneType(Dictionary<string,string> t)=>t.GetValueOrDefault("pz:zone") is {} explicitType?explicitType:
  t.GetValueOrDefault("amenity")=="parking"?"ParkingStall":
  t.GetValueOrDefault("landuse") switch{"residential"=>"TownZone","forest"=>"Forest","farmland" or "meadow" or "orchard"=>"Farm","allotments"=>"Vegitation",_=>t.GetValueOrDefault("natural")=="wood"?"Forest":null};

 static void ImportZones(MapProject p,List<Feature> features,List<string> notes){
  var candidates=new List<(GameZone Zone,long Area)>();var reasons=new Dictionary<string,int>(StringComparer.Ordinal);void Skip(string reason)=>reasons[reason]=reasons.GetValueOrDefault(reason)+1;
  var mpt=p.MetresPerTile??1;var roadDistance=RoadDistance(p,(int)Math.Ceiling(60/mpt));
  foreach(var f in features.Where(f=>f.Rings.Count>0&&!f.Tags.ContainsKey("building"))){
   var type=ZoneType(f.Tags);if(type is null)continue;if(type=="Vehicle")type="ParkingStall";
   if(!GameZone.Types.Contains(type)){Skip($"unknown zone type {type}");continue;}
   var ring=f.Rings[0].Select(v=>(X:Math.Clamp((int)Math.Round(v.X),0,p.Width),Y:Math.Clamp((int)Math.Round(v.Y),0,p.Height))).ToList();
   if(!f.Polygon){ // a line: a three-tile band around each straight segment
    foreach(var (a,b) in ring.Zip(ring.Skip(1))){var x=Math.Min(a.X,b.X)-1;var y=Math.Min(a.Y,b.Y)-1;Add(type,f.Tags,x,y,Math.Abs(b.X-a.X)+3,Math.Abs(b.Y-a.Y)+3,null);}
    continue;
   }
   var points=ring.Distinct().Select(v=>new ZonePoint(v.X,v.Y)).ToList();if(points.Count>128)points=Enumerable.Range(0,128).Select(i=>points[i*points.Count/128]).Distinct().ToList();
   if(points.Count<3){Skip("outline too small");continue;}
   var minX=points.Min(v=>v.X);var minY=points.Min(v=>v.Y);var w=points.Max(v=>v.X)-minX;var h=points.Max(v=>v.Y)-minY;if(w<1||h<1){Skip("outline too small");continue;}
   if(type=="ParkingStall"){Add(type,f.Tags,minX+1,minY+1,w-2,h-2,null);continue;}
   if(type=="Forest"&&roadDistance is not null){long deep=0,all=0;for(var y=minY;y<minY+h;y++)for(var x=minX;x<minX+w;x++)if(ZoneGeometry.Covers(minX,minY,w,h,points,x,y)){all++;if(roadDistance[y*p.Width+x]>(int)Math.Ceiling(60/mpt))deep++;}if(all>0&&deep*2>all)type="DeepForest";}
   Add(type,f.Tags,minX,minY,w,h,points);
  }
  void Add(string type,Dictionary<string,string> tags,int x,int y,int w,int h,List<ZonePoint>? polygon){
   x=Math.Max(0,x);y=Math.Max(0,y);w=Math.Min(w,p.Width-x);h=Math.Min(h,p.Height-y);
   string? facing=null;
   if(type=="ParkingStall"){ // Build 42 spawns cars only in parking zones measuring whole 5x3 stalls: 3 wide and 5 deep for cars facing N or S
    var northSouth=tags.GetValueOrDefault("pz:direction") switch{"N" or "S"=>true,"E" or "W"=>false,_=>w/3*(h/5)>=w/5*(h/3)};facing=tags.GetValueOrDefault("pz:direction") is "N" or "S" or "E" or "W"?tags["pz:direction"]:northSouth?"N":"E";
    var (sw,sh)=northSouth?(w/3*3,h/5*5):(w/5*5,h/3*3);if(sw<3||sh<3){Skip("parking area smaller than one 5x3 stall");return;}x+=(w-sw)/2;y+=(h-sh)/2;w=sw;h=sh;polygon=null;
   }
   if(w<1||h<1){Skip("outline outside the map");return;}
   var zone=new GameZone{Id=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((tags.GetValueOrDefault("__pz_source")??"")+"|zone|"+x+","+y)))[..32],Type=type,X=x,Y=y,Width=w,Height=h,Polygon=polygon,
    Name=type=="ZombiesType"?(tags.GetValueOrDefault("pz:zombies")??tags.GetValueOrDefault("name")??""):tags.GetValueOrDefault("name")?[..Math.Min(100,tags["name"].Length)]??"",
    SourceTags=tags.Where(t=>!t.Key.StartsWith("__",StringComparison.Ordinal)&&t.Key.Length<=100).Take(32).ToDictionary(t=>t.Key,t=>t.Value[..Math.Min(300,t.Value.Length)])};
   if(zone.Vehicle){var distribution=tags.GetValueOrDefault("pz:vehicles")??"parkingstall";if(!VanillaVehicleZones.Contains(distribution)){Skip($"vehicle zone '{distribution}' is not a vanilla distribution");distribution="parkingstall";}zone.VehicleDistribution=distribution;zone.Direction=facing??"N";}
   candidates.Add((zone,(long)w*h));
  }
  // Smaller, more specific zones first; a zone that would overlap one already placed in its layer is left out.
  var placed=0;foreach(var (zone,_) in candidates.OrderBy(c=>c.Area)){
   if(p.GameZones.Count>=4096){Skip("zone limit reached");continue;}
   try{GameZone.Validate(zone,p.Width,p.Height);}catch(ArgumentException){Skip("invalid zone outline");continue;}
   p.GameZones.RemoveAll(z=>z.Id==zone.Id);p.GameZones.Add(zone);try{GameZones.ValidateProject(p);placed++;}catch(ArgumentException e){p.GameZones.RemoveAt(p.GameZones.Count-1);Skip(e.Message.Contains("overlap")?"overlaps another zone or a building":e.Message.Contains("water")?"crosses water":"rejected by zone checks");}
  }
  if(placed>0||reasons.Count>0)notes.Add($"Game zones from land use, parking and pz:zone tags: {placed}"+(reasons.Count>0?"; left out: "+string.Join(", ",reasons.Select(r=>$"{r.Key} {r.Value}")):"")+".");
 }
 /// <summary>Tile distance to the nearest road, capped at <paramref name="limit"/>+1; null when the map has no road.</summary>
 static int[]? RoadDistance(MapProject p,int limit){
  var distance=new int[p.Terrain.Length];Array.Fill(distance,limit+1);var queue=new Queue<int>();
  for(var i=0;i<distance.Length;i++)if(p.Terrain[i]==Terrain.Road){distance[i]=0;queue.Enqueue(i);}
  if(queue.Count==0)return null;
  while(queue.TryDequeue(out var i)){var d=distance[i]+1;if(d>limit)continue;var x=i%p.Width;var y=i/p.Width;foreach(var (nx,ny) in new[]{(x+1,y),(x-1,y),(x,y+1),(x,y-1)})if(nx>=0&&ny>=0&&nx<p.Width&&ny<p.Height&&distance[ny*p.Width+nx]>d){distance[ny*p.Width+nx]=d;queue.Enqueue(ny*p.Width+nx);}}
  return distance;
 }
}
