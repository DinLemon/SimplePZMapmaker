using System.Globalization;
namespace SimplePZMapmaker.Core;
public static partial class GeographicImporter
{
 /// <summary>Street dressing a section generated (mailboxes, bins, yard props) gives way to a shared building: the items on its squares and
 /// its one-square margin are taken out and returned so a failed placement can put them back.</summary>
 static List<(MapProject,WorldDecoration,int Index)> ClearDressing(WorldProject world,Building b){
  var cells=BuildingDesigner.Occupied(b).Select(i=>(X:b.X+i%(b.Width+1),Y:b.Y+i/(b.Width+1))).SelectMany(c=>new[]{(c.X,c.Y),(c.X-1,c.Y),(c.X,c.Y-1),(c.X-1,c.Y-1)}).ToHashSet();var removed=new List<(MapProject,WorldDecoration,int Index)>();
  foreach(var m in world.Maps){var p=m.Project;var ox=p.WorldX*256;var oy=p.WorldY*256;foreach(var item in p.Decorations.Select((d,index)=>(Decoration:d,Index:index)).Where(v=>v.Decoration.Id.StartsWith(DressingPrefix,StringComparison.Ordinal)&&v.Decoration.Parts.Any(part=>cells.Contains((ox+v.Decoration.X+part.Dx,oy+v.Decoration.Y+part.Dy)))).ToList()){p.Decorations.Remove(item.Decoration);removed.Add((p,item.Decoration,item.Index));}}
  return removed;
 }
 /// <summary>Keep source-order priority independent of section ownership. A later automatic local
 /// outline may yield to an earlier source footprint crossing a seam; authored or attached members never yield.</summary>
 static List<(MapProject Project,Building Building,int Index)> DeferLaterSources(WorldProject world,Building incoming,IReadOnlyDictionary<string,int> order){
  var removed=new List<(MapProject,Building,int)>();if(incoming.SourceId is null||!order.TryGetValue(incoming.SourceId,out var first))return removed;
  var cells=BuildingDesigner.Occupied(incoming).Select(i=>(X:incoming.X+i%(incoming.Width+1),Y:incoming.Y+i/(incoming.Width+1))).ToHashSet();
  foreach(var member in world.Maps){var p=member.Project;var ox=p.WorldX*256;var oy=p.WorldY*256;
   var candidates=p.Buildings.Select((b,i)=>(Building:b,Index:i)).Where(v=>!v.Building.Modified&&v.Building.AttachedTo is null&&v.Building.FootprintOrigin=="source-grid"&&v.Building.SourceId is {} id&&order.TryGetValue(id,out var later)&&later>first&&!p.Sites.Any(site=>site.BuildingIds.Contains(v.Building.Id))&&p.Generation?.LockedBuildingIds.Contains(v.Building.Id)!=true
    &&incoming.X<ox+v.Building.X+v.Building.Width+1&&incoming.X+incoming.Width+1>ox+v.Building.X&&incoming.Y<oy+v.Building.Y+v.Building.Height+1&&incoming.Y+incoming.Height+1>oy+v.Building.Y
    &&BuildingDesigner.Occupied(v.Building).Any(i=>cells.Contains((ox+v.Building.X+i%(v.Building.Width+1),oy+v.Building.Y+i/(v.Building.Width+1))))).ToList();
   foreach(var v in candidates){p.Buildings.Remove(v.Building);removed.Add((p,v.Building,v.Index));}
  }
  return removed;
 }
 public static (int Added,int Skipped) ImportWorldBuildings(string json,WorldProject world,int seed,List<string> warnings,CancellationToken token=default){
  if(world.GeoReference is null)return(0,0);var features=ReadWorldFeatures(json);var reference=world.GeoReference;var projection=new GeographicProjection(reference,reference.OriginCellX,reference.OriginCellY);var added=0;var skipped=0;var sectionsValidated=false;var existing=world.Maps.SelectMany(m=>m.Project.Buildings).Concat(world.Buildings).Select(b=>b.SourceId).Where(s=>s is not null).ToHashSet();
  var sourceOrder=features.Select((f,i)=>(Id:f.Tags.GetValueOrDefault("__pz_source"),Index:i)).Where(v=>v.Id is not null).GroupBy(v=>v.Id!).ToDictionary(g=>g.Key,g=>g.First().Index);
  foreach(var feature in features.Where(f=>f.Polygon&&f.Tags.ContainsKey("building"))){
   token.ThrowIfCancellationRequested();var sourceId=feature.Tags.GetValueOrDefault("__pz_source");if(sourceId is not null&&existing.Contains(sourceId))continue;
   var prepared=PrepareWorldBuilding(feature,projection,reference,0,out var invalid);if(prepared is null){if(invalid)skipped++;continue;}
   var result=PlaceWorldBuilding(prepared,world,seed,warnings,sourceOrder,existing,ref sectionsValidated);added+=result.Added;skipped+=result.Skipped;
  }
  if(added>0)warnings.Add($"Shared buildings spanning section boundaries restored from straightened OSM outlines (original evidence retained): {added}. Each is stored once in the world; rooms and tiles are clipped to native cells.");if(skipped>0)warnings.Add($"Shared buildings skipped because of water, overlaps, or inaccessible layouts: {skipped}.");return(added,skipped);
 }
 // The legacy whole-world loop and prepared bounded imports share projection and placement exactly.
 internal sealed record PreparedWorldBuilding(WorldBuildingCandidate Candidate,Dictionary<string,string> Tags,List<List<(double X,double Y)>> Rings,List<List<(double X,double Y)>> SourceRings);
 static PreparedWorldBuilding? PrepareWorldBuilding(Feature feature,GeographicProjection projection,WorldGeoReference reference,int index,out bool skipped){
  skipped=false;
   var rings=(reference.Warp is null?feature.Rings.Select(r=>(IReadOnlyList<(double X,double Y)>)r.Select(projection.Project).ToList()).ToList():projection.ProjectGeometry(feature.Rings,true).Select(r=>(IReadOnlyList<(double X,double Y)>)r).ToList()).Select(r=>r.Select(p=>(X:p.X+reference.OriginCellX*256,Y:p.Y+reference.OriginCellY*256)).ToList()).ToList();if(rings.Count==0){skipped=true;return null;}var straightRings=ImportedBuildingGeometry.Orthogonalize(rings);if(straightRings is null){skipped=true;return null;}rings=straightRings;var ring=rings[0];var x=(int)Math.Floor(ring.Min(p=>p.X));var y=(int)Math.Floor(ring.Min(p=>p.Y));var width=Math.Max(BuildingDesigner.MinimumFootprint,(int)Math.Ceiling(ring.Max(p=>p.X)-x));var height=Math.Max(BuildingDesigner.MinimumFootprint,(int)Math.Ceiling(ring.Max(p=>p.Y)-y));if(width>BuildingDesigner.MaximumFootprint||height>BuildingDesigner.MaximumFootprint)return null;
  return new(new(index,x,y,width,height),feature.Tags,rings,feature.Rings);
 }
 internal static (int Added,int Skipped) ImportPreparedWorldBuilding(PreparedWorldBuilding prepared,WorldProject world,int seed,List<string> warnings,IReadOnlyDictionary<string,int> sourceOrder,CancellationToken token){
  token.ThrowIfCancellationRequested();var sectionsValidated=false;var existing=world.Maps.SelectMany(m=>m.Project.Buildings).Concat(world.Buildings).Select(b=>b.SourceId).Where(s=>s is not null).ToHashSet();
  var result=PlaceWorldBuilding(prepared,world,seed,warnings,sourceOrder,existing,ref sectionsValidated);return result;
 }
 static (int Added,int Skipped) PlaceWorldBuilding(PreparedWorldBuilding prepared,WorldProject world,int seed,List<string> warnings,IReadOnlyDictionary<string,int> sourceOrder,HashSet<string?> existing,ref bool sectionsValidated){
  var sourceId=prepared.Tags.GetValueOrDefault("__pz_source");if(sourceId is not null&&existing.Contains(sourceId))return(0,0);
  var candidate=prepared.Candidate;var x=candidate.X;var y=candidate.Y;var width=candidate.Width;var height=candidate.Height;var rings=prepared.Rings;var added=0;var skipped=0;

   // shared when the footprint or the free line a section keeps around a building (MapGenerator.CanPlace) reaches another section;
   // a section rejects such an outline as outside its map, so it would be lost at the seam
   var owners=new[]{x-1,x,x+width}.SelectMany(cx=>new[]{y-1,y,y+height}.Select(cy=>WorldAssembler.MemberAt(world,cx,cy))).ToArray();if(new[]{owners[4],owners[5],owners[7],owners[8]}.Any(m=>m is null)||owners.Where(m=>m is not null).Select(m=>m!.Id).Distinct().Count()<2)return(0,0);
   var tags=prepared.Tags;var sourceKind=tags.GetValueOrDefault("building","yes");var kind=KindFromTag(tags)??(tags.ContainsKey("shop")?"shop":sourceKind switch{"retail" or "commercial" or "train_station"=>"shop","apartments" or "residential"=>"apartments","school" or "college" or "kindergarten"=>"school","hospital"=>"clinic","hotel"=>"hotel","industrial"=>"industrial","warehouse" or "shed" or "service"=>"warehouse","garages" or "garage"=>"garage",_=>"house"});
   string? Tag(string name)=>tags.TryGetValue(name,out var value)?value[..Math.Min(100,value.Length)]:null;
   var b=new Building{X=x,Y=y,Width=width,Height=height,Kind=kind,Name=Tag("name")??BuildingDesigner.Names[kind],SourceId=sourceId,SourceKind=sourceKind,SourceLevels=Tag("building:levels"),SourceBasements=Tag("building:levels:underground"),SourceRoof=Tag("roof:shape")};var levels=kind is "apartments" or "school" or "clinic" or "hotel"?2:1;
   if(int.TryParse(b.SourceLevels,out var specified)&&specified>0){levels=Math.Clamp(specified,1,ZLevels.AboveGroundCount);b.LevelsOrigin=tags.GetValueOrDefault("pz:evidence")=="inferred"||tags.GetValueOrDefault("source:inferred_fields","").Split(',').Contains("building:levels")?"inferred":"osm-levels";}else if(tags.TryGetValue("height",out var h)&&double.TryParse(h.Replace(" m",""),NumberStyles.Float,CultureInfo.InvariantCulture,out var metres)&&double.IsFinite(metres)&&metres>0){levels=Math.Clamp((int)Math.Min(ZLevels.AboveGroundCount,Math.Round(metres/3)),1,ZLevels.AboveGroundCount);b.LevelsOrigin="osm-height";}
   var basements=int.TryParse(b.SourceBasements,out var depth)?Math.Clamp(depth,0,ZLevels.BasementCount):0;if(levels+basements>1&&!BuildingDesigner.HoldsStairs(width,height)){levels=1;basements=0;warnings.Add($"Shared building {b.Name} is under 6 tiles on both sides and keeps one floor; original source tags were preserved.");}
   List<(MapProject,WorldDecoration,int Index)> cleared=[];List<(MapProject Project,Building Building,int Index)> deferred=[];try{b.Design=BuildingDesigner.Generate(b,levels,unchecked(seed^x*73856093^y*19349663),"rect",basements);if(b.SourceRoof is "flat" or "gabled" or "shed" or "hipped")b.Design.Roof=b.SourceRoof;else if(b.SourceRoof=="skillion")b.Design.Roof="shed";if(RoofGeometry.SelectSafeAutomaticRoof(b))warnings.Add($"Roof for shared building {b.Name} was adapted to fit z={ZLevels.Max}; original source tags were preserved.");if(specified>ZLevels.AboveGroundCount||depth>ZLevels.BasementCount)warnings.Add($"Source floor counts for shared building {b.Name} were limited to 32 above-ground and 32 basement levels.");if(!ImportedBuildingGeometry.Apply(b,rings,out var footprintReason))throw new ArgumentException(footprintReason);b.SourceOutline=ImportedBuildingGeometry.Capture(prepared.SourceRings,out var simplified);b.SourceOutlineSimplified=simplified;ApplyAppearanceTags(b,tags,null,problem=>warnings.Add($"Shared building {b.Name}: appearance tag ignored: {problem}."));ApplyRoomTags(b,tags,problem=>warnings.Add($"Shared building {b.Name}: room tag ignored: {problem}."));if(!sectionsValidated){WorldAssembler.Validate(world);sectionsValidated=true;}deferred=DeferLaterSources(world,b,sourceOrder);cleared=ClearDressing(world,b);world.Buildings.Add(b);foreach(var section in cleared.Select(c=>c.Item1).Distinct())ProjectValidator.Validate(section);WorldAssembler.ValidateKnownSectionContents(world,b);
    foreach(var item in deferred){var owner=item.Project;var later=item.Building;if(later.SourceId is {} laterId)existing.Remove(laterId);
     foreach(var evidence in owner.SourceFeatures.Where(e=>e.BuildingId==later.Id)){evidence.BuildingId=null;if(evidence.Kind=="entrance")evidence.EntranceOutcome="unassociated";}
     if(owner.ImportSummary is {} report){const string reason="Deferred to earlier source priority across a section seam";report.PlacedBuildings=owner.Buildings.Count;report.SkipReasons[reason]=report.SkipReasons.GetValueOrDefault(reason)+1;if(report.SkippedOutlines.Count<256)report.SkippedOutlines.Add(new(later.SourceId,later.Name,reason,later.X,later.Y,later.Width,later.Height));}
    }
    if(deferred.Count>0)warnings.Add($"Shared source {b.SourceId} retained its earlier source priority; {deferred.Count} later automatic local outlines remain as source evidence. Modified, attached and site-owned buildings were protected.");
    added++;if(sourceId is not null)existing.Add(sourceId);}catch(ArgumentException){world.Buildings.Remove(b);foreach(var (owner,d,index) in cleared.OrderBy(v=>v.Index))owner.Decorations.Insert(index,d);foreach(var item in deferred.OrderBy(v=>v.Index))item.Project.Buildings.Insert(item.Index,item.Building);skipped++;}
  return(added,skipped);
 }
}
