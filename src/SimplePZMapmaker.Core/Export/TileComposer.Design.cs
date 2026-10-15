namespace SimplePZMapmaker.Core.Export;
internal sealed partial class TileComposer
{
 private readonly Dictionary<int,List<string>> layers=[];
 private readonly Dictionary<int,int> upperRoomIds=[];
 internal int MaxLevel {get;private set;}=1;
 internal int MinLevel {get;private set;}
 internal int RoomAt(int x,int y,int z)=>z==0?RoomIds[y*256+x]:upperRoomIds.GetValueOrDefault(z*65536+y*256+x,-1);
 private void Set(int x,int y,int z,List<string> tiles){if(!ZLevels.Contains(z))throw new ArgumentException("An exported tile exceeds the Build 42 signed floor range (-32 to 31).");if(!InCell(x,y))return;foreach(var tile in tiles)authoredTiles.Add(tile);if(z==0)ground[y*256+x]=tiles;else layers[z*65536+y*256+x]=tiles;}
 private void AddAt(int x,int y,int z,string tile,byte flags=0){if(!ZLevels.Contains(z))throw new ArgumentException("An exported tile exceeds the Build 42 signed floor range (-32 to 31).");if(!InCell(x,y))return;var list=At(x,y,z);if(z==0&&!ground.ContainsKey(y*256+x))ground[y*256+x]=list;else if(z!=0&&!layers.ContainsKey(z*65536+y*256+x))layers[z*65536+y*256+x]=list;authoredTiles.Add(tile);list.Add(tile);if(z==0)Navigation[y*256+x]|=flags;}
 private void ComposeDesign(Building b){
  var design=b.Design!;var x0=b.X-ox;var y0=b.Y-oy;var building=new List<int>();MinLevel=Math.Min(MinLevel,design.MinLevel);MaxLevel=Math.Max(MaxLevel,RoofGeometry.MaxLevel(b));
  var wallBase=design.Material switch{"brick"=>16,"plaster"=>32,_=>0};
  var style=design.Style.Length>0?VisualStyles.All[design.Style]:null;
  for(var index=0;index<design.Floors.Count;index++){
   var z=index+design.MinLevel;var floor=design.Floors[index];
   for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)if(BuildingDesigner.Cell(b,floor,x,y)!='.'){
    if(!InCell(x0+x,y0+y))continue;var roomType=floor.Rooms[BuildingDesigner.Cell(b,floor,x,y).ToString()];var outside=roomType=="balcony";var baseFloor=floor.FloorTile??(style is null?design.Material=="wood"?"floors_interior_tilesandwood_01_40":"floors_interior_tilesandwood_01_0":roomType is "bathroom" or "kitchen"?style.WetFloor:style.Floor);var tiles=new List<string>{floor.FloorTiles.GetValueOrDefault(y*b.Width+x,baseFloor)};Set(x0+x,y0+y,z,tiles);if(z==0)Navigation[(y0+y)*256+x0+x]=outside?(byte)0:(byte)16;
   }
   foreach(var room in floor.Rooms.Where(r=>floor.Cells.Contains(r.Key[0]))){
    var rectangles=new List<RoomRect>();
    for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;){if(floor.Cells[y*b.Width+x]!=room.Key[0]){x++;continue;}var start=x;while(x<b.Width&&floor.Cells[y*b.Width+x]==room.Key[0])x++;
     var previous=rectangles.LastOrDefault(r=>r.X==x0+start&&r.Width==x-start&&r.Y+r.Height==y0+y);
     if(previous is not null){var i=rectangles.IndexOf(previous);rectangles[i]=previous with{Height=previous.Height+1};}else rectangles.Add(new(x0+start,y0+y,x-start,1));
    }
    rectangles=rectangles.Select(r=>new RoomRect(Math.Max(0,r.X),Math.Max(0,r.Y),Math.Min(256,r.X+r.Width)-Math.Max(0,r.X),Math.Min(256,r.Y+r.Height)-Math.Max(0,r.Y))).Where(r=>r.Width>0&&r.Height>0).ToList();if(rectangles.Count==0)continue;
    var id=Rooms.Count;building.Add(id);var roomName=room.Value switch{"hotelroom"=>"motelroom","balcony"=>"emptyoutside","garage"=>"garagestorage",_=>room.Value};Rooms.Add(new(roomName,0,0,0,0,Buildings.Count,z,rectangles));
    for(var i=0;i<floor.Cells.Length;i++)if(floor.Cells[i]==room.Key[0]&&InCell(x0+i%b.Width,y0+i/b.Width)){var key=(y0+i/b.Width)*256+x0+i%b.Width;if(z==0)RoomIds[key]=id;else upperRoomIds[z*65536+key]=id;}
   }
   foreach(var e in floor.Edges){
    if(e.Kind=="open")continue;var north=e.Edge=="n";var tile=(style?.WallBase??wallBase)+(e.Kind=="door"?10:e.Kind=="window"?8:0)+(north?1:0);
    var a=BuildingDesigner.Cell(b,floor,e.X,e.Y);var other=north?BuildingDesigner.Cell(b,floor,e.X,e.Y-1):BuildingDesigner.Cell(b,floor,e.X-1,e.Y);var inside=a=='.'?other:a;var balcony=(a=='.'||other=='.')&&floor.Rooms.GetValueOrDefault(inside.ToString())=="balcony";
    if(balcony&&e.Kind=="wall"){AddAt(x0+e.X,y0+e.Y,z,e.WallTile??$"carpentry_02_{(north?101:100)}",north?(byte)2:(byte)4);continue;}
    AddAt(x0+e.X,y0+e.Y,z,e.WallTile??$"{style?.Wall??"walls_exterior_house_01"}_{tile}",north?(byte)2:(byte)4);
    if(e.Kind is "door" or "window")AddAt(x0+e.X,y0+e.Y,z,e.OpeningTile??$"fixtures_{(e.Kind=="door"?"doors":"windows")}_01_{(north?1:0)+(e.Kind=="door"?style?.DoorBase??0:style?.WindowBase??0)}");
   }
   foreach(var o in floor.Objects){
    if(o.Kind=="stairs"){
     for(var i=1;i<=3;i++){var x=x0+o.X+(o.Direction=="w"?i:0);var y=y0+o.Y+(o.Direction=="n"?i:0);AddAt(x,y,z,$"carpentry_02_{(o.Direction=="n"?99:91)-i}");}
    }else foreach(var part in BuildingObjects.Parts(o))AddAt(x0+o.X+part.Dx,y0+o.Y+part.Dy,z,part.Tile,o.Kind=="asset"?part.Flags:(byte)1);
   }
  }
  // TileZed stair runs are five squares: upper landing, three steps, lower landing.
  // Clear floor tiles directly above the three step sprites, preserving upper walls/objects.
  for(var index=0;index<design.Floors.Count-1;index++)foreach(var o in design.Floors[index].Objects.Where(o=>o.Kind=="stairs"))for(var i=1;i<=3;i++){
   var z=index+design.MinLevel;
   var x=x0+o.X+(o.Direction=="w"?i:0);var y=y0+o.Y+(o.Direction=="n"?i:0);if(!InCell(x,y))continue;var tiles=At(x,y,z+1);var upper=design.Floors[index+1];var cellIndex=(y-y0)*b.Width+x-x0;tiles.RemoveAll(t=>t.StartsWith("floors_",StringComparison.Ordinal)||t==upper.FloorTile||upper.FloorTiles.TryGetValue(cellIndex,out var custom)&&t==custom);
  }
  // Exposed lower-floor terraces and the final floor receive weather cover at the next z.
  for(var index=0;design.Roof!="none"&&index<design.Floors.Count;index++){
   var z=index+design.MinLevel;var f=design.Floors[index];for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++){
    var code=BuildingDesigner.Cell(b,f,x,y);if(code=='.'||f.Rooms[code.ToString()]=="balcony"||index+1<design.Floors.Count&&BuildingDesigner.Cell(b,design.Floors[index+1],x,y)!='.'||z==-1)continue;
    AddAt(x0+x,y0+y,z+1,design.Roof=="flat"?design.FlatRoofTile??"floors_interior_tilesandwood_01_40":"floors_interior_tilesandwood_01_40");
    if(index==design.Floors.Count-1&&design.Roof!="flat"){
     // TileZed's 30-degree roof palette has six slope pieces per vertical level.
     var piece=RoofGeometry.Piece(b,f,x,y);var roofLevel=z+1+piece.Depth/6;var tileIndex=piece.TileIndex;
     MaxLevel=Math.Max(MaxLevel,roofLevel);AddAt(x0+x,y0+y,roofLevel,$"{design.RoofTileset??style?.Roof??"roofs_30_01"}_{tileIndex}");
    }
   }
  }
  if(building.Count>0)Buildings.Add(building);
 }
}
