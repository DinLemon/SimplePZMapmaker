using System.Globalization;

namespace SimplePZMapmaker.Core;

/// <summary>One physical carriageway-width decision for source paths and roadside placement.
/// Adapted from KnoxMap generator/rendering/widths.py (MIT; docs/licenses/KNOXMAP.md).
/// This measures the carriageway, not unmapped sidewalks or verges.</summary>
public static class RoadWidthPolicy
{
 public sealed record Decision(double Metres,string Source)
 {
  public double Tiles(double metresPerTile)
  {
   if(!double.IsFinite(metresPerTile)||metresPerTile<=0)throw new ArgumentException("Road scale must be positive and finite.");
   return Math.Clamp(Metres/metresPerTile,1,48);
  }
 }
 public static Decision Resolve(IReadOnlyDictionary<string,string> tags)
 {
  foreach(var key in new[]{"width","est_width"})if(tags.TryGetValue(key,out var text)&&ParseMetres(text) is {} width)return new(width,key);
  var highway=tags.GetValueOrDefault("highway");
  var walking=highway is "footway" or "path" or "cycleway" or "steps" or "pedestrian" or "bridleway" or "corridor";
  if(!walking){
   if(Lanes(tags.GetValueOrDefault("lanes")) is {} total)return FromLanes(total,"lanes");
   var forward=Lanes(tags.GetValueOrDefault("lanes:forward"));var backward=Lanes(tags.GetValueOrDefault("lanes:backward"));
   // One directional count alone is not the total on a two-way road.
   var oneWay=tags.TryGetValue("oneway",out var mappedDirection)?mappedDirection is "yes" or "true" or "1" or "-1":tags.GetValueOrDefault("junction")=="roundabout";
   if(forward is not null&&backward is not null||oneWay&&(forward is not null||backward is not null))return FromLanes((forward??0)+(backward??0)+(Lanes(tags.GetValueOrDefault("lanes:both_ways"))??0),"directional-lanes");
   if(oneWay&&(highway is "residential" or "unclassified" or "living_street" or "road"))return FromLanes(1,"oneway");
  }
  var fallback=highway switch{
   "motorway"=>24,"trunk"=>16,"primary"=>12,"secondary"=>9,"tertiary"=>7,
   "motorway_link" or "trunk_link" or "primary_link" or "secondary_link" or "tertiary_link"=>4.2,
   "service"=>3.5,"track"=>3,"pedestrian"=>9,
   "footway" or "path" or "cycleway" or "bridleway" or "corridor"=>2.5,"steps"=>2,_=>6};
  return new(fallback,"highway");
 }
 static Decision FromLanes(int lanes,string source)=>new(Math.Clamp(lanes,1,8)*3.2+1,source);
 static int? Lanes(string? text)=>text is {Length:>0 and <=16}&&int.TryParse(text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out var n)&&n>0?Math.Min(8,n):null;
 internal static double? ParseMetres(string? text)
 {
  if(text is null||text.Length>64)return null;var value=text.Trim().ToLowerInvariant();double multiplier=1;
  if(value.Contains('\'')){
   var split=value.IndexOf('\'');var feet=value[..split].Trim();var inches=value[(split+1)..].Trim();
   if(!double.TryParse(feet,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var f)||!double.IsFinite(f)||f<0)return null;
   double i=0;if(inches.Length>0&&(!inches.EndsWith('"')||!double.TryParse(inches[..^1].Trim(),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out i)||!double.IsFinite(i)||i is <0 or >=12))return null;
   var metres=f*.3048+i*.0254;return double.IsFinite(metres)&&metres>0&&metres<=60?metres:null;
  }
  if(value.EndsWith("ft",StringComparison.Ordinal)){multiplier=.3048;value=value[..^2].TrimEnd();}
  else if(value.EndsWith('m'))value=value[..^1].TrimEnd();
  if(!double.TryParse(value.Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var parsed))return null;
  parsed*=multiplier;return double.IsFinite(parsed)&&parsed>0&&parsed<=60?parsed:null;
 }
}
