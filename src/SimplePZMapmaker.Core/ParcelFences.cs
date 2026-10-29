namespace SimplePZMapmaker.Core;

/// <summary>Native fence runs built only on available parcel boundary cells, with access gaps retained.</summary>
public static class ParcelFences
{
 public static readonly FenceStyle Picket=new(){Id="generated-picket",W="fencing_01_4",N="fencing_01_5",NW="fencing_01_6",Post="fencing_01_7"};
 public static List<FenceLine> Runs(string prefix,FenceStyle style,IEnumerable<(int X,int Y,bool North)> edges)
 {
  var lines=new List<FenceLine>();
  foreach(var group in edges.Distinct().GroupBy(e=>(e.North,Fixed:e.North?e.Y:e.X)).OrderBy(g=>g.Key.North).ThenBy(g=>g.Key.Fixed))
  {
   var values=group.Select(e=>e.North?e.X:e.Y).Order().ToArray();
   for(var i=0;i<values.Length;i++){var start=values[i];var end=start;while(i+1<values.Length&&values[i+1]==end+1)end=values[++i];
    var a=group.Key.North?new FencePoint(start,group.Key.Fixed):new FencePoint(group.Key.Fixed,start);var b=group.Key.North?new FencePoint(end+1,group.Key.Fixed):new FencePoint(group.Key.Fixed,end+1);
    lines.Add(new(){Id=ParametricBuildings.Stable(prefix+":"+a.X+":"+a.Y+":"+b.X+":"+b.Y),Style=style.Id,Points=[a,b]});
   }
  }
  return lines;
 }
 public static void MergeStyles(MapProject project,IEnumerable<FenceStyle> styles)
 {
  foreach(var style in styles){var prior=project.FenceStyles.FirstOrDefault(s=>s.Id==style.Id);if(prior is null)project.FenceStyles.Add(NeighborhoodPlanner.Copy(style));else if(System.Text.Json.JsonSerializer.Serialize(prior)!=System.Text.Json.JsonSerializer.Serialize(style))throw new ArgumentException("Generated fence style conflicts with an existing style: "+style.Id);}
 }
}
