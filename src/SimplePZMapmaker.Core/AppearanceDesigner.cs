namespace SimplePZMapmaker.Core;
public static class AppearanceDesigner
{
 public static int Apply(MapProject p,string theme,SurfaceRegion? region=null,bool styleBuildings=true){
  ProjectValidator.Validate(p);if(!EnvironmentStyles.Names.ContainsKey(theme))throw new ArgumentException("Choose a terrain appearance.");
  if(region is null){p.SurfaceTheme=theme;p.SurfaceRegions.Clear();p.Version=Math.Max(4,p.Version);}else{
   if(region.X<0||region.Y<0||region.Width<1||region.Height<1||(long)region.X+region.Width>p.Width||(long)region.Y+region.Height>p.Height)throw new ArgumentException("Select an area inside the map.");
   if(p.SurfaceRegions.Count>=128&&!p.SurfaceRegions.Any(r=>r.X==region.X&&r.Y==region.Y&&r.Width==region.Width&&r.Height==region.Height))throw new ArgumentException("The limit of 128 appearance regions has been reached. Replace an existing region or apply the appearance to the entire map.");
   p.Version=5;p.SurfaceRegions.RemoveAll(r=>r.X==region.X&&r.Y==region.Y&&r.Width==region.Width&&r.Height==region.Height);p.SurfaceRegions.Add(new(){X=region.X,Y=region.Y,Width=region.Width,Height=region.Height,Theme=theme});
  }
  var changed=0;if(styleBuildings)foreach(var b in p.Buildings.Where(b=>!b.Modified&&b.Design is not null&&(region is null||b.X>=region.X&&b.Y>=region.Y&&(long)b.X+b.Width<=region.X+region.Width&&(long)b.Y+b.Height<=region.Y+region.Height))){VisualStyles.Apply(b);changed++;}
  ProjectValidator.Validate(p);return changed;
 }
}
