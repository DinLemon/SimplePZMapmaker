namespace SimplePZMapmaker.Core.Export;

/// <summary>Read-only native export geometry for an isolated lighting calculation. All positions use game tiles.</summary>
public static class NativeLightingGeometry
{
 public const int Floor=1,WallN=2,WallW=4,Opaque=8,WindowN=16,WindowW=32,DoorN=64,DoorW=128,OpenN=256,OpenW=512,Elevated=1024,OpenAir=2048,ThumpN=4096,ThumpW=8192;
 public const int Halo=24,MaximumSquares=500000,MaximumLights=4096;
 public sealed record Square(int X,int Y,int Z,int Flags,long BuildingId,long RoomId);
 public sealed record PointLight(int X,int Y,int Z,int Radius,double R,double G,double B,bool Active);
 public sealed record RoomLight(long BuildingId,long RoomId,int X,int Y,int Z,int Width,int Height,bool Active);
 public sealed record Scene(int WorldTileX,int WorldTileY,int ChunksX,int ChunksY,int MinZ,int MaxZ,int OutputX,int OutputY,int Size,int Level,List<Square> Squares,List<PointLight> PointLights,List<RoomLight> RoomLights,string[] Warnings);
 sealed record Cell(MapProject Project,int CellX,int CellY,TileComposer Composer);
 static readonly IReadOnlyDictionary<string,string> EmptyProperties=new Dictionary<string,string>();

 public static Scene FromProject(MapProject project,int x,int y,int size,int level,bool roomLightsPowered,Func<string,IReadOnlyDictionary<string,string>> properties,CancellationToken cancellation=default)
 {
  cancellation.ThrowIfCancellationRequested();ProjectValidator.Validate(project);return Build(project,[project],x,y,size,level,roomLightsPowered,properties,cancellation);
 }
 public static Scene FromWorld(WorldProject world,string memberId,int x,int y,int size,int level,bool roomLightsPowered,Func<string,IReadOnlyDictionary<string,string>> properties,CancellationToken cancellation=default)
 {
  cancellation.ThrowIfCancellationRequested();WorldAssembler.Validate(world);var member=world.Maps.SingleOrDefault(m=>m.Id==memberId)??throw new ArgumentException("World section not found.");
  var selected=member.Project;ValidateCrop(selected,x,y,size,level);
  var left=selected.WorldX*256+x-Halo;var top=selected.WorldY*256+y-Halo;var right=left+size+2*Halo;var bottom=top+size+2*Halo;
  // Project each intersecting section independently. Shared buildings retain both sides of a section seam.
  var relevantWorld=new WorldProject{Id=world.Id,Name=world.Name,Version=world.Version,Maps=world.Maps,LinearFeatures=world.LinearFeatures,Buildings=world.Buildings.Where(b=>b.X<right&&b.Y<bottom&&b.X+b.Width>=left&&b.Y+b.Height>=top).ToList(),GeoReference=world.GeoReference,Elevation=world.Elevation};
  var projects=world.Maps.Where(m=>m.Project.WorldX*256<right&&m.Project.WorldY*256<bottom&&m.Project.WorldX*256+m.Project.Width>left&&m.Project.WorldY*256+m.Project.Height>top).Select(m=>WorldAssembler.ProjectSection(relevantWorld,m)).ToArray();
  return Build(selected,projects,x,y,size,level,roomLightsPowered,properties,cancellation);
 }
 static void ValidateCrop(MapProject p,int x,int y,int size,int level)
 {
  if(size is <8 or >64||x<0||y<0||x>p.Width-size||y>p.Height-size||level is < ZLevels.Min or > ZLevels.Max)throw new ArgumentException("Choose an 8–64 tile lighting area inside the map and a floor from -32 to 31.");
 }
 static Scene Build(MapProject selected,MapProject[] projects,int x,int y,int size,int level,bool roomLightsPowered,Func<string,IReadOnlyDictionary<string,string>> properties,CancellationToken cancellation)
 {
  ArgumentNullException.ThrowIfNull(properties);ValidateCrop(selected,x,y,size,level);cancellation.ThrowIfCancellationRequested();
  var outputAbsX=checked(selected.WorldX*256+x);var outputAbsY=checked(selected.WorldY*256+y);
  var left=Math.Max(0,outputAbsX-Halo)/8*8;var top=Math.Max(0,outputAbsY-Halo)/8*8;
  var right=(outputAbsX+size+Halo+7)/8*8;var bottom=(outputAbsY+size+Halo+7)/8*8;
  var width=right-left;var height=bottom-top;
  if(width>128||height>128)throw new ArgumentException("The lighting area exceeds the native chunk budget.");
  var cells=new Dictionary<(int X,int Y),Cell>();
  foreach(var p in projects)for(var cy=Math.Max(p.WorldY,top/256);cy<=Math.Min(p.WorldY+p.Height/256-1,(bottom-1)/256);cy++)for(var cx=Math.Max(p.WorldX,left/256);cx<=Math.Min(p.WorldX+p.Width/256-1,(right-1)/256);cx++){
   cancellation.ThrowIfCancellationRequested();if(cells.Count>=4)throw new ArgumentException("The lighting area intersects more than four native cells.");
   cells.Add((cx,cy),new(p,cx,cy,new(p,cx-p.WorldX,cy-p.WorldY)));
  }
  var min=Math.Min(level,cells.Values.Select(c=>c.Composer.MinLevel).DefaultIfEmpty(0).Min());var max=Math.Max(level,cells.Values.Select(c=>c.Composer.MaxLevel).DefaultIfEmpty(0).Max());
  if(min<ZLevels.Min||max>ZLevels.Max)throw new ArgumentException("Exported lighting geometry exceeds the native signed floor range.");
  var warnings=new HashSet<string>(StringComparer.Ordinal){"Native light values are visualized by the editor. Browser materials and geometry shadows are previews; the game's FBO and material shaders are not reproduced.","Sky RGB, ambient strength and room power are controlled inputs. Game time, weather, erosion and live switches are not simulated."};
  var propertyCache=new Dictionary<string,IReadOnlyDictionary<string,string>>(StringComparer.Ordinal);
  IReadOnlyDictionary<string,string> Props(string tile){if(propertyCache.TryGetValue(tile,out var cached))return cached;if(propertyCache.Count>=5000)throw new ArgumentException("The lighting area contains too many distinct tile definitions.");var value=properties(tile)??EmptyProperties;propertyCache.Add(tile,value);return value;}
  var squares=new Dictionary<(int X,int Y,int Z),Square>();
  long CellId(Cell c)=>((long)c.CellY*1024+c.CellX+1)<<32;
  (long Building,long Room) RoomIds(Cell c,int id){if(id<0||c.Composer.Rooms[id].Name=="emptyoutside")return(-1,-1);var room=c.Composer.Rooms[id];return(CellId(c)+room.Building+1,CellId(c)+0x10000+id+1);}
  var missing=false;
  for(var yy=top;yy<bottom;yy++){
   cancellation.ThrowIfCancellationRequested();for(var xx=left;xx<right;xx++){
    if(!cells.TryGetValue((xx/256,yy/256),out var cell)){missing=true;continue;}
    var tx=xx%256;var ty=yy%256;
    for(var z=min;z<=max;z++){
     var tiles=cell.Composer.At(tx,ty,z);if(tiles.Count==0)continue;
     var flags=0;foreach(var tile in tiles){var p=Props(tile);flags|=TileFlags(p);
      if(p.ContainsKey("LightRadius")||p.ContainsKey("lightR"))warnings.Add("Tile light fixtures require live game switch and power state; only authored model point lights and the explicit room power assumption are included.");
      if(p.ContainsKey("LightFilterR")||p.ContainsKey("LightFilterMix")||p.ContainsKey("curtain")||p.ContainsKey("WallSE")||p.ContainsKey("trans")||p.ContainsKey("noStart"))warnings.Add("Some tiles use diagonal, curtain, transmission or vertical vision rules beyond this lighting adapter; inspect those tiles separately.");
     }
     var ids=RoomIds(cell,cell.Composer.RoomAt(tx,ty,z));var key=(xx-left,yy-top,z);
     if(squares.Count>=MaximumSquares)throw new ArgumentException("The lighting area exceeds 500,000 exported squares. Choose a smaller area or fewer floors.");
     squares.Add(key,new(key.Item1,key.Item2,z,flags,ids.Building,ids.Room));
    }
   }
  }
  if(missing||outputAbsX<Halo||outputAbsY<Halo)warnings.Add("The lighting halo reaches an unmapped area. Light and enclosure beyond the available sections are unknown.");
  var pointLights=new List<PointLight>();var seenModels=new HashSet<(string,int,int,int)>();
  foreach(var p in projects)foreach(var placed in StaticModelExporter.Gather(p)){
   cancellation.ThrowIfCancellationRequested();var m=placed.Placement;if(!seenModels.Add((m.Id,placed.X,placed.Y,placed.Z)))continue;
   if(placed.X<left||placed.X>=right||placed.Y<top||placed.Y>=bottom||placed.Z<min||placed.Z>max)continue;
   var key=(placed.X-left,placed.Y-top,placed.Z);if(!squares.TryGetValue(key,out var sq)){warnings.Add("A model occupies an otherwise empty square. The adapter does not invent supporting floors for it.");continue;}
   if(m.Door is {} door){var flags=sq.Flags|(door.Direction=="n"?DoorN|ThumpN:DoorW|ThumpW);if(door.Open)flags|=door.Direction=="n"?OpenN:OpenW;squares[key]=sq with{Flags=flags};}
   if(m.Light is {} light){if(pointLights.Count>=MaximumLights)throw new ArgumentException("The lighting area contains more than 4,096 point lights.");pointLights.Add(new(placed.X,placed.Y,placed.Z,light.Radius,light.Rgb[0],light.Rgb[1],light.Rgb[2],light.Active));}
   if(StaticModelPlacement.BlocksTiles(m))warnings.Add("Model footprints provide physical collision. Their mesh does not automatically become an opaque wall in this lighting scene.");
  }
  // A sky-exposed column cannot contain an interior room or an opaque floor above the sample.
  // Room IDs come directly from the exporter; balcony/emptyoutside definitions remain exterior.
  foreach(var column in squares.Values.GroupBy(s=>(s.X,s.Y))){var covered=false;foreach(var sq in column.OrderByDescending(s=>s.Z)){
   var key=(sq.X,sq.Y,sq.Z);if(sq.RoomId<0&&sq.Z>=0&&!covered)squares[key]=sq with{Flags=sq.Flags|OpenAir};
   if(sq.RoomId>=0||(sq.Flags&Floor)!=0)covered=true;
  }}
  var roomLights=new List<RoomLight>();foreach(var cell in cells.Values)for(var id=0;id<cell.Composer.Rooms.Count;id++){
   cancellation.ThrowIfCancellationRequested();var room=cell.Composer.Rooms[id];if(room.Name=="emptyoutside"||room.Level<min||room.Level>max)continue;var ids=RoomIds(cell,id);
   foreach(var rectangle in room.Rectangles??[new(room.X,room.Y,room.Width,room.Height)]){
    var rx=cell.CellX*256+rectangle.X;var ry=cell.CellY*256+rectangle.Y;
    if(rx>=right||ry>=bottom||rx+rectangle.Width<=left||ry+rectangle.Height<=top)continue;
    if(roomLights.Count>=MaximumLights)throw new ArgumentException("The lighting area contains more than 4,096 room light rectangles.");roomLights.Add(new(ids.Building,ids.Room,rx,ry,room.Level,rectangle.Width,rectangle.Height,roomLightsPowered));
   }
  }
  return new(left,top,width/8,height/8,min,max,outputAbsX-left,outputAbsY-top,size,level,squares.Values.OrderBy(s=>s.Z).ThenBy(s=>s.Y).ThenBy(s=>s.X).ToList(),pointLights,roomLights,warnings.Order(StringComparer.Ordinal).ToArray());
 }
 /// <summary>Tile definition properties, including Build 42's compound wall flags, normalized for the native protocol.</summary>
 public static int TileFlags(IReadOnlyDictionary<string,string> p)
 {
  bool Has(string key)=>p.ContainsKey(key);var f=0;
  if(Has("solidfloor")&&!Has("transparentFloor"))f|=Floor;
  if(Has("solid")||Has("blocksight"))f|=Opaque;
  if((Has("WallN")||Has("WallNW")||Has("collideN"))&&!Has("WallNTrans")&&!Has("transparentN")&&!Has("WindowN")&&!Has("DoorWallN")&&!Has("doorN")&&!Has("windowN"))f|=WallN;
  if((Has("WallW")||Has("WallNW")||Has("collideW"))&&!Has("WallWTrans")&&!Has("transparentW")&&!Has("WindowW")&&!Has("DoorWallW")&&!Has("doorW")&&!Has("windowW"))f|=WallW;
  if(Has("windowN"))f|=WindowN;if(Has("windowW"))f|=WindowW;if(Has("doorN"))f|=DoorN;if(Has("doorW"))f|=DoorW;
  if(Has("stairsTN")||Has("stairsTW")||Has("stairsMN")||Has("stairsMW"))f|=Elevated;
  return f;
 }
}
