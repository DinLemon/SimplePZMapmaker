namespace SimplePZMapmaker.Core;
public static class NeighborhoodVariation {
 public static void Apply(Building building,int seed){
  var d=building.Design??throw new ArgumentException("A template needs a floor plan.");var random=new Random(seed);d.Variant=seed;
  VaryPartitions(building,random,seed);
  var choices=building.Kind is "warehouse" or "industrial" or "garage"?new[]{"industrial","commercial"}:building.Kind is "school" or "clinic" or "shop"?new[]{"commercial","modern","brick"}:new[]{"cottage","brick","stone","modern"};
  VisualStyles.Apply(building,choices[random.Next(choices.Length)]);
  if(d.Floors.Count+d.MinLevel<=3)d.Roof=new[]{"flat","gabled","shed","hipped"}[random.Next(4)];
  foreach(var floor in d.Floors){floor.FloorTile=null;floor.FloorTiles.Clear();foreach(var edge in floor.Edges){edge.WallTile=null;edge.OpeningTile=null;edge.OverlayTile=null;}}
  for(var z=0;z<d.Floors.Count;z++){var floor=d.Floors[z];floor.Objects.RemoveAll(o=>o.Kind!="stairs");BuildingDesigner.Furnish(building,z,new Random(unchecked(seed+z*1009)));floor.Objects.RemoveAll(o=>o.Kind!="stairs"&&floor.Rooms[BuildingDesigner.Cell(building,floor,o.X,o.Y).ToString()]=="hall");}
  building.SourceKind="generated-template-variant";building.Name=building.Name[..Math.Min(80,building.Name.Length)]+" (generated variant)";
  BuildingDesigner.Validate(building);
 }
 sealed record Boundary(int X,int Y,int Length,string Edge,char A,char B);
 static void VaryPartitions(Building b,Random random,int seed){
  var d=b.Design!;for(var z=-d.MinLevel;z<d.Floors.Count;z++){
   var f=d.Floors[z];var boundaries=new List<Boundary>();
   bool Room(char c)=>c!='.'&&f.Rooms[c.ToString()] is not("hall" or "balcony");
   for(var x=2;x<b.Width-2;x++)for(var y=1;y<b.Height-1;y++){var a=BuildingDesigner.Cell(b,f,x-1,y);var c=BuildingDesigner.Cell(b,f,x,y);if(a==c||!Room(a)||!Room(c))continue;var length=0;while(y+length<b.Height-1&&BuildingDesigner.Cell(b,f,x-1,y+length)==a&&BuildingDesigner.Cell(b,f,x,y+length)==c&&BuildingDesigner.Cell(b,f,x+1,y+length)==c&&!BuildingDesigner.StairSpace(b,z,x,y+length))length++;if(length>=3){boundaries.Add(new(x,y,length,"w",a,c));y+=length-1;}}
   for(var y=2;y<b.Height-2;y++)for(var x=1;x<b.Width-1;x++){var a=BuildingDesigner.Cell(b,f,x,y-1);var c=BuildingDesigner.Cell(b,f,x,y);if(a==c||!Room(a)||!Room(c))continue;var length=0;while(x+length<b.Width-1&&BuildingDesigner.Cell(b,f,x+length,y-1)==a&&BuildingDesigner.Cell(b,f,x+length,y)==c&&BuildingDesigner.Cell(b,f,x+length,y+1)==c&&!BuildingDesigner.StairSpace(b,z,x+length,y))length++;if(length>=3){boundaries.Add(new(x,y,length,"n",a,c));x+=length-1;}}
   var originalCells=f.Cells;var originalEdges=f.Edges;var originalObjects=f.Objects;var changed=false;
   foreach(var border in boundaries.OrderBy(_=>random.Next()).Take(12)){
    var delta=(seed&1)==0?-1:1;if(delta<0&&Enumerable.Range(0,border.Length).Any(i=>BuildingDesigner.Cell(b,f,border.X+(border.Edge=="n"?i:-2),border.Y+(border.Edge=="w"?i:-2))!=border.A))delta=1;var cells=originalCells.ToCharArray();for(var i=0;i<border.Length;i++)cells[(border.Y+(border.Edge=="w"?i:0)+(border.Edge=="n"&&delta<0?-1:0))*b.Width+border.X+(border.Edge=="n"?i:0)+(border.Edge=="w"&&delta<0?-1:0)]=delta>0?border.A:border.B;f.Cells=new(cells);f.Objects=originalObjects.Where(o=>o.Kind=="stairs").ToList();f.Edges=[];
    bool BoundaryNow(BuildingEdge e){var a=BuildingDesigner.Cell(b,f,e.X,e.Y);var c=e.Edge=="n"?BuildingDesigner.Cell(b,f,e.X,e.Y-1):BuildingDesigner.Cell(b,f,e.X-1,e.Y);return a!=c&&(a!='.'||c!='.');}
    foreach(var e in originalEdges){var move=e.Edge==border.Edge&&(border.Edge=="w"?e.X==border.X&&e.Y>=border.Y&&e.Y<border.Y+border.Length:e.Y==border.Y&&e.X>=border.X&&e.X<border.X+border.Length);var edge=move?new BuildingEdge{X=e.X+(e.Edge=="w"?delta:0),Y=e.Y+(e.Edge=="n"?delta:0),Edge=e.Edge,Kind=e.Kind,Edited=true,WallTile=e.WallTile,OpeningTile=e.OpeningTile}:e;if(BoundaryNow(edge))f.Edges.Add(edge);}
    var keys=f.Edges.Select(e=>(e.X,e.Y,e.Edge)).ToHashSet();foreach(var e in BuildingDesigner.AutoEdges(b,f,z+d.MinLevel))if(keys.Add((e.X,e.Y,e.Edge)))f.Edges.Add(new(){X=e.X,Y=e.Y,Edge=e.Edge,Kind="wall",Edited=true});
    try{BuildingDesigner.Validate(b);changed=true;break;}catch(ArgumentException){f.Cells=originalCells;f.Edges=originalEdges;f.Objects=originalObjects;}
   }
   if(!changed){f.Cells=originalCells;f.Edges=originalEdges;f.Objects=originalObjects;}
  }
 }
}
