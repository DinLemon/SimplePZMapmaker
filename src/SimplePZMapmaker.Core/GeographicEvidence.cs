namespace SimplePZMapmaker.Core;

/// <summary>Original geographic evidence and its projected geometry. Neither is a cadastral claim.</summary>
public sealed class GeographicFeature
{
 public string Id {get;set;}="";
 public string SourceId {get;set;}="";
 public string Kind {get;set;}="poi";
 public string GeometryKind {get;set;}="point";
 public string Name {get;set;}="";
 public Dictionary<string,string> Tags {get;set;}=[];
 public List<List<SourceOutlinePoint>> SourceGeometry {get;set;}=[];
 public List<List<SitePoint>> Geometry {get;set;}=[];
 public string? BuildingId {get;set;}
 public string Origin {get;set;}="source";
 public bool Modified {get;set;}
 public string? EntranceOutcome {get;set;}
}
public static class GeographicEvidence
{
 public static bool RelevantTag(string key)=>key.StartsWith("addr:",StringComparison.Ordinal)||key is "name" or "name:en" or "official_name" or "building" or "building:part" or "building:levels" or "building:levels:underground" or "building:min_level" or "height" or "min_height" or "roof:shape" or "roof:height" or "roof:levels" or "amenity" or "shop" or "entrance" or "door" or "barrier" or "landuse" or "site" or "type" or "access" or "level" or "repeat_on" or "indoor" or "operator" or "brand" or "healthcare" or "emergency" or "wheelchair" or "tourism" or "leisure" or "office" or "craft" or "opening_hours" or "highway" or "railway" or "natural" or "waterway" or "service" or "area";
 public static Dictionary<string,string> CaptureTags(Dictionary<string,string> tags)=>tags.Where(p=>RelevantTag(p.Key)&&p.Key.Length<=100).OrderBy(p=>p.Key,StringComparer.Ordinal).Take(64).ToDictionary(p=>p.Key,p=>p.Value[..Math.Min(300,p.Value.Length)]);
 public static void Validate(MapProject project){
  if(project.SourceFeatures is null||project.SourceFeatures.Count>30000)throw new ArgumentException("Geographic evidence exceeds 30,000 records.");var ids=new HashSet<string>();long points=0;
  foreach(var f in project.SourceFeatures){
   if(f is null||string.IsNullOrWhiteSpace(f.Id)||f.Id.Length>100||!ids.Add(f.Id)||string.IsNullOrWhiteSpace(f.SourceId)||f.SourceId.Length>100||f.Name is null||f.Name.Length>300||f.Kind is not("building" or "site" or "entrance" or "poi" or "barrier" or "address")||f.GeometryKind is not("point" or "line" or "polygon")||f.Origin is not("source" or "source-edited" or "authored")||f.Tags is null||f.Tags.Count>64||f.Tags.Any(p=>p.Key.Length is <1 or >100||p.Value is null||p.Value.Length>300)||f.SourceGeometry is null||f.Geometry is null||f.SourceGeometry.Count is <1 or >32||f.SourceGeometry.Count!=f.Geometry.Count||f.BuildingId is {Length:>100}||f.EntranceOutcome is not(null or "mapped" or "unassociated" or "ambiguous" or "sealed" or "unsupported-level" or "unsupported-role" or "unsupported-edge" or "edited-building" or "blocked" or "budget-exceeded"))throw new ArgumentException("Invalid geographic evidence metadata.");
   for(var i=0;i<f.Geometry.Count;i++){var a=f.SourceGeometry[i];var b=f.Geometry[i];if(a is null||b is null||a.Count!=b.Count||a.Count<(f.GeometryKind=="point"?1:f.GeometryKind=="line"?2:4)||f.GeometryKind=="point"&&a.Count!=1||f.GeometryKind=="polygon"&&(a[0]!=a[^1]||b[0]!=b[^1])||a.Any(p=>p is null||!double.IsFinite(p.Longitude)||!double.IsFinite(p.Latitude)||p.Longitude is < -180 or >180||p.Latitude is < -85 or >85)||b.Any(p=>p is null||!double.IsFinite(p.X)||!double.IsFinite(p.Y)||Math.Abs(p.X)>2_000_000||Math.Abs(p.Y)>2_000_000))throw new ArgumentException("Invalid original or projected geographic geometry.");points+=a.Count;}
  }
  if(points>250000)throw new ArgumentException("Geographic evidence exceeds 250,000 points.");
 }
}
