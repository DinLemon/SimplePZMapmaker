namespace SimplePZMapmaker.Core;

public static class BuildingDesigner
{
 public static readonly string[] Kinds=["house","apartments","school","shop","warehouse","industrial","clinic","hotel","garage"];
 public static readonly Dictionary<string,string> Names=new(){["house"]="Частный дом",["apartments"]="Многоквартирный дом",["school"]="Школа",["shop"]="Магазин",["warehouse"]="Склад",["industrial"]="Промышленный корпус",["clinic"]="Больница",["hotel"]="Гостиница",["garage"]="Гараж"};
 public static readonly string[] RoomNames=["hall","livingroom","kitchen","bedroom","bathroom","classroom","office","conveniencestore","warehouse","medical","hotelroom","garage","storage","balcony"];
 public static char Cell(Building b,BuildingFloor floor,int x,int y)=>x<0||y<0||x>=b.Width||y>=b.Height?'.':floor.Cells[y*b.Width+x];
 public static bool Occupies(Building b,int x,int y)=>b.Design is null?x>=0&&y>=0&&x<b.Width&&y<b.Height:b.Design.Floors.Any(f=>Cell(b,f,x,y)!='.');
 public static bool OccupiesAbove(Building b,int x,int y)=>b.Design is null?Occupies(b,x,y):b.Design.Floors.Where((_,i)=>i+b.Design.MinLevel>=0).Any(f=>Cell(b,f,x,y)!='.');
 public static HashSet<int> Occupied(Building b,bool perimeter=true){
  var result=new HashSet<int>();for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)if(Occupies(b,x,y)){result.Add(y*(b.Width+1)+x);if(perimeter){result.Add(y*(b.Width+1)+x+1);result.Add((y+1)*(b.Width+1)+x);}}
  if(b.Design is null&&perimeter)for(var y=0;y<=b.Height;y++)for(var x=0;x<=b.Width;x++)result.Add(y*(b.Width+1)+x);return result;
 }
 public static BuildingDesign Generate(Building b,int levels,int seed,string shape="rect",int basements=0){
  if(b.Width is <6 or >64||b.Height is <6 or >64||levels is <1 or >16||basements is <0 or >16||!Kinds.Contains(b.Kind)||shape is not("rect" or "l" or "t" or "courtyard" or "annex"))throw new ArgumentException("Проверьте тип, размеры, форму и этажность здания: 1–16 этажей и 0–16 подвалов.");
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
  for(var z=0;z<levels;z++){
   var f=design.Floors[z];f.Edges=AutoEdges(b,f,z+design.MinLevel);
   Furnish(b,z,rng);
  }
  Validate(b);
  return design;
 }
 public static void Furnish(Building b,int z,Random rng){
  var f=b.Design!.Floors[z];f.Objects.RemoveAll(o=>o.Kind!="stairs");
  foreach(var code in f.Rooms.Keys.Where(k=>k!="0"&&f.Rooms[k]!="balcony")){
   var passages=f.Edges.Where(e=>e.Kind is "door" or "open").SelectMany(e=>e.Edge=="n"?new[]{e.Y*b.Width+e.X,(e.Y-1)*b.Width+e.X}:new[]{e.Y*b.Width+e.X,e.Y*b.Width+e.X-1}).ToHashSet();
   var kind=f.Rooms[code] switch{"kitchen"=>"counter","bedroom" or "hotelroom"=>"bed","livingroom"=>"seat","bathroom"=>"sink",_=>"storage"};var taken=f.Objects.Where(o=>o.Kind!="stairs").SelectMany(BuildingObjects.Cells).ToHashSet();
   var candidates=Enumerable.Range(0,f.Cells.Length).Where(i=>f.Cells[i]==code[0]).SelectMany(i=>new[]{"n","w"}.Select(direction=>new BuildingObject{X=i%b.Width,Y=i/b.Width,Kind=kind,Direction=direction})).Where(o=>BuildingObjects.Cells(o).All(point=>Cell(b,f,point.X,point.Y)==code[0]&&!StairSpace(b,z,point.X,point.Y)&&!passages.Contains(point.Y*b.Width+point.X)&&!taken.Contains(point))).ToArray();
   if(candidates.Length>6)f.Objects.Add(candidates[rng.Next(candidates.Length)]);
  }
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
 public static void Validate(Building b){
  if(b is null||b.Width is <6 or >64||b.Height is <6 or >64||!Kinds.Contains(b.Kind))throw new ArgumentException("Проверьте размеры и тип здания.");
  if(b.Attribution is null||b.Attribution.Count>30||b.Attribution.Any(s=>s is null||s.Length>1000))throw new ArgumentException("Источник шаблона здания повреждён.");
  if(b.Design is null)return;var d=b.Design;
  [System.Diagnostics.CodeAnalysis.DoesNotReturn] void Fail(string text)=>throw new ArgumentException($"Здание «{b.Name}»: {text}");
  if(d.Material is not("wood" or "brick" or "plaster")||d.Roof is not("flat" or "gabled" or "shed" or "hipped")||d.Shape is not("rect" or "l" or "t" or "courtyard" or "annex" or "custom")||d.Floors is null||d.MinLevel is < -16 or >0||d.Floors.Count is <1 or >32||d.Floors.Count+d.MinLevel is <1 or >16)Fail("повреждён материал, крыша или этажность: 1–16 этажей и 0–16 подвалов.");
  if(d.Floors.Any(f=>f is null||f.Cells is null||f.Cells.Length!=b.Width*b.Height||f.Objects is null||f.Objects.Count>512||f.Edges is null||f.Edges.Count>2*(b.Width+1)*(b.Height+1)||f.Edges.Any(e=>e is null)||f.Rooms is null))Fail("слой этажа повреждён.");
  for(var z=0;z<d.Floors.Count;z++){
   var f=d.Floors[z];if(f is null||f.Cells is null||f.Cells.Length!=b.Width*b.Height||f.Rooms is null||f.Rooms.Count is <1 or >35||f.Rooms.Any(r=>r.Key.Length!=1||!"0123456789abcdefghijklmnopqrstuvwxyz".Contains(r.Key[0])||!RoomNames.Contains(r.Value))||f.Cells.Any(c=>c!='.'&&!f.Rooms.ContainsKey(c.ToString()))||!f.Cells.Any(c=>c!='.')||f.Edges is null||f.Edges.Count>2*(b.Width+1)*(b.Height+1)||f.Objects is null||f.Objects.Count>512)Fail($"уровень {z+d.MinLevel} повреждён.");
   var keys=new HashSet<string>();foreach(var e in f.Edges){if(e is null||e.X<0||e.Y<0||e.X>b.Width||e.Y>b.Height||e.Edge is not("n" or "w")||e.Kind is not("wall" or "door" or "window" or "open")||!keys.Add($"{e.X},{e.Y},{e.Edge}")||Cell(b,f,e.X,e.Y)=='.'&&(e.Edge=="n"?Cell(b,f,e.X,e.Y-1):Cell(b,f,e.X-1,e.Y))=='.')Fail($"проверьте стены уровня {z+d.MinLevel}.");}
   var objectPositions=new HashSet<int>();foreach(var o in f.Objects){if(o is null||o.Kind is not("stairs" or "bed" or "storage" or "counter" or "seat" or "sink")||o.Direction is not("n" or "w")||Cell(b,f,o.X,o.Y)=='.'||!objectPositions.Add(o.Y*b.Width+o.X))Fail($"мебель уровня {z+d.MinLevel} стоит вне помещения или пересекается.");
    if(o.Kind=="stairs"){
     if(z==d.Floors.Count-1)Fail("лестница должна вести на следующий этаж.");
     for(var i=0;i<5;i++){var x=o.X+(o.Direction=="w"?i:0);var y=o.Y+(o.Direction=="n"?i:0);if(Cell(b,f,x,y)=='.'||Cell(b,d.Floors[z+1],x,y)=='.')Fail("лестнице нужны три ступени и площадки на обоих этажах.");}
    }
   }
   var furnitureCells=new HashSet<int>();foreach(var o in f.Objects.Where(o=>o.Kind!="stairs")){
    foreach(var point in BuildingObjects.Cells(o))if(Cell(b,f,point.X,point.Y)=='.'||StairSpace(b,z,point.X,point.Y)||!furnitureCells.Add(point.Y*b.Width+point.X))Fail("мебель пересекается, перекрывает лестницу или выходит за контур.");
    var parts=BuildingObjects.Cells(o).ToHashSet();foreach(var point in parts)foreach(var (dx,dy,edge) in new[]{(1,0,"w"),(0,1,"n")})if(parts.Contains((point.X+dx,point.Y+dy))&&f.Edges.Any(e=>e.X==point.X+dx&&e.Y==point.Y+dy&&e.Edge==edge&&e.Kind!="open"))Fail("мебель пересекает стену или проём.");
    if(f.Edges.Any(e=>e.Kind is "door" or "open"&&(parts.Contains((e.X,e.Y))||parts.Contains(e.Edge=="n"?(e.X,e.Y-1):(e.X-1,e.Y)))))Fail("мебель перекрывает дверь или проход.");
   }
   if(z<d.Floors.Count-1&&!f.Objects.Any(o=>o.Kind=="stairs"))Fail($"на этаже {z+1} нет лестницы.");
   foreach(var o in f.Objects.Where(o=>o.Kind!="stairs"))if(StairSpace(b,z,o.X,o.Y))Fail("мебель перекрывает лестницу.");
   var stairCells=new HashSet<int>();foreach(var stair in f.Objects.Where(o=>o.Kind=="stairs"))for(var i=0;i<5;i++){
    var x=stair.X+(stair.Direction=="w"?i:0);var y=stair.Y+(stair.Direction=="n"?i:0);if(!stairCells.Add(y*b.Width+x))Fail("лестницы пересекаются.");
    if(i>0)foreach(var level in new[]{z,z+1})if(d.Floors[level].Edges.Any(e=>e.X==x&&e.Y==y&&e.Edge==stair.Direction&&e.Kind!="open"))Fail("лестница пересекает стену или проём. Оставьте свободный проход вдоль ступеней.");
   }
   // Walls/closed windows block; doors/open edges connect adjacent room cells.
   var walls=f.Edges.Where(e=>e.Kind is "wall" or "window").Select(e=>$"{e.X},{e.Y},{e.Edge}").ToHashSet();
   var start=f.Cells.IndexOfAny("0123456789abcdefghijklmnopqrstuvwxyz".ToCharArray());var seen=new HashSet<int>{start};var q=new Queue<int>();q.Enqueue(start);
   while(q.TryDequeue(out var index)){var x=index%b.Width;var y=index/b.Width;foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}){var nx=x+dx;var ny=y+dy;if(Cell(b,f,nx,ny)=='.')continue;var key=dx!=0?$"{Math.Max(x,nx)},{y},w":$"{x},{Math.Max(y,ny)},n";var next=ny*b.Width+nx;if(!walls.Contains(key)&&seen.Add(next))q.Enqueue(next);}}
   if(seen.Count!=f.Cells.Count(c=>c!='.'))Fail($"на этаже {z+1} есть изолированная комната. Добавьте дверь или проход.");
   if(z+d.MinLevel==0&&!f.Edges.Any(e=>e.Kind is "door" or "open"&&(Cell(b,f,e.X,e.Y)=='.'||(e.Edge=="n"?Cell(b,f,e.X,e.Y-1):Cell(b,f,e.X-1,e.Y))=='.')))Fail("на наземном этаже (z=0) нужен наружный вход.");
  }
 }
 public static int Regenerate(MapProject project,string? id=null){
  ProjectValidator.Validate(project);project.Version=3;var changed=0;
  foreach(var b in project.Buildings.Where(b=>id is null?!b.Modified:b.Id==id)){
   var old=b.Design;var design=Generate(b,old is null?1:old.Floors.Count+old.MinLevel,unchecked((old?.Variant??project.Seed)+1),old?.Shape is "custom"?"rect":old?.Shape??"rect",-(old?.MinLevel??0));
   if(old is not null){
    for(var z=0;z<design.Floors.Count;z++){
     var f=design.Floors[z];var cells=f.Cells.ToCharArray();for(var i=0;i<cells.Length;i++)if(old.Floors[z].Cells[i]=='.')cells[i]='.';
     f.Objects.Clear();foreach(var stair in old.Floors[z].Objects.Where(o=>o.Kind=="stairs"))f.Objects.Add(new(){Kind="stairs",X=stair.X,Y=stair.Y,Direction=stair.Direction});
     foreach(var stair in old.Floors.Where((_,level)=>level==z||level==z-1).SelectMany(floor=>floor.Objects).Where(o=>o.Kind=="stairs"))for(var i=0;i<5;i++)cells[(stair.Y+(stair.Direction=="n"?i:0))*b.Width+stair.X+(stair.Direction=="w"?i:0)]='0';
     f.Cells=new(cells);f.Edges=AutoEdges(b,f,z+design.MinLevel);
    }
    design.Shape=old.Shape;if(b.SourceId is not null)design.Roof=old.Roof;
   }
   for(var z=0;z<design.Floors.Count;z++)Furnish(b,z,new Random(unchecked(design.Variant+z*1009)));
   Validate(b);if(id is not null)b.Modified=true;changed++;
  }ProjectValidator.Validate(project);return changed;
 }
}
