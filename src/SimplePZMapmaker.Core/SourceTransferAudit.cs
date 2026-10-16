namespace SimplePZMapmaker.Core;
public sealed record SourceTransferIssue(string Code,string Severity,string Category,string Message,double X,double Y,int? Z=null,string? BuildingId=null,string? SourceId=null,string? FeatureId=null);
public sealed record SourceTransferCoverage(bool SourceFeaturesAvailable,int EvaluatedFeatures,int TotalFeatures,int MaxFeatures,string PoiCoverage,GenerationQualityArea? Area);
public sealed record SourceServiceLocation(string FeatureId,string SourceId,string Category,string Name,double X,double Y,string Access,string Address,double? StraightLineTiles,double? StraightLineMetres,string RouteStatus="not-computed");
public sealed class SourceTransferReport{
 public bool Complete {get;set;}=true;
 public List<SourceTransferIssue> Issues {get;set;}=[];
 public Dictionary<string,int> Counts {get;set;}=[];
 public List<string> Notes {get;set;}=[];
 public SourceTransferCoverage Coverage {get;set;}=new(false,0,0,0,"unknown",null);
 public List<SourceServiceLocation> Services {get;set;}=[];
}
/// <summary>Compares quantized authored geometry against immutable source coordinates. It cannot certify OSM completeness.</summary>
public static class SourceTransferAudit{
 public static SourceTransferReport Analyze(MapProject p,GenerationQualityArea? area=null,int maxFeatures=5000,CancellationToken token=default){
  token.ThrowIfCancellationRequested();GeographicEvidence.Validate(p);SiteModel.Validate(p);
  if(maxFeatures is <1 or >30000)throw new ArgumentException("Audit feature budget: 1–30,000.");
  if(area is not null&&(area.X<0||area.Y<0||area.Width<1||area.Height<1||(long)area.X+area.Width>p.Width||(long)area.Y+area.Height>p.Height))throw new ArgumentException("Choose an audit area inside this section.");
  var r=new SourceTransferReport();bool Selected(GeographicFeature e){var ring=e.Geometry[0];return area is null||ring.Max(v=>v.X)>=area.X&&ring.Min(v=>v.X)<area.X+area.Width&&ring.Max(v=>v.Y)>=area.Y&&ring.Min(v=>v.Y)<area.Y+area.Height;}
  var features=p.SourceFeatures.Where(Selected).ToArray();r.Coverage=new(p.SourceFeatures.Count>0,Math.Min(features.Length,maxFeatures),features.Length,maxFeatures,"unknown",area);r.Complete=p.SourceFeatures.Count>0&&features.Length<=maxFeatures;
  r.Notes.Add("Complete means all retained evidence in the selected scope was evaluated; it does not certify source completeness, cadastral ownership, surveyed interiors or exact roof materials.");
  r.Notes.Add("Generated doors are inferred unless a source entrance is explicitly mapped. Gate/barrier evidence is retained without inventing gameplay access. Measurements describe retained source data, whose survey date and accuracy may vary.");
  r.Notes.Add("Service coverage is unknown. Listed facilities are source observations; distances are straight-line, not walking routes. No missing facility is invented.");
  if(p.SourceFeatures.Count==0)r.Notes.Add("Legacy map has no detailed source layer. Reimport the original geographic area to inspect addresses, entrances and services.");
  if(features.Length>maxFeatures)r.Notes.Add("Feature budget reached. Narrow the selected area; unexamined evidence remains unknown.");
  if(p.MetresPerTile is null)r.Notes.Add("No metre scale recorded. Distances are in tiles only.");
  else if(p.MetresPerTile>2)r.Issues.Add(new("source-scale-coarse","warning","scale",$"Scale is {p.MetresPerTile:0.##} metres per tile; narrow buildings, entrances and paths may collapse. Import smaller sections for accurate transfer.",area?.X??0,area?.Y??0));
  var byId=p.Buildings.ToDictionary(b=>b.Id);var bySource=p.Buildings.Where(b=>b.SourceId is not null).GroupBy(b=>b.SourceId!).ToDictionary(g=>g.Key,g=>g.ToArray());
  var evaluated=0;long work=0;foreach(var e in features.Take(maxFeatures)){
   token.ThrowIfCancellationRequested();evaluated++;var v=e.Geometry[0][0];void Issue(string code,string message,string severity="warning") {if(r.Issues.Count<2048)r.Issues.Add(new(code,severity,e.Kind,message,v.X,v.Y,0,e.BuildingId,e.SourceId,e.Id));else r.Complete=false;}
   r.Counts[e.Kind]=r.Counts.GetValueOrDefault(e.Kind)+1;
   if(e.Kind=="building"){
    var b=e.BuildingId is {} bid&&byId.TryGetValue(bid,out var direct)?direct:bySource.GetValueOrDefault(e.SourceId)?.FirstOrDefault();
    if(b is null){Issue("source-building-missing","Source footprint has no placed building. Inspect import skip reasons; it may cross a section or exceed native placement limits.");continue;}
    if(bySource.GetValueOrDefault(e.SourceId)?.Length>1)Issue("source-identity-duplicate","More than one placed building claims this source identity.");
    if(b.Modified||b.FootprintOrigin=="manual-source-edit"||e.Modified)Issue("source-authored-edit","Building was manually adapted; original source rings remain unchanged.","info");
    if(b.LevelsOrigin=="inferred")Issue("source-floors-inferred","Floor count is assumed; source provides no supported explicit level count.","info");
    if(e.GeometryKind!="polygon")continue;
    var boundary=new SiteParcel{Boundary=e.Geometry};var mismatch=0;var compared=0;
    // Bounded centre-sample comparison at actual building position catches displacement without treating normal edge quantization as drift.
    for(var y=b.Y;y<b.Y+b.Height;y++)for(var x=b.X;x<b.X+b.Width;x++){
     work+=e.Geometry.Sum(g=>g.Count);if(work>10_000_000){r.Complete=false;break;}
     compared++;if(SiteModel.Contains(boundary,x+.5,y+.5)!=BuildingDesigner.Occupies(b,x-b.X,y-b.Y))mismatch++;
    }
    var outsideSource=e.Geometry[0].Any(q=>q.X<b.X-1.5||q.Y<b.Y-1.5||q.X>b.X+b.Width+1.5||q.Y>b.Y+b.Height+1.5);
    if(mismatch>Math.Max(2,compared*.02)||outsideSource)Issue("source-footprint-drift",$"Game footprint differs from source beyond normal tile-edge rounding ({mismatch}/{compared} sampled cells). Original holes and boundary are available in the overlay.");
   }
   if(e.Kind=="entrance"&&e.EntranceOutcome!="mapped")Issue("source-entrance-"+(e.EntranceOutcome??"unmapped"),"Source entrance retained: "+(e.EntranceOutcome??"unmapped")+". It is not silently replaced with a playable door.");
   if(e.Kind=="entrance"&&e.EntranceOutcome=="mapped"){
    var b=e.BuildingId is {} entranceBuilding&&byId.TryGetValue(entranceBuilding,out var owner)?owner:null;
    var floor=b?.Design is {} design&&design.MinLevel<=0&&design.MinLevel+design.Floors.Count>0?design.Floors[-design.MinLevel]:null;
    if(b is null||floor is null||!floor.Edges.Any(edge=>edge.Kind is "door" or "open"&&(BuildingDesigner.Cell(b,floor,edge.X,edge.Y)=='.')!=(BuildingDesigner.Cell(b,floor,edge.X-(edge.Edge=="w"?1:0),edge.Y-(edge.Edge=="n"?1:0))=='.')&&GeographicImporter.SegmentDistance(v,new(b.X+edge.X,b.Y+edge.Y),new(b.X+edge.X+(edge.Edge=="n"?1:0),b.Y+edge.Y+(edge.Edge=="w"?1:0)))<=1.5))Issue("source-entrance-drift","Previously mapped source entrance no longer has a nearby supported exterior door. Recheck the authored edge.");
   }
   if(e.Kind=="entrance"&&e.EntranceOutcome=="budget-exceeded")r.Complete=false;
   if(e.Kind=="site"){
    var site=p.Sites.FirstOrDefault(s=>s.SourceFeatureId==e.Id);if(site is null){r.Complete=false;Issue("source-site-unmaterialized","Source area retained but no editable group is materialized (site or geometry budget). Membership is unknown.");}
    if(site is not null)foreach(var note in site.AssociationNotes){Issue("source-site-association",note,"info");if(note.Contains("budget"))r.Complete=false;}
    if(site is not null&&site.Origin=="source-edited")Issue("source-site-edited","Editable boundary differs from source; this is authored grouping, not a cadastral correction.","info");
   }
   if(e.Tags.ContainsKey("amenity")||e.Tags.ContainsKey("shop")||e.Tags.ContainsKey("healthcare")){
    var x=e.Geometry[0].Average(q=>q.X);var y=e.Geometry[0].Average(q=>q.Y);var nearest=p.Buildings.Count==0?(double?)null:p.Buildings.Min(b=>Math.Sqrt(Math.Pow(b.X+b.Width/2d-x,2)+Math.Pow(b.Y+b.Height/2d-y,2)));
    var address=string.Join(", ",new[]{"addr:city","addr:street","addr:housenumber","addr:unit"}.Select(k=>e.Tags.GetValueOrDefault(k)).Where(v=>!string.IsNullOrWhiteSpace(v)));
    r.Services.Add(new(e.Id,e.SourceId,e.Tags.GetValueOrDefault("amenity",e.Tags.GetValueOrDefault("shop",e.Tags.GetValueOrDefault("healthcare","service"))),e.Name,x,y,e.Tags.GetValueOrDefault("access","unspecified"),address,nearest,nearest*p.MetresPerTile));
   }
   if(work>10_000_000){r.Complete=false;r.Notes.Add("Geometry comparison work budget reached. Remaining footprints were not evaluated.");r.Coverage=r.Coverage with{EvaluatedFeatures=evaluated};break;}
  }
  r.Counts["errors"]=r.Issues.Count(i=>i.Severity=="error");r.Counts["warnings"]=r.Issues.Count(i=>i.Severity=="warning");r.Counts["services"]=r.Services.Count;return r;
 }
}
