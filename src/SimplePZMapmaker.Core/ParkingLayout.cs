namespace SimplePZMapmaker.Core;

/// <summary>A contiguous native stall row and its three-tile rear access strip.</summary>
public sealed record ParkingRow(ZoneRectangle Stalls,ZoneRectangle Aisle);
public sealed record ParkingLayoutResult(IReadOnlyList<ParkingRow> Rows,string Direction,int AcceptedStalls,int RejectedStalls,long Work,bool Limited,IReadOnlyList<string> Notes);

/// <summary>Bounded, deterministic parking packing in projected tile coordinates. Entire tile interiors must lie
/// inside the outer ring and outside every hole. A rear aisle is three tiles wide (one native car-stall width);
/// these local access strips do not establish road connectivity or a turning-radius/driving guarantee.</summary>
public static class ParkingLayout
{
 public const int MaximumZones=4000,MaximumMaskTiles=262144,MaximumVertices=4096;
 public const long MaximumWork=8_000_000;
 /// <summary>Only generated rows declare a rear access strip. Authored legacy rectangles keep their
 /// historical semantics; their layout cannot be inferred merely from the zone's direction.</summary>
 public static ZoneRectangle? RearAisle(GameZone zone)
 {
  if(!zone.Vehicle||!zone.SourceTags.ContainsKey("pzmap:parking-source")||zone.Polygon is not null)return null;
  return zone.Direction switch{
   "N" when zone.Height==5&&zone.Width%3==0=>new(zone.X,zone.Y+5,zone.Width,3),
   "S" when zone.Height==5&&zone.Width%3==0=>new(zone.X,zone.Y-3,zone.Width,3),
   "E" when zone.Width==5&&zone.Height%3==0=>new(zone.X-3,zone.Y,3,zone.Height),
   "W" when zone.Width==5&&zone.Height%3==0=>new(zone.X+5,zone.Y,3,zone.Height),_=>null};
 }
 public static ParkingLayoutResult Build(MapProject project,IReadOnlyList<IReadOnlyList<(double X,double Y)>> rings,string? direction=null,int maximumZones=MaximumZones,long workBudget=MaximumWork)
  =>Build(project,rings,direction,maximumZones,workBudget,null);

 internal static ParkingLayoutResult Build(MapProject project,IReadOnlyList<IReadOnlyList<(double X,double Y)>> rings,string? direction,int maximumZones,long workBudget,ParcelPlacement? placement)
 {
  var notes=new List<string>();var explicitDirection=direction is "N" or "S" or "E" or "W";
  if(direction is not null&&!explicitDirection)notes.Add($"Unknown parking direction '{direction}'; fitted N/E orientation assumed.");
  string facing=explicitDirection?direction!:"N";workBudget=Math.Clamp(workBudget,0,MaximumWork);maximumZones=Math.Clamp(maximumZones,0,MaximumZones);
  ParkingLayoutResult Empty(string reason,long work=0,bool limited=false){notes.Add(reason);return new([],facing,0,0,work,limited,notes);}
  if(workBudget==0)return Empty("Parking geometry work budget exhausted.",0,true);
  if(rings.Count==0||rings[0].Count<3)return Empty("Parking outline is empty.");
  long vertices=0;foreach(var ring in rings){vertices+=ring.Count;if(vertices>MaximumVertices)return Empty("Parking geometry work budget: too many vertices; holes were not discarded.",0,true);if(ring.Count<3||ring.Any(v=>!double.IsFinite(v.X)||!double.IsFinite(v.Y)))return Empty("Invalid parking ring; holes were not discarded.");}
  if(maximumZones==0)return Empty("Parking zone limit reached.",0,true);
  // Geographic round trips can differ from exact tile edges by a few billionths of a tile.
  static double Snap(double v)=>Math.Abs(v-Math.Round(v))<1e-7?Math.Round(v):v;
  var contours=rings.Select(r=>r.Select(v=>(X:Snap(v.X),Y:Snap(v.Y))).ToArray()).ToArray();var outer=contours[0];
  var left=(int)Math.Clamp(Math.Floor(outer.Min(v=>v.X)),0,project.Width);var top=(int)Math.Clamp(Math.Floor(outer.Min(v=>v.Y)),0,project.Height);
  var right=(int)Math.Clamp(Math.Ceiling(outer.Max(v=>v.X)),0,project.Width);var bottom=(int)Math.Clamp(Math.Ceiling(outer.Max(v=>v.Y)),0,project.Height);
  var width=right-left;var height=bottom-top;var area=(long)width*height;
  if(area<=0)return Empty("Parking outline is outside the map.");
  if(area>MaximumMaskTiles)return Empty("Parking mask work budget exceeded; no bounding rectangle substituted.",Math.Min(vertices,workBudget),true);
  var vehicleZones=project.GameZones.Where(z=>z.Vehicle&&z.X<right&&z.Y<bottom&&z.X+z.Width>left&&z.Y+z.Height>top).ToArray();
  // Charge an upper bound before allocating/rasterizing: all ring edges and zone checks per tile,
  // plus prefix sums and both orientation scans. No second polygon raster for orientation selection.
  var work=vertices+project.GameZones.Count+area*(vertices+vehicleZones.Sum(z=>(long)(z.Polygon?.Count??1))+5);
  if(work>workBudget)return Empty("Parking geometry work budget exceeded; no holes were simplified away.",Math.Min(vertices,workBudget),true);
  placement??=new ParcelPlacement(project);
  var stride=width+1;var blocked=new int[checked(stride*(height+1))];
  for(var y=0;y<height;y++)for(var x=0;x<width;x++){
   var xx=left+x;var yy=top+y;
   var free=placement.Free(xx,yy,allowRoad:true)&&WholeCell(contours,xx,yy)&&!vehicleZones.Any(z=>ZoneGeometry.Covers(z,xx,yy));
   var i=(y+1)*stride+x+1;blocked[i]=(free?0:1)+blocked[i-1]+blocked[i-stride]-blocked[i-stride-1];
  }
  bool Free(int x,int y,int w,int h){x-=left;y-=top;if(x<0||y<0||x+w>width||y+h>height)return false;
   return blocked[(y+h)*stride+x+w]-blocked[y*stride+x+w]-blocked[(y+h)*stride+x]+blocked[y*stride+x]==0;}
  (List<ParkingRow> Rows,int Accepted,int Rejected,bool Limited) Pack(string dir){
   var ns=dir is "N" or "S";var across=ns?width:height;var depth=ns?height:width;var reverse=dir is "S" or "E";
   var rows=new List<ParkingRow>();int accepted=0,rejected=0;var limited=false;
   for(var lane=0;lane+5<=depth;lane+=8){int run=-1,count=0;
    void Flush(){if(count==0)return;var sx=ns?left+run:left+lane+(reverse?3:0);var sy=ns?top+lane+(reverse?3:0):top+run;
     if(rows.Count<maximumZones){var stalls=new ZoneRectangle(sx,sy,ns?count*3:5,ns?5:count*3);
      var aisle=ns?new ZoneRectangle(sx,reverse?sy-3:sy+5,count*3,3):new ZoneRectangle(reverse?sx-3:sx+5,sy,3,count*3);
      rows.Add(new(stalls,aisle));accepted+=count;
     }else{rejected+=count;limited=true;}count=0;run=-1;}
    for(var cross=0;cross+3<=across;cross+=3){var x=ns?left+cross:left+lane;var y=ns?top+lane:top+cross;
     if(Free(x,y,ns?3:8,ns?8:3)){if(run<0)run=cross;count++;}else{Flush();rejected++;}}
    Flush();
   }
   return(rows,accepted,rejected,limited);
  }
  var chosen=Pack(facing);if(!explicitDirection){var alternative=Pack("E");if(alternative.Accepted>chosen.Accepted){chosen=alternative;facing="E";}}
  notes.Add("Assumed local rear access aisles: 3 tiles (native stall width); road connectivity and turning space require review.");
  if(chosen.Accepted==0)notes.Add("No whole native stall with a clear rear aisle fits the parking polygon.");
  if(chosen.Limited)notes.Add("Parking row zone limit reached.");
  return new(chosen.Rows,facing,chosen.Accepted,chosen.Rejected,work,chosen.Limited,notes);
 }
 static bool WholeCell((double X,double Y)[][] rings,int x,int y)
 {
  for(var r=0;r<rings.Length;r++){
   var ring=rings[r];var inside=false;var px=x+.5;var py=y+.5;
   for(int i=0,j=ring.Length-1;i<ring.Length;j=i++){
    var a=ring[j];var b=ring[i];
    // Reject any outer/hole edge entering the OPEN cell, including a tiny hole wholly inside it.
    // Boundary contact along a tile edge is allowed. A centre-only mask would miss these holes.
    if(EntersCell(a,b,x,y))return false;
    if((a.Y>py)!=(b.Y>py)&&px<(b.X-a.X)*(py-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
   }
   if(r==0?!inside:inside)return false;
  }
  return true;
 }
 static bool EntersCell((double X,double Y) a,(double X,double Y) b,int x,int y)
 {
  const double epsilon=1e-8;var dx=b.X-a.X;var dy=b.Y-a.Y;double start=0,end=1;
  bool Clip(double p,double q){if(p==0)return q>=0;var t=q/p;if(p<0){if(t>end)return false;start=Math.Max(start,t);}else{if(t<start)return false;end=Math.Min(end,t);}return true;}
  return Clip(-dx,a.X-x-epsilon)&&Clip(dx,x+1-epsilon-a.X)&&Clip(-dy,a.Y-y-epsilon)&&Clip(dy,y+1-epsilon-a.Y);
 }
}
