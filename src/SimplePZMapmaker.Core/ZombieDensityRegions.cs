namespace SimplePZMapmaker.Core;
public sealed class ZombieDensityRegion
{
 public string Id {get;set;}=Guid.NewGuid().ToString("N");public string Name {get;set;}="Density region";
 public int X {get;set;}public int Y {get;set;}public int Width {get;set;}=8;public int Height {get;set;}=8;
 public List<ZonePoint>? Polygon {get;set;}public int Density {get;set;}=100;public int Order {get;set;}
 public Dictionary<string,string> SourceTags {get;set;}=[];
}
public sealed class ZombieDensityState
{
 public int[] Baseline {get;set;}=[];
 public Dictionary<int,int> Applied {get;set;}=[];
 public List<int> ManualOverrides {get;set;}=[];
}
public sealed record DensityRasterChange(int Index,int X,int Y,int Previous,int Density,int CoveredTiles,bool ManualOverride);
public static class ZombieDensityRegions
{
 public static void ValidateProject(MapProject p)
 {
  if(p.ZombieDensityRegions is null||p.ZombieDensityRegions.Count>256||p.ZombieDensityRegions.Any(r=>r is null)||p.ZombieDensityRegions.Select(r=>r.Id).Distinct().Count()!=p.ZombieDensityRegions.Count)throw new ArgumentException("A map supports up to 256 distinct density regions.");long budget=0;
  foreach(var r in p.ZombieDensityRegions){ValidateIdentity(r.Id,r.Name);if(r.Density is <0 or >255||r.Order is <-1000000 or >1000000)throw new ArgumentException("Density must be 0..255 and order bounded.");ZoneGeometry.Validate(r.X,r.Y,r.Width,r.Height,r.Polygon,p.Width,p.Height);ZoneGeometry.ValidateSourceTags(r.SourceTags);budget+=(long)r.Width*r.Height;}
  if(budget>16777216)throw new ArgumentException("Density preview exceeds its 16,777,216 tile coverage budget.");
  if(p.ZombieDensityState is {} s){var count=p.Width/8*(p.Height/8);if(s.Baseline is null||s.Baseline.Length!=count||s.Baseline.Any(v=>v is <0 or >255)||s.Applied is null||s.Applied.Any(v=>v.Key<0||v.Key>=count||v.Value is <0 or >255)||s.ManualOverrides is null||s.ManualOverrides.Count>count||s.ManualOverrides.Any(i=>i<0||i>=count)||s.ManualOverrides.Distinct().Count()!=s.ManualOverrides.Count)throw new ArgumentException("Invalid reversible zombie density baseline or ownership.");}
 }
 internal static void ValidateIdentity(string id,string name){if(id is null||id.Length!=32||!id.All(Uri.IsHexDigit)||name is null||name.Length>100||name.Any(char.IsControl))throw new ArgumentException("Regions require a stable 32 digit ID and a bounded name.");}
 public static MapProject ApplyProposal(MapProject project,ZombieDensityRegion[] regions,CancellationToken cancellation=default)
 {
  if(regions is null)throw new ArgumentException("Density regions are required.");var copy=NeighborhoodPlanner.Copy(project);copy.ZombieDensityRegions=NeighborhoodPlanner.Copy(regions.ToList());ValidateProject(copy);
  var state=copy.ZombieDensityState??new(){Baseline=copy.Zombies.ToArray()};var manual=state.ManualOverrides.ToHashSet();for(var i=0;i<copy.Zombies.Length;i++){if(!state.Applied.TryGetValue(i,out var last))state.Baseline[i]=copy.Zombies[i];else if(copy.Zombies[i]!=last){state.Baseline[i]=copy.Zombies[i];manual.Add(i);}}
  copy.Zombies=state.Baseline.ToArray();var tiles=new Dictionary<int,int[]>();var rasterWidth=copy.Width/8;
  foreach(var r in copy.ZombieDensityRegions.OrderBy(r=>r.Order).ThenBy(r=>r.Id,StringComparer.Ordinal))for(var y=r.Y;y<r.Y+r.Height;y++){cancellation.ThrowIfCancellationRequested();for(var x=r.X;x<r.X+r.Width;x++){if(!ZoneGeometry.Covers(r.X,r.Y,r.Width,r.Height,r.Polygon,x,y))continue;var index=y/8*rasterWidth+x/8;if(!tiles.TryGetValue(index,out var values)){values=Enumerable.Repeat(-1,64).ToArray();tiles[index]=values;}values[y%8*8+x%8]=r.Density;}}
  state.Applied=[];foreach(var (index,values) in tiles){if(manual.Contains(index))continue;var sum=values.Sum(v=>v<0?state.Baseline[index]:v);var density=(sum+32)/64;copy.Zombies[index]=density;state.Applied[index]=density;}
  state.ManualOverrides=manual.Order().ToList();copy.ZombieDensityState=regions.Length==0?null:state;return copy;
 }
 public static DensityRasterChange[] Preview(MapProject original,MapProject applied)
 {
  var covered=new Dictionary<int,int>();foreach(var r in applied.ZombieDensityRegions)for(var y=r.Y;y<r.Y+r.Height;y++)for(var x=r.X;x<r.X+r.Width;x++)if(ZoneGeometry.Covers(r.X,r.Y,r.Width,r.Height,r.Polygon,x,y)){var i=y/8*(applied.Width/8)+x/8;covered[i]=0;}
  return covered.Keys.Union(original.ZombieDensityState?.Applied.Keys??Enumerable.Empty<int>()).Order().Select(i=>new DensityRasterChange(i,i%(applied.Width/8)*8,i/(applied.Width/8)*8,original.Zombies[i],applied.Zombies[i],Enumerable.Range(0,64).Count(n=>applied.ZombieDensityRegions.Any(r=>ZoneGeometry.Covers(r.X,r.Y,r.Width,r.Height,r.Polygon,i%(applied.Width/8)*8+n%8,i/(applied.Width/8)*8+n/8))),applied.ZombieDensityState?.ManualOverrides.Contains(i)==true)).ToArray();
 }
}
