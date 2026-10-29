namespace SimplePZMapmaker.Core;

/// <summary>Where each source building and fence of a world import ended up (docs/TRANSFER-CONTRACT.md, section 3).</summary>
public sealed record TransferOutcome(int PlacedSection,int PlacedWorld,int Evidence,int Lost,int Duplicated);
public sealed record TransferReport(Dictionary<string,TransferOutcome> ByKind,List<string> Problems,Dictionary<string,int> EvidenceReasons)
{
 /// <summary>The seam check passes when no source feature is lost or placed more than once.</summary>
 public bool Passed=>ByKind.Values.All(o=>o.Lost==0&&o.Duplicated==0);
}
public static partial class GeographicImporter
{
 /// <summary>Checks a geographic world against the source it was imported from: every building outline and barrier line must be
 /// placed in exactly one section, placed once at world level, or kept as evidence with a reason.</summary>
 public static TransferReport TransferCheck(string json,WorldProject world){
  if(world.GeoReference is not {} reference)throw new ArgumentException("The seam check needs a geographic world reference.");
  var features=ReadWorldFeatures(json);var projection=new GeographicProjection(reference,reference.OriginCellX,reference.OriginCellY);
  (double X,double Y) Global((double X,double Y) point){var p=projection.Project(point);return(p.X+reference.OriginCellX*256,p.Y+reference.OriginCellY*256);}
  var problems=new List<string>();var reasons=new Dictionary<string,int>(StringComparer.Ordinal);var byKind=new Dictionary<string,TransferOutcome>(StringComparer.Ordinal);
  void Count(string kind,string id,int section,int shared,Func<string?> evidence){
   var o=byKind.GetValueOrDefault(kind)??new(0,0,0,0,0);
   if(section+shared>1){o=o with{Duplicated=o.Duplicated+1};if(problems.Count<64)problems.Add($"duplicated {kind} {id}: {section} section and {shared} world placements");}
   else if(section==1)o=o with{PlacedSection=o.PlacedSection+1};
   else if(shared==1)o=o with{PlacedWorld=o.PlacedWorld+1};
   else if(evidence() is {} reason){o=o with{Evidence=o.Evidence+1};reasons[reason]=reasons.GetValueOrDefault(reason)+1;}
   else{o=o with{Lost=o.Lost+1};if(problems.Count<64)problems.Add($"lost {kind} {id}");}
   byKind[kind]=o;
  }
  var sectionBuildings=world.Maps.SelectMany(m=>m.Project.Buildings).Where(b=>b.SourceId is not null).GroupBy(b=>b.SourceId!).ToDictionary(g=>g.Key,g=>g.Count());
  var sharedBuildings=world.Buildings.Where(b=>b.SourceId is not null).GroupBy(b=>b.SourceId!).ToDictionary(g=>g.Key,g=>g.Count());
  var skipped=world.Maps.Where(m=>m.Project.ImportSummary is not null).SelectMany(m=>m.Project.ImportSummary!.SkippedOutlines.Where(o=>o.SourceId is not null&&o.Reason!="Outside the map").Select(o=>(o.SourceId!,o.Reason))).GroupBy(o=>o.Item1).ToDictionary(g=>g.Key,g=>g.First().Item2);
  var truncated=world.Maps.Any(m=>m.Project.ImportSummary?.SkippedOutlines.Count>=256);
  foreach(var f in features.Where(f=>f.Polygon&&f.Tags.ContainsKey("building")&&f.Rings.Count>0)){
   var id=f.Tags.GetValueOrDefault("__pz_source")??"";var projected=reference.Warp is null?f.Rings[0].Select(Global).ToList():projection.ProjectGeometry(f.Rings,true).SelectMany(r=>r.Select(p=>(X:p.X+reference.OriginCellX*256,Y:p.Y+reference.OriginCellY*256))).ToList();var ring=projected;
   var owners=ring.Select(p=>WorldAssembler.MemberAt(world,(int)Math.Floor(p.X),(int)Math.Floor(p.Y))?.Id).ToHashSet();
   Count("building",id,sectionBuildings.GetValueOrDefault(id),sharedBuildings.GetValueOrDefault(id),()=>skipped.TryGetValue(id,out var reason)?"skipped: "+reason:owners.All(o=>o is null)?"skipped: outside the world":owners.Count(o=>o is not null)>1||owners.Contains(null)?"skipped: crossing outline rejected at world level (water, overlap or access)":truncated?"skipped: listed only in a truncated section report":null);
  }
  foreach(var f in features.Where(f=>!f.Polygon&&FenceBarriers.Contains(f.Tags.GetValueOrDefault("barrier",""))&&f.Rings.Count>0)){
   var id=f.Tags.GetValueOrDefault("__pz_source")??"";var touches=(reference.Warp is null?f.Rings.SelectMany(r=>r).Select(Global):projection.ProjectGeometry(f.Rings,false).SelectMany(r=>r.Select(p=>(X:p.X+reference.OriginCellX*256,Y:p.Y+reference.OriginCellY*256)))).Any(p=>WorldAssembler.MemberAt(world,(int)Math.Floor(p.X),(int)Math.Floor(p.Y)) is not null);
   var sections=world.Maps.Count(m=>m.Project.Fences.Any(l=>l.SourceId==id));var shared=world.Fences.Any(l=>l.SourceId==id)?1:0;
   Count("fence",id,sections,shared,()=>touches?null:"skipped: outside the world");
  }
  return new(byKind,problems,reasons);
 }
}
