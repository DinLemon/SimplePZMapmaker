namespace SimplePZMapmaker.Core;

/// <summary>Street dressing for imported maps, from Build 42 street_decoration_01 sprites identified by their tile properties
/// (GroupName, CustomName, Facing): a mailbox at the curb in front of every house with a garbage bin beside it, a stop sign on each
/// approach of a lesser road to a junction, and a concrete wheel stop at the front of every 3x5 stall of a parking zone. Items are
/// decorations with stable IDs, so a re-import replaces them, and squares that are taken are skipped and counted.</summary>
public static partial class GeographicImporter
{
 const string DressingPrefix="street-";
 static readonly Dictionary<string,string> StopSigns=new(){["S"]="street_decoration_01_0",["E"]="street_decoration_01_1",["N"]="street_decoration_01_2",["W"]="street_decoration_01_3"};
 static readonly Dictionary<string,string> Mailboxes=new(){["E"]="street_decoration_01_18",["S"]="street_decoration_01_19",["W"]="street_decoration_01_20",["N"]="street_decoration_01_21"};
 const string GarbageBin="trashcontainers_01_17",WheelStopNS="street_decoration_01_28",WheelStopEW="street_decoration_01_29";
 /// <summary>Yard props of a village house (CustomName in the tile definitions): chopping block, rain collector barrel, composter.</summary>
 static readonly string[] YardProps=["crafted_02_85","carpentry_02_52","camping_01_19"];
 const string Scarecrow="location_farm_accesories_01_36";
 static int RoadRank(string? highway)=>highway switch{"motorway" or "trunk" or "motorway_link" or "trunk_link"=>5,"primary" or "primary_link"=>4,"secondary" or "secondary_link"=>3,"tertiary" or "tertiary_link"=>2,"unclassified" or "residential" or "living_street" or "service" or "road"=>1,_=>0};
 static double RoadWidthMetres(string? highway)=>highway is "motorway" or "trunk"?16:highway is "primary" or "secondary"?10:6;

 static void ApplyStreetDressing(MapProject p,List<Feature> features,List<string> notes){
  p.Decorations.RemoveAll(d=>d.Id.StartsWith(DressingPrefix,StringComparison.Ordinal));
  var taken=new HashSet<int>();foreach(var b in p.Buildings)foreach(var i in BuildingDesigner.Occupied(b)){var x=b.X+i%(b.Width+1);var y=b.Y+i/(b.Width+1);if(x>=0&&y>=0&&x<p.Width&&y<p.Height)taken.Add(y*p.Width+x);}
  foreach(var d in p.Decorations)foreach(var part in d.Parts)taken.Add((d.Y+part.Dy)*p.Width+d.X+part.Dx);foreach(var s in p.Spawns)taken.Add(s.Y*p.Width+s.X);
  foreach(var m in p.StaticModels)taken.Add(m.Y*p.Width+m.X);
  if(p.Fences.Count>0){var blocked=FenceLines.Blocker(p);foreach(var piece in FenceLines.Compose(p.Fences,p.FenceStyles,p.GateStyles,p.Width,p.Height,blocked:blocked).Pieces)taken.Add(piece.Y*p.Width+piece.X);}
  int mailboxes=0,bins=0,signs=0,stops=0,yard=0,scarecrows=0,skipped=0;
  bool Road(int x,int y)=>x>=0&&y>=0&&x<p.Width&&y<p.Height&&p.Terrain[y*p.Width+x] is Terrain.Road;
  bool Free(int x,int y)=>x>=0&&y>=0&&x<p.Width&&y<p.Height&&!taken.Contains(y*p.Width+x)&&p.Terrain[y*p.Width+x] is not(Terrain.Water or Terrain.Road);
  bool Place(string kind,int x,int y,string tile,byte flags){if(!Free(x,y)){skipped++;return false;}
   p.Decorations.Add(new(){Id=DressingPrefix+kind+"-"+x+"-"+y,X=x,Y=y,Asset=tile,Parts=[new(0,0,tile,flags)]});taken.Add(y*p.Width+x);return true;}
  (int X,int Y)[] steps=[(1,0),(-1,0),(0,1),(0,-1)];
  string Facing(int dx,int dy)=>Math.Abs(dx)>=Math.Abs(dy)?(dx>0?"E":"W"):(dy>0?"S":"N");

  // Houses: from the outside of the front door to the first square beside a road, at most 16 steps away.
  foreach(var b in p.Buildings.Where(b=>b.Design?.Floors.Any(f=>f.Rooms.ContainsValue("bedroom"))==true).OrderBy(b=>b.Y).ThenBy(b=>b.X)){
   var d=b.Design!;var f=d.Floors[-d.MinLevel];var starts=new List<(int X,int Y)>();
   foreach(var e in f.Edges.Where(e=>e.Kind=="door")){var (ox,oy)=e.Edge=="n"?(e.X,e.Y-1):(e.X-1,e.Y);var here=BuildingDesigner.Cell(b,f,e.X,e.Y)=='.';var there=BuildingDesigner.Cell(b,f,ox,oy)=='.';
    if(here!=there)starts.Add(here?(b.X+e.X,b.Y+e.Y):(b.X+ox,b.Y+oy));}
   var seen=new HashSet<(int,int)>(starts);var queue=new Queue<((int X,int Y) At,int Depth)>(starts.Select(s=>(s,0)));(int X,int Y)? curb=null;(int X,int Y) roadSide=default;
   while(queue.TryDequeue(out var item)&&curb is null){var (at,depth)=item;
    foreach(var (dx,dy) in steps)if(Road(at.X+dx,at.Y+dy)&&Free(at.X,at.Y)&&depth>0){curb=at;roadSide=(dx,dy);break;}
    if(depth<16)foreach(var (dx,dy) in steps){var next=(at.X+dx,at.Y+dy);if(seen.Add(next)&&next.Item1>=0&&next.Item2>=0&&next.Item1<p.Width&&next.Item2<p.Height&&!taken.Contains(next.Item2*p.Width+next.Item1)&&p.Terrain[next.Item2*p.Width+next.Item1] is not(Terrain.Water or Terrain.Road))queue.Enqueue((next,depth+1));}
   }
   if(curb is not {} c){skipped++;continue;}
   if(Place("mailbox",c.X,c.Y,Mailboxes[Facing(roadSide.X,roadSide.Y)],0))mailboxes++;
   foreach(var side in new[]{1,-1}){var (bx,by)=(c.X+roadSide.Y*side,c.Y+roadSide.X*side);if(Road(bx+roadSide.X,by+roadSide.Y)&&Place("bin",bx,by,GarbageBin,1)){bins++;break;}}
  }

  // Village yards: a house with no pavement within 10 squares gets one to three yard props 2-5 squares from its walls, kept two
  // squares away from its doorways so the way in stays clear; a farm zone gets a scarecrow in its middle.
  foreach(var b in p.Buildings.Where(b=>b.Design?.Floors.Any(f=>f.Rooms.ContainsValue("bedroom"))==true).OrderBy(b=>b.Y).ThenBy(b=>b.X)){
   var rural=true;for(var y=Math.Max(0,b.Y-10);y<Math.Min(p.Height,b.Y+b.Height+10)&&rural;y++)for(var x=Math.Max(0,b.X-10);x<Math.Min(p.Width,b.X+b.Width+10);x++)if(p.Terrain[y*p.Width+x]==Terrain.Pavement){rural=false;break;}
   if(!rural)continue;var d=b.Design!;var f=d.Floors[-d.MinLevel];
   var doors=f.Edges.Where(e=>e.Kind is "door" or "open").Select(e=>(X:b.X+e.X,Y:b.Y+e.Y)).ToList();
   int Ring(int x,int y){var dx=Math.Max(0,Math.Max(b.X-x,x-(b.X+b.Width-1)));var dy=Math.Max(0,Math.Max(b.Y-y,y-(b.Y+b.Height-1)));return Math.Max(dx,dy);}
   var hash=b.Id.Aggregate(2166136261u,(h,c)=>unchecked((h^c)*16777619u));var wanted=1+(int)(hash%3);var spots=new List<(int X,int Y)>();
   for(var y=b.Y-5;y<b.Y+b.Height+5;y++)for(var x=b.X-5;x<b.X+b.Width+5;x++)if(Ring(x,y) is >=2 and <=5&&Free(x,y)&&p.Terrain[y*p.Width+x] is Terrain.Grass or Terrain.Dirt&&doors.All(o=>Math.Abs(o.X-x)+Math.Abs(o.Y-y)>4)&&steps.All(s=>!Road(x+s.X,y+s.Y)))spots.Add((x,y));
   for(var i=0;i<wanted&&spots.Count>0;i++){var spot=spots[(int)((hash>>(i*5))%(uint)spots.Count)];if(Place("yard",spot.X,spot.Y,YardProps[(i+(int)(hash%3))%YardProps.Length],1))yard++;spots.RemoveAll(o=>Math.Abs(o.X-spot.X)<=1&&Math.Abs(o.Y-spot.Y)<=1);}
  }
  foreach(var z in p.GameZones.Where(z=>z.Type=="Farm"))if(Place("scarecrow",z.X+z.Width/2,z.Y+z.Height/2,Scarecrow,0))scarecrows++;

  // Junctions: where two road lines cross or one ends on another. An approach of the lesser road, or of an equal road that ends
  // there while the other passes through, gets a stop sign on the drivers' right before the crossing road.
  var mpt=p.MetresPerTile??1;var roads=features.Where(f=>!f.Polygon&&f.Rings.Count>0&&f.Rings[0].Count>=2&&RoadRank(f.Tags.GetValueOrDefault("highway"))>0).ToList();
  // Distance along a line to its closest point to (x, y), with that distance.
  (double Along,double Distance,double Length) Locate(List<(double X,double Y)> line,double x,double y){double best=double.MaxValue,along=0,walked=0;
   for(var i=1;i<line.Count;i++){var (ax,ay)=line[i-1];var (bx,by)=line[i];var dx=bx-ax;var dy=by-ay;var len=Math.Sqrt(dx*dx+dy*dy);var t=len<1e-9?0:Math.Clamp(((x-ax)*dx+(y-ay)*dy)/(len*len),0,1);var px=ax+dx*t-x;var py=ay+dy*t-y;var d=Math.Sqrt(px*px+py*py);if(d<best){best=d;along=walked+t*len;}walked+=len;}
   return(along,best,walked);}
  (double X,double Y) PointAt(List<(double X,double Y)> line,double along){for(var i=1;i<line.Count;i++){var (ax,ay)=line[i-1];var (bx,by)=line[i];var len=Math.Sqrt((bx-ax)*(bx-ax)+(by-ay)*(by-ay));if(along<=len||i==line.Count-1){var t=len<1e-9?0:Math.Clamp(along/len,0,1);return(ax+(bx-ax)*t,ay+(by-ay)*t);}along-=len;}return line[^1];}
  var crossings=new List<(double X,double Y)>();
  for(var r=0;r<roads.Count;r++)for(var q=r+1;q<roads.Count;q++){var a1=roads[r].Rings[0];var b1=roads[q].Rings[0];
   for(var i=1;i<a1.Count;i++)for(var j=1;j<b1.Count;j++){var (ax,ay)=a1[i-1];var (bx,by)=a1[i];var (cx,cy)=b1[j-1];var (dx,dy)=b1[j];var den=(bx-ax)*(dy-cy)-(by-ay)*(dx-cx);
    if(Math.Abs(den)<1e-9)continue;var t=((cx-ax)*(dy-cy)-(cy-ay)*(dx-cx))/den;var u=((cx-ax)*(by-ay)-(cy-ay)*(bx-ax))/den;var slack=1.5/Math.Max(1e-9,Math.Sqrt((bx-ax)*(bx-ax)+(by-ay)*(by-ay)));var slack2=1.5/Math.Max(1e-9,Math.Sqrt((dx-cx)*(dx-cx)+(dy-cy)*(dy-cy)));
    if(t>=-slack&&t<=1+slack&&u>=-slack2&&u<=1+slack2){var x=ax+(bx-ax)*t;var y=ay+(by-ay)*t;if(crossings.All(c=>Math.Abs(c.X-x)+Math.Abs(c.Y-y)>3))crossings.Add((x,y));}}}
  foreach(var (jx,jy) in crossings.Where(c=>c.X>=0&&c.Y>=0&&c.X<p.Width&&c.Y<p.Height).OrderBy(c=>c.Y).ThenBy(c=>c.X)){
   var here=roads.Select(f=>(Feature:f,Spot:Locate(f.Rings[0],jx,jy))).Where(e=>e.Spot.Distance<=2).ToList();if(here.Count<2)continue;
   int Rank(Feature f)=>RoadRank(f.Tags.GetValueOrDefault("highway"));bool Ends((double Along,double Distance,double Length) s)=>s.Along<=2||s.Along>=s.Length-2;
   var major=here.Max(e=>Rank(e.Feature));var through=here.Any(e=>!Ends(e.Spot));var majorHalf=here.Max(e=>RoadWidthMetres(e.Feature.Tags.GetValueOrDefault("highway")))/mpt/2;
   foreach(var (f,spot) in here){
    var rank=Rank(f);if(!(rank<major||rank==major&&Ends(spot)&&through))continue;var highway=f.Tags.GetValueOrDefault("highway");
    foreach(var sign in new[]{-1,1}){
     var probe=spot.Along+sign*(majorHalf+2);if(probe<0||probe>spot.Length)continue;var (px,py)=PointAt(f.Rings[0],probe);var ux=px-jx;var uy=py-jy;var length=Math.Sqrt(ux*ux+uy*uy);if(length<1)continue;ux/=length;uy/=length;
     // Drivers travel along -u towards the junction; their right-hand side is (u.y, -u.x).
     var side=RoadWidthMetres(highway)/mpt/2+1;var sx=(int)Math.Round(px+uy*side);var sy=(int)Math.Round(py-ux*side);
     if(Place("stop",sx,sy,StopSigns[Facing((int)Math.Round(ux*10),(int)Math.Round(uy*10))],0))signs++;
    }
   }
  }

  // Parking zones: one wheel stop per stall, at the end the cars face.
  foreach(var z in p.GameZones.Where(z=>z.Vehicle&&z.Polygon is null)){
   var dir=z.Direction??"N";var northSouth=dir is "N" or "S";if(northSouth?z.Width%3!=0||z.Height%5!=0:z.Width%5!=0||z.Height%3!=0)continue;
   if(northSouth){for(var x=z.X;x<z.X+z.Width;x+=3)for(var y=z.Y;y<z.Y+z.Height;y+=5)if(Place("stop-block",x+1,dir=="N"?y:y+4,WheelStopNS,0))stops++;}
   else for(var y=z.Y;y<z.Y+z.Height;y+=3)for(var x=z.X;x<z.X+z.Width;x+=5)if(Place("stop-block",dir=="W"?x:x+4,y+1,WheelStopEW,0))stops++;
  }
  if(mailboxes+bins+signs+stops+yard+scarecrows>0||skipped>0)notes.Add($"Street dressing: {mailboxes} mailboxes, {bins} garbage bins, {signs} stop signs, {stops} parking wheel stops, {yard} village yard props, {scarecrows} scarecrows"+(skipped>0?$"; {skipped} left out where the square was taken or no road was near":"")+".");
 }
}
