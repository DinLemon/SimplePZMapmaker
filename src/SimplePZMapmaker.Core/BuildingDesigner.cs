namespace SimplePZMapmaker.Core;

public static class BuildingDesigner
{
 public static readonly string[] Kinds=["house","apartments","school","shop","warehouse","industrial","clinic","hotel","garage"];
 public static readonly Dictionary<string,string> Names=new(){["house"]="Detached house",["apartments"]="Apartment building",["school"]="School",["shop"]="Shop",["warehouse"]="Warehouse",["industrial"]="Industrial building",["clinic"]="Clinic",["hotel"]="Hotel",["garage"]="Garage"};
 public static readonly string[] RoomNames=["hall","livingroom","kitchen","bedroom","bathroom","classroom","office","conveniencestore","warehouse","medical","hotelroom","garage","storage","balcony"];
 /// <summary>Every room name a floor may use: the editor's own names plus the Build 42 internal room names (loot and zombie room filters).</summary>
 public static readonly HashSet<string> AllRoomNames=[..RoomNames,..LoadRoomCatalog()];
 static IEnumerable<string> LoadRoomCatalog(){using var s=typeof(BuildingDesigner).Assembly.GetManifestResourceStream("SimplePZMapmaker.Core.RoomCatalog.json")!;using var d=System.Text.Json.JsonDocument.Parse(s);return d.RootElement.GetProperty("rooms").EnumerateArray().Select(e=>e.GetString()!).ToArray();}
 public static char Cell(Building b,BuildingFloor floor,int x,int y)=>x<0||y<0||x>=b.Width||y>=b.Height?'.':floor.Cells[y*b.Width+x];
 public static bool Occupies(Building b,int x,int y)=>b.Design is null?x>=0&&y>=0&&x<b.Width&&y<b.Height:b.Design.Floors.Any(f=>Cell(b,f,x,y)!='.');
 public static bool OccupiesAbove(Building b,int x,int y)=>b.Design is null?Occupies(b,x,y):b.Design.Floors.Where((_,i)=>i+b.Design.MinLevel>=0).Any(f=>Cell(b,f,x,y)!='.');
 public static HashSet<int> Occupied(Building b,bool perimeter=true){
  var result=new HashSet<int>();for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)if(Occupies(b,x,y)){result.Add(y*(b.Width+1)+x);if(perimeter){result.Add(y*(b.Width+1)+x+1);result.Add((y+1)*(b.Width+1)+x);}}
  if(b.Design is null&&perimeter)for(var y=0;y<=b.Height;y++)for(var x=0;x<=b.Width;x++)result.Add(y*(b.Width+1)+x);return result;
 }
 /// <summary>Smallest footprint side. Below <see cref="RoomyFootprint"/> a building is one room (a shed, kiosk or booth).</summary>
 public const int MinimumFootprint=2,RoomyFootprint=6,MaximumFootprint=512,MaximumFloorCells=262144;
 public static bool Small(int width,int height)=>width<RoomyFootprint||height<RoomyFootprint;
 /// <summary>Stairs need five tiles in a straight line plus a landing, so a small footprint holds them only along a side of at least six tiles.</summary>
 public static bool HoldsStairs(int width,int height)=>Math.Max(width,height)>=RoomyFootprint;
 public static BuildingDesign Generate(Building b,int levels,int seed,string shape="rect",int basements=0){
  if(b.Width is <MinimumFootprint or >MaximumFootprint||b.Height is <MinimumFootprint or >MaximumFootprint||levels is <1 or >ZLevels.AboveGroundCount||basements is <0 or >ZLevels.BasementCount||!Kinds.Contains(b.Kind)||shape is not("rect" or "l" or "t" or "courtyard" or "annex"))throw new ArgumentException("Check the building type, dimensions, shape, and floor count: 2-512 tiles per side, 1-32 above-ground floors (z=0 to 31) and 0-32 basement levels (z=-1 to -32).");
  if((long)b.Width*b.Height*(levels+basements)>MaximumFloorCells)throw new ArgumentException("Building floor-cell budget exceeds 262144 tiles.");
  if(Small(b.Width,b.Height))return GenerateSmall(b,levels,seed,shape,basements);
  levels+=basements;
  var rng=new Random(seed);var design=new BuildingDesign{MinLevel=-basements,Variant=seed,Shape=shape,Material=new[]{"wood","brick","plaster"}[rng.Next(3)],Roof=shape=="rect"?new[]{"flat","gabled","shed","hipped"}[rng.Next(4)]:"flat"};
  var names=b.Kind switch{"house"=>new[]{"livingroom","kitchen","bedroom","bathroom"},"apartments"=>new[]{"bedroom","kitchen","livingroom","bathroom"},"school"=>new[]{"classroom","office","classroom","bathroom"},"shop"=>new[]{"conveniencestore","storage","office","bathroom"},"clinic"=>new[]{"medical","office","medical","bathroom"},"hotel"=>new[]{"hotelroom","hotelroom","office","bathroom"},"garage"=>new[]{"garage","storage","garage","storage"},_=>new[]{"warehouse","storage","office","storage"}};
  var stairX=shape=="t"||b.Kind is "apartments" or "clinic" or "hotel"?b.Width/2:1;const int stairY=1;
  for(var z=0;z<levels;z++){
   var floor=new BuildingFloor{Rooms=new(){["0"]="hall"}};var cells=new char[b.Width*b.Height];var roomKeys=new Dictionary<string,char>();
   char RoomCode(string key,string name){if(roomKeys.TryGetValue(key,out var existing))return existing;var code="123456789abcdefghijklmnopqrstuvwxyz"[roomKeys.Count];roomKeys[key]=code;floor.Rooms[code.ToString()]=name;return code;}
   var midX=Math.Clamp(b.Width/2+rng.Next(-1,2),3,b.Width-2);var midY=Math.Clamp(b.Height/2+rng.Next(-1,2),3,b.Height-2);
   for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++){
    var inside=shape switch{"l"=>x<b.Width/2||y<b.Height/2,"t"=>y<b.Height/3||Math.Abs(x-b.Width/2)<=b.Width/6,"courtyard"=>x<3||y<3||x>=b.Width-3||y>=b.Height-3,"annex"=>x<b.Width*2/3||y<b.Height/2,_=>true};
    if(!inside){cells[y*b.Width+x]='.';continue;}
    char code;
    if(b.Kind is "apartments" or "clinic" or "hotel"){
     var corridor=x==midX||x==midX-1;
     if(corridor)code='0';else{var wing=x<midX?0:1;var bay=Math.Min(5,y/Math.Max(4,(b.Height+5)/6));var section=b.Kind=="apartments"?(x<(wing==0?midX/2:midX+(b.Width-midX)/2)?0:1):0;var name=b.Kind=="clinic"?(bay==0?"office":"medical"):b.Kind=="hotel"?(bay==0&&wing==0&&z==basements?"office":"hotelroom"):section==0?"bedroom":bay%3==0?"kitchen":bay%3==1?"livingroom":"bathroom";code=RoomCode($"{wing}-{bay}-{section}",name);}
    }else if(b.Kind=="school"){
     if(y==midY||y==midY-1)code='0';else{var side=y<midY?0:1;var bay=Math.Min(7,x/Math.Max(4,(b.Width+7)/8));code=RoomCode($"{side}-{bay}",bay==0?side==0?"office":"bathroom":"classroom");}
    }else if(b.Kind is "warehouse" or "industrial" or "garage"){
     code=y<b.Height*3/4?RoomCode("main",b.Kind=="garage"?"garage":"warehouse"):RoomCode(x<midX?"store":"office",x<midX?"storage":"office");
    }else if(b.Kind=="shop"){
     code=y<b.Height*2/3?RoomCode("retail","conveniencestore"):RoomCode(x<midX?"store":"office",x<midX?"storage":"office");
    }else{var quadrant=(x>=midX?1:0)+(y>=midY?2:0);code=RoomCode(quadrant.ToString(),z-basements>0&&quadrant<3?"bedroom":names[(quadrant+Math.Abs(seed%2))%4]);}
    cells[y*b.Width+x]=code;
   }
   if(levels>1)for(var y=stairY;y<=stairY+4;y++)cells[y*b.Width+stairX]='0';
   floor.Cells=new(cells);
   if(z<basements)foreach(var key in floor.Rooms.Keys.Where(k=>k!="0").ToArray())floor.Rooms[key]=b.Kind is "warehouse" or "industrial"?"warehouse":"storage";
   if(z<levels-1)floor.Objects.Add(new(){X=stairX,Y=stairY,Kind="stairs"});
   design.Floors.Add(floor);
  }
  b.Design=design;
  SettlementAccess.ConnectLandings(b,seed,furnish:false,validate:false);
  for(var z=0;z<levels;z++){
   var f=design.Floors[z];f.Edges=AutoEdges(b,f,z+design.MinLevel);
   Furnish(b,z,rng);
  }
  RoofGeometry.SelectSafeAutomaticRoof(b);
  Validate(b);
  return design;
 }
 /// <summary>A footprint under 6 tiles on a side: one room, plus a hall strip for stairs when the long side holds them.</summary>
 static BuildingDesign GenerateSmall(Building b,int levels,int seed,string shape,int basements){
  if(shape!="rect")throw new ArgumentException("Footprints under 6 tiles on a side use the rectangular shape.");
  if(levels+basements>1&&!HoldsStairs(b.Width,b.Height))throw new ArgumentException("A footprint under 6 tiles on both sides has room for one floor only: stairs need a straight run of five tiles plus a landing.");
  var total=levels+basements;var rng=new Random(seed);var design=new BuildingDesign{MinLevel=-basements,Variant=seed,Shape="rect",Material=new[]{"wood","brick","plaster"}[rng.Next(3)],Roof=new[]{"flat","gabled","shed","hipped"}[rng.Next(4)]};
  var room=b.Kind switch{"shop"=>"conveniencestore","garage"=>"garage","school" or "clinic" or "hotel" or "apartments"=>"office",_=>"storage"};
  var direction=b.Height>=RoomyFootprint?"n":"w";var stairX=direction=="n"?0:1;var stairY=direction=="n"?1:0;
  for(var z=0;z<total;z++){
   var cells=Enumerable.Repeat('1',b.Width*b.Height).ToArray();var floor=new BuildingFloor{Rooms=new(){["0"]="hall",["1"]=z<basements?"storage":room}};
   if(total>1)for(var i=0;i<5;i++)cells[(stairY+(direction=="n"?i:0))*b.Width+stairX+(direction=="w"?i:0)]='0';else floor.Rooms.Remove("0");
   floor.Cells=new(cells);if(z<total-1)floor.Objects.Add(new(){X=stairX,Y=stairY,Kind="stairs",Direction=direction});design.Floors.Add(floor);
  }
  b.Design=design;
  SettlementAccess.ConnectLandings(b,seed,furnish:false,validate:false);
  for(var z=0;z<total;z++){design.Floors[z].Edges=AutoEdges(b,design.Floors[z],z+design.MinLevel);Furnish(b,z,rng);}
  RoofGeometry.SelectSafeAutomaticRoof(b);Validate(b);return design;
 }
 /// <summary>Furnishes one floor. Bedrooms, kitchens, bathrooms and living rooms take TileZed furniture against their walls (map format 14);
 /// <paramref name="catalog"/> false keeps the single legacy object per room, as the distributed housing templates do.</summary>
 public static void Furnish(Building b,int z,Random rng,bool catalog=true,bool preserveAssets=false){
  var f=b.Design!.Floors[z];f.Objects.RemoveAll(o=>o.Kind!="stairs"&&(!preserveAssets||o.Kind!="asset"));
  foreach(var code in f.Rooms.Keys.Where(k=>k!="0"&&f.Rooms[k]!="balcony")){
   var passages=f.Edges.Where(e=>e.Kind is "door" or "open").SelectMany(e=>e.Edge=="n"?new[]{e.Y*b.Width+e.X,(e.Y-1)*b.Width+e.X}:new[]{e.Y*b.Width+e.X,e.Y*b.Width+e.X-1}).ToHashSet();
   if(catalog&&FurnitureCatalog.Furnishes(f.Rooms[code])){FurnitureCatalog.Furnish(b,f,z,code[0],f.Rooms[code],passages,rng);continue;}
   if(catalog){RoomFurnishing.Furnish(b,f,z,code[0],f.Rooms[code],passages);continue;}
   var kind=f.Rooms[code] switch{"kitchen"=>"counter","bedroom" or "hotelroom"=>"bed","livingroom"=>"seat","bathroom"=>"sink",_=>"storage"};var taken=f.Objects.Where(o=>o.Kind!="stairs").SelectMany(BuildingObjects.Cells).ToHashSet();
   var candidates=Enumerable.Range(0,f.Cells.Length).Where(i=>f.Cells[i]==code[0]).SelectMany(i=>new[]{"n","w"}.Select(direction=>new BuildingObject{X=i%b.Width,Y=i/b.Width,Kind=kind,Direction=direction,Style=b.Design.Style})).Where(o=>BuildingObjects.Cells(o).All(point=>Cell(b,f,point.X,point.Y)==code[0]&&!StairSpace(b,z,point.X,point.Y)&&!passages.Contains(point.Y*b.Width+point.X)&&!taken.Contains(point))).ToArray();
   if(candidates.Length>6)f.Objects.Add(candidates[rng.Next(candidates.Length)]);
  }
 }
 /// <summary>Refreshes only one floor's generated furniture. Authored tile groups, models, stairs and geometry remain.</summary>
 public static void Refurnish(Building b,int level){
  Validate(b);if(b.Design is null)throw new ArgumentException("Generate a building plan before furnishing it.");var z=level-b.Design.MinLevel;if(z<0||z>=b.Design.Floors.Count)throw new ArgumentException("Choose an existing floor to furnish.");
  Furnish(b,z,new Random(unchecked(b.Design.Variant+z*1009)),preserveAssets:true);b.Modified=true;Validate(b);
 }
 public static bool StairSpace(Building b,int z,int x,int y)=>b.Design!.Floors.Where((_,level)=>level==z||level==z-1).SelectMany(f=>f.Objects).Any(o=>o.Kind=="stairs"&&(o.Direction=="n"?x==o.X&&y>=o.Y&&y<=o.Y+4:y==o.Y&&x>=o.X&&x<=o.X+4));
 public static List<BuildingEdge> AutoEdges(Building b,BuildingFloor f,int z){
  var edges=new List<BuildingEdge>();var doors=new HashSet<string>();var entrance=false;
  var regions=new int[f.Cells.Length];Array.Fill(regions,-1);var region=0;
  for(var i=0;i<regions.Length;i++)if(f.Cells[i]!='.'&&regions[i]<0){var q=new Queue<int>();q.Enqueue(i);regions[i]=region;while(q.TryDequeue(out var index)){var x=index%b.Width;var y=index/b.Width;foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}){var nx=x+dx;var ny=y+dy;if(nx<0||ny<0||nx>=b.Width||ny>=b.Height)continue;var next=ny*b.Width+nx;if(regions[next]<0&&f.Cells[next]==f.Cells[i]){regions[next]=region;q.Enqueue(next);}}}region++;}
  for(var y=0;y<=b.Height;y++)for(var x=0;x<=b.Width;x++)foreach(var direction in new[]{"n","w"}){
   var a=Cell(b,f,x,y);var other=direction=="n"?Cell(b,f,x,y-1):Cell(b,f,x-1,y);if(a==other||a=='.'&&other=='.')continue;
   var outer=a=='.'||other=='.';var kind="wall";
   if(!outer){var first=regions[y*b.Width+x];var second=direction=="n"?regions[(y-1)*b.Width+x]:regions[y*b.Width+x-1];var pair=$"{Math.Min(first,second)},{Math.Max(first,second)}";if(doors.Add(pair))kind="door";}
   else if(z==0&&!entrance&&a!='.'&&direction=="n"){kind="door";entrance=true;}
   else if(z>=0&&outer&&(direction=="n"?x:y)%5==3)kind="window";
   edges.Add(new(){X=x,Y=y,Edge=direction,Kind=kind});
  }return edges;
 }
 public static void Validate(Building b,bool requireAccess=true){
  if(b is null||b.Width is <MinimumFootprint or >MaximumFootprint||b.Height is <MinimumFootprint or >MaximumFootprint||!Kinds.Contains(b.Kind))throw new ArgumentException("Check the building dimensions (2-512 tiles per side) and type.");
  if(b.Attribution is null||b.Attribution.Count>30||b.Attribution.Any(s=>s is null||s.Length>1000))throw new ArgumentException("Invalid building template source.");
  ResourceDependencies.Validate(b.RequiredMods,b.AssetSources);
  if(b.Design is null)return;var d=b.Design;
  [System.Diagnostics.CodeAnalysis.DoesNotReturn] void Fail(string text)=>throw new ArgumentException($"Building '{b.Name}': {text}");
  if(d.Material is not("wood" or "brick" or "plaster")||d.Roof is not("none" or "flat" or "gabled" or "shed" or "hipped")||d.Shape is not("rect" or "l" or "t" or "courtyard" or "annex" or "custom")||d.Floors is null||d.MinLevel is <ZLevels.Min or >0||d.Floors.Count is <1 or >ZLevels.Total||d.Floors.Count+d.MinLevel is <1 or >ZLevels.AboveGroundCount)Fail("invalid material, roof, or floor count: 1-32 above-ground floors and 0-32 basement levels, within z=-32 to 31.");
  if(d.Style is null||d.Style.Length>0&&!VisualStyles.All.ContainsKey(d.Style)||d.RoofTileset is not null&&!System.Text.RegularExpressions.Regex.IsMatch(d.RoofTileset,@"^roofs_30_[0-9]{2}$"))Fail("invalid appearance style or roof tileset.");
  if(d.FlatRoofTile is not null&&!VisualStyles.TileId(d.FlatRoofTile))Fail("invalid flat roof surface resource.");
  if((long)b.Width*b.Height*d.Floors.Count>MaximumFloorCells)Fail("building floor-cell budget exceeds 262144 tiles.");
  if(d.Floors.Any(f=>f is null||f.Cells is null||f.Cells.Length!=b.Width*b.Height||f.Objects is null||f.Objects.Count>512||f.Edges is null||f.Edges.Count>2*(b.Width+1)*(b.Height+1)||f.Edges.Any(e=>e is null)||f.Rooms is null))Fail("invalid floor layer.");
  for(var z=0;z<d.Floors.Count;z++){
   var f=d.Floors[z];if(f is null||f.Cells is null||f.Cells.Length!=b.Width*b.Height||f.Rooms is null||f.Rooms.Count is <1 or >35||f.Rooms.Any(r=>r.Key.Length!=1||!"0123456789abcdefghijklmnopqrstuvwxyz".Contains(r.Key[0])||!AllRoomNames.Contains(r.Value))||f.Cells.Any(c=>c!='.'&&!f.Rooms.ContainsKey(c.ToString()))||!f.Cells.Any(c=>c!='.')||f.Edges is null||f.Edges.Count>2*(b.Width+1)*(b.Height+1)||f.Objects is null||f.Objects.Count>512)Fail($"invalid level {z+d.MinLevel}.");
   if(f.FloorTile is not null&&!VisualStyles.TileId(f.FloorTile)||f.FloorTiles is null||f.FloorTiles.Count>f.Cells.Length||f.FloorTiles.Any(v=>v.Key<0||v.Key>=f.Cells.Length||f.Cells[v.Key]=='.'||!VisualStyles.TileId(v.Value)))Fail("floor resources are invalid or outside a room.");
   if(f.StaticModels is null||f.StaticModels.Count>512)Fail("invalid model layer.");var modelIds=new HashSet<string>();foreach(var model in f.StaticModels){StaticModelPlacement.Validate(model,b.Width,b.Height,z+d.MinLevel,z+d.MinLevel);if(!modelIds.Add(model.Id)||StaticModelPlacement.FootprintCells(model).Any(c=>Cell(b,f,model.X+c.Dx,model.Y+c.Dy)=='.'))Fail("the complete model footprint is outside a room.");}
   foreach(var e in f.Edges)if(e.OverlayLevelOffset is <0 or >1||e.OverlayLevelOffset!=0&&(e.OverlayTile is null||z+d.MinLevel+e.OverlayLevelOffset>ZLevels.Max))Fail("facade overlay offset must be 0 or 1 within z=-32..31 and requires a decoration.");
   foreach(var e in f.Edges)if(e.WallTile is not null&&!VisualStyles.TileId(e.WallTile)||e.OpeningTile is not null&&!VisualStyles.TileId(e.OpeningTile)||e.OverlayTile is not null&&(!VisualStyles.TileId(e.OverlayTile)||e.Kind=="open"))Fail("invalid wall or opening resource.");
   var keys=new HashSet<string>();foreach(var e in f.Edges){if(e is null||e.X<0||e.Y<0||e.X>b.Width||e.Y>b.Height||e.Edge is not("n" or "w")||e.Kind is not("wall" or "door" or "window" or "open")||!keys.Add($"{e.X},{e.Y},{e.Edge}")||Cell(b,f,e.X,e.Y)=='.'&&(e.Edge=="n"?Cell(b,f,e.X,e.Y-1):Cell(b,f,e.X-1,e.Y))=='.')Fail($"check the walls on level {z+d.MinLevel}.");}
   var objectPositions=new HashSet<int>();foreach(var o in f.Objects){if(o is null||o.Kind is not("stairs" or "bed" or "storage" or "counter" or "seat" or "sink" or "asset" or "furniture")||(o.Kind=="furniture"?!FurnitureCatalog.Known(o.Asset,o.Direction):o.Direction is not("n" or "w"))||o.Style is null||o.Style.Length>0&&!VisualStyles.All.ContainsKey(o.Style)||Cell(b,f,o.X,o.Y)=='.'||!objectPositions.Add(o.Y*b.Width+o.X))Fail($"furniture on level {z+d.MinLevel} is outside a room or overlaps.");
    if(o.Kind=="asset")VisualStyles.ValidateParts(o.Asset,o.Parts);else if(o.Kind!="furniture"&&o.Asset is not null||o.Parts is null||o.Parts.Count>0)Fail("the object resource does not match its type.");
    if(o.Kind=="stairs"){
     if(z==d.Floors.Count-1)Fail("stairs must lead to the next floor.");
     for(var i=0;i<5;i++){var x=o.X+(o.Direction=="w"?i:0);var y=o.Y+(o.Direction=="n"?i:0);if(Cell(b,f,x,y)=='.'||Cell(b,d.Floors[z+1],x,y)=='.')Fail("stairs need three steps and landings on both floors.");}
    }
   }
   var furnitureCells=new HashSet<int>();foreach(var o in f.Objects.Where(o=>o.Kind!="stairs")){
    foreach(var point in BuildingObjects.Cells(o))if(Cell(b,f,point.X,point.Y)=='.'||StairSpace(b,z,point.X,point.Y)||!furnitureCells.Add(point.Y*b.Width+point.X))Fail("furniture overlaps, blocks the stairs, or extends beyond the outline.");
    var parts=BuildingObjects.Cells(o).ToHashSet();foreach(var point in parts)foreach(var (dx,dy,edge) in new[]{(1,0,"w"),(0,1,"n")})if(parts.Contains((point.X+dx,point.Y+dy))&&f.Edges.Any(e=>e.X==point.X+dx&&e.Y==point.Y+dy&&e.Edge==edge&&e.Kind!="open"))Fail("furniture crosses a wall or opening.");
    if(f.Edges.Any(e=>e.Kind is "door" or "open"&&(parts.Contains((e.X,e.Y))||parts.Contains(e.Edge=="n"?(e.X,e.Y-1):(e.X-1,e.Y)))))Fail("furniture blocks a door or passage.");
   }
   var blockingModels=new HashSet<int>();foreach(var model in f.StaticModels.Where(StaticModelPlacement.BlocksTiles)){var cells=StaticModelPlacement.FootprintCells(model).Select(c=>(X:model.X+c.Dx,Y:model.Y+c.Dy)).ToHashSet();foreach(var c in cells){if(!blockingModels.Add(c.Y*b.Width+c.X)||StairSpace(b,z,c.X,c.Y)||furnitureCells.Contains(c.Y*b.Width+c.X)||f.Edges.Any(e=>e.Kind is "door" or "open"&&(e.X==c.X&&e.Y==c.Y||(e.Edge=="n"?e.X==c.X&&e.Y-1==c.Y:e.X-1==c.X&&e.Y==c.Y))))Fail("a blocking footprint overlaps another model, furniture, stairs, or a passage.");foreach(var (dx,dy,edge) in new[]{(1,0,"w"),(0,1,"n")})if(cells.Contains((c.X+dx,c.Y+dy))&&f.Edges.Any(e=>e.X==c.X+dx&&e.Y==c.Y+dy&&e.Edge==edge&&e.Kind!="open"))Fail("a model footprint crosses a wall or door.");}}
   var modelDoors=new HashSet<string>();foreach(var m in f.StaticModels.Where(m=>m.Door is not null)){var dir=m.Door!.Direction;if(!modelDoors.Add($"{m.X},{m.Y},{dir}")||StairSpace(b,z,m.X,m.Y)||furnitureCells.Contains(m.Y*b.Width+m.X)||blockingModels.Contains(m.Y*b.Width+m.X)||f.Edges.Any(e=>e.X==m.X&&e.Y==m.Y&&e.Edge==dir&&e.Kind!="open"))Fail("a model door overlaps a wall, door, stair, furniture, or blocking model. Use a clear opening.");}
   if(requireAccess&&z<d.Floors.Count-1&&!f.Objects.Any(o=>o.Kind=="stairs"))Fail($"floor {z+1} has no stairs.");
   foreach(var o in f.Objects.Where(o=>o.Kind!="stairs"))if(StairSpace(b,z,o.X,o.Y))Fail("furniture blocks the stairs.");
   var stairCells=new HashSet<int>();foreach(var stair in f.Objects.Where(o=>o.Kind=="stairs"))for(var i=0;i<5;i++){
    var x=stair.X+(stair.Direction=="w"?i:0);var y=stair.Y+(stair.Direction=="n"?i:0);if(!stairCells.Add(y*b.Width+x))Fail("stairs overlap.");
    // The upper floor's far end borders the shaft, not the lower landing. It may have a guard;
    // every lower-floor edge and every edge across the three upper shaft cells must stay open.
    if(i>0)foreach(var level in new[]{z,z+1})if(!(level==z+1&&i==4)&&d.Floors[level].Edges.Any(e=>e.X==x&&e.Y==y&&e.Edge==stair.Direction&&e.Kind!="open"))Fail("stairs cross a wall or opening. Keep the passage along the steps clear.");
   }
  }
  RoofGeometry.Validate(b);
  if(requireAccess)BuildingAccess.Validate(b);
 }
 public static int Regenerate(MapProject project,string? id=null){
  ProjectValidator.Validate(project);project.Version=Math.Max(4,project.Version);var changed=0;
  foreach(var b in project.Buildings.Where(b=>id is null?!b.Modified:b.Id==id)){
   var old=b.Design;var design=Generate(b,old is null?1:old.Floors.Count+old.MinLevel,unchecked((old?.Variant??project.Seed)+1),old?.Shape is "custom"?"rect":old?.Shape??"rect",-(old?.MinLevel??0));
   if(old is not null){
    design.Style=old.Style;design.RoofTileset=old.RoofTileset;design.FlatRoofTile=old.FlatRoofTile;
    for(var z=0;z<design.Floors.Count;z++){
     var f=design.Floors[z];var cells=f.Cells.ToCharArray();for(var i=0;i<cells.Length;i++)if(old.Floors[z].Cells[i]=='.')cells[i]='.';
     f.Objects.Clear();foreach(var stair in old.Floors[z].Objects.Where(o=>o.Kind=="stairs"))f.Objects.Add(new(){Kind="stairs",X=stair.X,Y=stair.Y,Direction=stair.Direction});
     foreach(var stair in old.Floors.Where((_,level)=>level==z||level==z-1).SelectMany(floor=>floor.Objects).Where(o=>o.Kind=="stairs"))for(var i=0;i<5;i++)cells[(stair.Y+(stair.Direction=="n"?i:0))*b.Width+stair.X+(stair.Direction=="w"?i:0)]='0';
     f.Cells=new(cells);f.Edges=AutoEdges(b,f,z+design.MinLevel);
     f.FloorTile=old.Floors[z].FloorTile;f.FloorTiles=new(old.Floors[z].FloorTiles);
    }
    design.Shape=old.Shape;if(b.SourceId is not null)design.Roof=old.Roof;
   }
   SettlementAccess.ConnectLandings(b,design.Variant,furnish:false,validate:false);
   for(var z=0;z<design.Floors.Count;z++)Furnish(b,z,new Random(unchecked(design.Variant+z*1009)));
   Validate(b);if(id is not null)b.Modified=true;changed++;
  }ProjectValidator.Validate(project);return changed;
 }
}
