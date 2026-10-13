namespace SimplePZMapmaker.Core;

internal static class BuildingAccess
{
 internal static void Validate(Building b){
  var d=b.Design!;var labels=new List<int[]>();var links=new List<List<int>>();var roots=new HashSet<int>();
  int Key(int x,int y,string edge)=>y*(b.Width+1)+x+(edge=="w"?(b.Width+1)*(b.Height+1):0);
  foreach(var f in d.Floors){
   var map=new int[f.Cells.Length];Array.Fill(map,-1);labels.Add(map);var walls=f.Edges.Where(e=>e.Kind is "wall" or "window").Select(e=>Key(e.X,e.Y,e.Edge)).ToHashSet();
   for(var start=0;start<map.Length;start++){
    if(f.Cells[start]=='.'||map[start]>=0)continue;var component=links.Count;links.Add([]);var queue=new Queue<int>();queue.Enqueue(start);map[start]=component;
    while(queue.TryDequeue(out var i)){var x=i%b.Width;var y=i/b.Width;foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}){var nx=x+dx;var ny=y+dy;if(BuildingDesigner.Cell(b,f,nx,ny)=='.')continue;var next=ny*b.Width+nx;var key=dx!=0?Key(Math.Max(x,nx),y,"w"):Key(x,Math.Max(y,ny),"n");if(map[next]<0&&!walls.Contains(key)){map[next]=component;queue.Enqueue(next);}}}
   }
  }
  var ground=-d.MinLevel;var g=d.Floors[ground];foreach(var e in g.Edges.Where(e=>e.Kind is "door" or "open")){
   var a=BuildingDesigner.Cell(b,g,e.X,e.Y);var nx=e.X-(e.Edge=="w"?1:0);var ny=e.Y-(e.Edge=="n"?1:0);var other=BuildingDesigner.Cell(b,g,nx,ny);
   if((a=='.')==(other=='.'))continue;var x=a=='.'?nx:e.X;var y=a=='.'?ny:e.Y;roots.Add(labels[ground][y*b.Width+x]);
  }
  if(roots.Count==0)throw new ArgumentException($"Здание «{b.Name}»: на наземном этаже (z=0) нужен наружный вход.");
  for(var z=0;z<d.Floors.Count-1;z++)foreach(var stair in d.Floors[z].Objects.Where(o=>o.Kind=="stairs")){var i=stair.Y*b.Width+stair.X;var lower=labels[z][i];var upper=labels[z+1][i];links[lower].Add(upper);links[upper].Add(lower);}
  var seen=new HashSet<int>(roots);var pending=new Queue<int>(roots);while(pending.TryDequeue(out var current))foreach(var next in links[current])if(seen.Add(next))pending.Enqueue(next);
  if(seen.Count!=links.Count)throw new ArgumentException($"Здание «{b.Name}»: есть изолированная комната или подъезд без доступа к наружному входу. Добавьте дверь, проход или лестницу.");
 }
}
