namespace SimplePZMapmaker.Core;

/// <summary>A vertex of the tile grid: the north-west corner of cell (X, Y).</summary>
public sealed record FencePoint(int X,int Y);
/// <summary>Tiles of one fence look. A horizontal run puts N on the cell south of it, a vertical run puts W on the cell east of it; a cell holding both edges of one style gets NW when it is set. Every AltEvery-th edge uses WAlt/NAlt (for example a brick pillar between forged sections).</summary>
public sealed class FenceStyle
{
 public string Id {get;set;}="";
 public string W {get;set;}="";
 public string N {get;set;}="";
 public string? NW {get;set;}
 public string? Post {get;set;}
 public string? WAlt {get;set;}
 public string? NAlt {get;set;}
 public int AltEvery {get;set;}
}
/// <summary>A gate or wicket Width edges wide; piece i of ClosedW/ClosedN/OpenW/OpenN goes on the i-th edge in line direction, reversed when it opens to the left.</summary>
public sealed class GateStyle
{
 public string Id {get;set;}="";
 public int Width {get;set;}=1;
 public List<string> ClosedW {get;set;}=[];
 public List<string> ClosedN {get;set;}=[];
 public List<string> OpenW {get;set;}=[];
 public List<string> OpenN {get;set;}=[];
}
public sealed class FenceGate
{
 /// <summary>Edges from the first point of the line to the gate's first edge.</summary>
 public int At {get;set;}
 public string Style {get;set;}="";
 public string OpensTo {get;set;}="right";
 public bool Open {get;set;}
}
/// <summary>A fence along cell edges (docs/TRANSFER-CONTRACT.md, section 4). It lives in its own layer, so a fence edge never takes the single decoration slot of a cell.</summary>
public sealed class FenceLine
{
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public string Style {get;set;}="";
 public int PostSpacing {get;set;}
 public List<FencePoint> Points {get;set;}=[];
 public List<FenceGate> Gates {get;set;}=[];
 public string? SourceId {get;set;}
 public Dictionary<string,string> SourceTags {get;set;}=[];
 public List<SourceOutlinePoint> SourcePath {get;set;}=[];
}
public static class FenceLines
{
 public const int MaximumLines=20000,MaximumPoints=512,MaximumEdges=250000,MaximumStyles=128;
 /// <summary>One placed fence tile. Flags are the native navigation edge bits (2 north, 4 west).</summary>
 public sealed record Piece(int X,int Y,string Tile,byte Flags,string Line);
 public sealed record Composition(List<Piece> Pieces,Dictionary<string,int> Skipped);

 public static void Validate(MapProject p)=>Validate(p.Fences,p.FenceStyles,p.GateStyles,258048);
 public static void Validate(WorldProject w)=>Validate(w.Fences,w.FenceStyles,w.GateStyles,258048);
 public static void Validate(List<FenceLine> lines,List<FenceStyle> styles,List<GateStyle> gates,int limit){
  if(lines is null||styles is null||gates is null||lines.Count>MaximumLines||styles.Count>MaximumStyles||gates.Count>MaximumStyles)throw new ArgumentException($"Fences: at most {MaximumLines} lines and {MaximumStyles} fence and gate styles.");
  var styleIds=new HashSet<string>();foreach(var s in styles){if(s is null||!SafeId(s.Id)||!styleIds.Add(s.Id)||!VisualStyles.TileId(s.W)||!VisualStyles.TileId(s.N)||s.NW is not null&&!VisualStyles.TileId(s.NW)||s.Post is not null&&!VisualStyles.TileId(s.Post)||s.WAlt is not null&&!VisualStyles.TileId(s.WAlt)||s.NAlt is not null&&!VisualStyles.TileId(s.NAlt)||s.AltEvery is <0 or >64||s.AltEvery>0&&(s.WAlt is null||s.NAlt is null))throw new ArgumentException("Invalid fence style: it needs an ID, W and N tiles, and both alternative tiles when it repeats one.");}
  var gateIds=new HashSet<string>();foreach(var g in gates){if(g is null||!SafeId(g.Id)||!gateIds.Add(g.Id)||g.Width is <1 or >8||g.ClosedW?.Count!=g.Width||g.ClosedN?.Count!=g.Width||g.OpenW is null||g.OpenN is null||g.OpenW.Count is not 0&&g.OpenW.Count!=g.Width||g.OpenN.Count!=g.OpenW.Count||g.ClosedW.Concat(g.ClosedN).Concat(g.OpenW).Concat(g.OpenN).Any(t=>!VisualStyles.TileId(t)))throw new ArgumentException("Invalid gate style: it needs an ID, a width of 1-8 and one closed (and optionally open) W and N tile per edge.");}
  var ids=new HashSet<string>();long edges=0;
  foreach(var f in lines){
   if(f is null||string.IsNullOrWhiteSpace(f.Id)||f.Id.Length>100||!ids.Add(f.Id)||!styleIds.Contains(f.Style)||f.PostSpacing is <0 or >64||f.Points is null||f.Points.Count is <2 or >MaximumPoints||f.Points.Any(v=>v is null||Math.Abs(v.X)>limit||Math.Abs(v.Y)>limit)||f.SourceId?.Length>100||f.SourceTags is null||f.SourceTags.Count>48||f.SourceTags.Any(v=>v.Key.Length is <1 or >100||v.Value is null||v.Value.Length>300)||f.SourcePath is null||f.SourcePath.Count>512||f.SourcePath.Any(v=>v is null||!double.IsFinite(v.Longitude)||!double.IsFinite(v.Latitude)||v.Longitude is < -180 or >180||v.Latitude is < -85 or >85)||f.Gates is null||f.Gates.Count>64)throw new ArgumentException("Invalid fence line: it needs a unique ID, a known style and 2-512 grid points.");
   var length=0;for(var i=1;i<f.Points.Count;i++){var a=f.Points[i-1];var b=f.Points[i];if(a.X!=b.X&&a.Y!=b.Y||a==b)throw new ArgumentException($"Fence line {f.Id}: segments must be horizontal or vertical runs along cell edges.");length+=Math.Abs(b.X-a.X)+Math.Abs(b.Y-a.Y);}
   edges+=length;if(edges>MaximumEdges)throw new ArgumentException($"Fences exceed {MaximumEdges:N0} edges.");
   var taken=new HashSet<int>();foreach(var g in f.Gates){var style=g is null?null:gates.FirstOrDefault(s=>s.Id==g.Style);if(style is null||g!.OpensTo is not("left" or "right")||g.At<0||g.At+style.Width>length||g.Open&&style.OpenW.Count==0)throw new ArgumentException($"Fence line {f.Id}: each gate needs a known style, an opening side and room on the line.");
    if(Segment(f,g.At)!=Segment(f,g.At+style.Width-1))throw new ArgumentException($"Fence line {f.Id}: a gate cannot turn a corner.");for(var e=g.At;e<g.At+style.Width;e++)if(!taken.Add(e))throw new ArgumentException($"Fence line {f.Id}: gates overlap.");}
  }
 }
 static bool SafeId(string? id)=>id is {Length:>0 and <=100}&&id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-' or '.');
 static int Segment(FenceLine f,int edge){for(var i=1;i<f.Points.Count;i++){var run=Math.Abs(f.Points[i].X-f.Points[i-1].X)+Math.Abs(f.Points[i].Y-f.Points[i-1].Y);if(edge<run)return i;edge-=run;}return f.Points.Count;}

 /// <summary>Edge tiles, gates and posts of all lines in the area [0,width) x [0,height), offset by (dx, dy). Edges on blocked cells are skipped and counted by reason.</summary>
 public static Composition Compose(IEnumerable<FenceLine> lines,IReadOnlyCollection<FenceStyle> styles,IReadOnlyCollection<GateStyle> gateStyles,int width,int height,int dx=0,int dy=0,Func<int,int,string?>? blocked=null){
  var styleById=styles.ToDictionary(s=>s.Id);var gateById=gateStyles.ToDictionary(s=>s.Id);var skipped=new Dictionary<string,int>();
  var edges=new Dictionary<(int X,int Y,bool North),(string Tile,string Style,bool Gate,string Line)>();var posts=new Dictionary<(int X,int Y),(string Tile,string Line)>();
  foreach(var f in lines){
   var style=styleById[f.Style];var gateAt=new Dictionary<int,string>();
   foreach(var g in f.Gates){var gs=gateById[g.Style];var pieces=g.Open?(W:gs.OpenW,N:gs.OpenN):(W:gs.ClosedW,N:gs.ClosedN);for(var i=0;i<gs.Width;i++){var k=g.OpensTo=="left"?gs.Width-1-i:i;gateAt[g.At+i]=k.ToString()+"|"+pieces.W[k]+"|"+pieces.N[k];}}
   var edge=0;
   void Post(int x,int y){if(style.Post is null)return;x+=dx;y+=dy;if(x<0||y<0||x>=width||y>=height)return;if(blocked?.Invoke(x,y) is {} reason){Skip(reason);return;}posts.TryAdd((x,y),(style.Post,f.Id));}
   void Skip(string reason)=>skipped[reason]=skipped.GetValueOrDefault(reason)+1;
   for(var s=1;s<f.Points.Count;s++){
    var a=f.Points[s-1];var b=f.Points[s];var horizontal=a.Y==b.Y;var step=horizontal?Math.Sign(b.X-a.X):Math.Sign(b.Y-a.Y);var run=horizontal?Math.Abs(b.X-a.X):Math.Abs(b.Y-a.Y);
    for(var i=0;i<run;i++,edge++){
     var vx=horizontal?a.X+i*step:a.X;var vy=horizontal?a.Y:a.Y+i*step;
     if(f.PostSpacing>0&&(edge%f.PostSpacing==0||i==0))Post(vx,vy);
     // The edge between vertex v and the next one belongs to the cell on its south (horizontal) or east (vertical) side.
     var x=(horizontal?Math.Min(vx,vx+step):vx)+dx;var y=(horizontal?vy:Math.Min(vy,vy+step))+dy;
     if(x<0||y<0||x>=width||y>=height)continue;
     if(blocked?.Invoke(x,y) is {} reason){Skip(reason);continue;}
     string tile;var gate=gateAt.TryGetValue(edge,out var piece);
     if(gate){var parts=piece!.Split('|');tile=horizontal?parts[2]:parts[1];}
     else{var alt=style.AltEvery>0&&edge%style.AltEvery==style.AltEvery-1;tile=horizontal?alt?style.NAlt!:style.N:alt?style.WAlt!:style.W;}
     var key=(x,y,horizontal);if(edges.TryGetValue(key,out var prior)&&prior.Gate&&!gate){Skip("overlap");continue;}
     if(edges.ContainsKey(key))Skip("overlap");edges[key]=(tile,style.Id,gate,f.Id);
    }
   }
   if(f.PostSpacing>0)Post(f.Points[^1].X,f.Points[^1].Y);
  }
  var result=new List<Piece>();
  foreach(var group in edges.GroupBy(e=>(e.Key.X,e.Key.Y))){
   var north=group.FirstOrDefault(e=>e.Key.North);var west=group.FirstOrDefault(e=>!e.Key.North);
   var bothPlain=north.Value.Tile is not null&&west.Value.Tile is not null&&!north.Value.Gate&&!west.Value.Gate&&north.Value.Style==west.Value.Style&&styleById[north.Value.Style] is {NW:{} corner}&&north.Value.Tile==styleById[north.Value.Style].N&&west.Value.Tile==styleById[west.Value.Style].W;
   if(bothPlain){result.Add(new(group.Key.X,group.Key.Y,styleById[north.Value.Style].NW!,6,north.Value.Line));continue;}
   foreach(var e in group.OrderBy(e=>e.Key.North))result.Add(new(e.Key.X,e.Key.Y,e.Value.Tile,(byte)(e.Value.Gate?0:e.Key.North?2:4),e.Value.Line));
  }
  foreach(var p in posts)result.Add(new(p.Key.X,p.Key.Y,p.Value.Tile,0,p.Value.Line));
  return new(result,skipped);
 }

 /// <summary>Blocking reasons used by export: building footprints and water never carry a fence.</summary>
 public static Func<int,int,string?> Blocker(MapProject p){var occupied=new HashSet<int>();foreach(var b in p.Buildings)foreach(var i in BuildingDesigner.Occupied(b,false)){var x=b.X+i%(b.Width+1);var y=b.Y+i/(b.Width+1);if(x>=0&&y>=0&&x<p.Width&&y<p.Height)occupied.Add(y*p.Width+x);}
  return(x,y)=>occupied.Contains(y*p.Width+x)?"building":p.Terrain[y*p.Width+x]==Terrain.Water?"water":null;}

 /// <summary>World fences crossing a section, as section-local lines; the composer clips them to the section, so a fence stays continuous across seams.</summary>
 public static List<FenceLine> ForSection(WorldProject world,MapProject p){var left=p.WorldX*256;var top=p.WorldY*256;
  return world.Fences.Where(f=>f.Points.Max(v=>v.X)>=left&&f.Points.Min(v=>v.X)<=left+p.Width&&f.Points.Max(v=>v.Y)>=top&&f.Points.Min(v=>v.Y)<=top+p.Height).Select(f=>new FenceLine{Id=f.Id,Style=f.Style,PostSpacing=f.PostSpacing,Points=f.Points.Select(v=>new FencePoint(v.X-left,v.Y-top)).ToList(),Gates=f.Gates,SourceId=f.SourceId,SourceTags=f.SourceTags,SourcePath=f.SourcePath}).ToList();}
}
