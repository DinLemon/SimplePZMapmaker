using System.Globalization;
namespace SimplePZMapmaker.Core;
public static partial class GeographicImporter
{
 public static (int Added,int Skipped) ImportWorldBuildings(string json,WorldProject world,int seed,List<string> warnings,CancellationToken token=default){
  if(world.GeoReference is null)return(0,0);using var document=Parse(json);var features=ReadFeatures(document.RootElement,out _);var reference=world.GeoReference;var projection=new GeographicProjection(reference,reference.OriginCellX,reference.OriginCellY);var added=0;var skipped=0;var existing=world.Maps.SelectMany(m=>m.Project.Buildings).Concat(world.Buildings).Select(b=>b.SourceId).Where(s=>s is not null).ToHashSet();
  foreach(var feature in features.Where(f=>f.Polygon&&f.Tags.ContainsKey("building"))){
   token.ThrowIfCancellationRequested();var sourceId=feature.Tags.GetValueOrDefault("__pz_source");if(sourceId is not null&&existing.Contains(sourceId))continue;
   var rings=feature.Rings.Select(r=>r.Select(point=>{var p=projection.Project(point);return(X:p.X+reference.OriginCellX*256,Y:p.Y+reference.OriginCellY*256);}).ToList()).ToList();var ring=rings[0];var x=(int)Math.Floor(ring.Min(p=>p.X));var y=(int)Math.Floor(ring.Min(p=>p.Y));var width=Math.Max(6,(int)Math.Ceiling(ring.Max(p=>p.X)-x));var height=Math.Max(6,(int)Math.Ceiling(ring.Max(p=>p.Y)-y));if(width>64||height>64)continue;
   var owners=new[]{WorldAssembler.MemberAt(world,x,y),WorldAssembler.MemberAt(world,x+width,y),WorldAssembler.MemberAt(world,x,y+height),WorldAssembler.MemberAt(world,x+width,y+height)};if(owners.Any(m=>m is null)||owners.Select(m=>m!.Id).Distinct().Count()<2)continue;
   var tags=feature.Tags;var sourceKind=tags.GetValueOrDefault("building","yes");var kind=tags.ContainsKey("shop")?"shop":sourceKind switch{"retail" or "commercial" or "train_station"=>"shop","apartments" or "residential"=>"apartments","school" or "college" or "kindergarten"=>"school","hospital"=>"clinic","hotel"=>"hotel","industrial"=>"industrial","warehouse" or "shed" or "service"=>"warehouse","garages" or "garage"=>"garage",_=>"house"};
   string? Tag(string name)=>tags.TryGetValue(name,out var value)?value[..Math.Min(100,value.Length)]:null;
   var b=new Building{X=x,Y=y,Width=width,Height=height,Kind=kind,Name=Tag("name")??BuildingDesigner.Names[kind],SourceId=sourceId,SourceKind=sourceKind,SourceLevels=Tag("building:levels"),SourceBasements=Tag("building:levels:underground"),SourceRoof=Tag("roof:shape")};var levels=kind is "apartments" or "school" or "clinic" or "hotel"?2:1;
   if(int.TryParse(b.SourceLevels,out var specified)&&specified>0){levels=Math.Clamp(specified,1,16);b.LevelsOrigin="osm-levels";}else if(tags.TryGetValue("height",out var h)&&double.TryParse(h.Replace(" m",""),NumberStyles.Float,CultureInfo.InvariantCulture,out var metres)&&double.IsFinite(metres)&&metres>0){levels=Math.Clamp((int)Math.Min(16,Math.Round(metres/3)),1,16);b.LevelsOrigin="osm-height";}
   var basements=int.TryParse(b.SourceBasements,out var depth)?Math.Clamp(depth,0,16):0;
   try{b.Design=BuildingDesigner.Generate(b,levels,unchecked(seed^x*73856093^y*19349663),"rect",basements);if(b.SourceRoof is "flat" or "gabled" or "shed" or "hipped")b.Design.Roof=b.SourceRoof;else if(b.SourceRoof=="skillion")b.Design.Roof="shed";ApplyOutline(b,rings);world.Buildings.Add(b);WorldAssembler.Validate(world);added++;if(sourceId is not null)existing.Add(sourceId);}catch(ArgumentException){world.Buildings.Remove(b);skipped++;}
  }
  if(added>0)warnings.Add($"Shared buildings spanning section boundaries restored from complete OSM outlines: {added}. Each is stored once in the world; rooms and tiles are clipped to native cells.");if(skipped>0)warnings.Add($"Shared buildings skipped because of water, overlaps, or inaccessible layouts: {skipped}.");return(added,skipped);
 }
}
