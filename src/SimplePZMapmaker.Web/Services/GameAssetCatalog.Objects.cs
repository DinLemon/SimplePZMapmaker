namespace SimplePZMapmaker.Web.Services;

public sealed partial class GameAssetCatalog
{
 private Dictionary<string,Asset>? objectIndexSource;
 private Dictionary<string,Placement> objectPlacements=[];
 private sealed record ObjectEntry(Asset Asset,Placement Placement);
 private ObjectEntry[] objectEntries=[];

 // Rebuild once per immutable catalog view (refresh/discover/activate replace items).
 // Segment the full family BEFORE provenance filtering: a partial mod override must
 // never turn the remaining base-game cells into a separate "complete" object.
 private void EnsureObjectIndex(){
  if(ReferenceEquals(objectIndexSource,items))return;
  var placements=new Dictionary<string,Placement>(StringComparer.Ordinal);var entries=new List<ObjectEntry>();
  var originalGroups=new Dictionary<string,string[]>(StringComparer.Ordinal);
  foreach(var group in GridSegments(gameItems.Values)){var ids=group.Select(a=>a.Id).ToArray();foreach(var id in ids)originalGroups[id]=ids;}
  void Add(List<Asset> segment){
   if(segment.Count==0)return;var first=segment[0];
   if(segment.Any(a=>!ObjectTile(a)||a.Source!=first.Source||a.ModId!=first.ModId))return;
   var parts=new List<Part>();var positions=new HashSet<(int,int)>();
   foreach(var a in segment){var xy=a.Grid?.Split(',');int x=0,y=0;
    if(a.Grid is not null&&(xy!.Length!=2||!int.TryParse(xy[0],out x)||!int.TryParse(xy[1],out y)))return;
    if(x is <0 or >7||y is <0 or >7||!positions.Add((x,y)))return;
    parts.Add(new(x,y,a.Id,Flags(a)));
   }
   var width=parts.Max(p=>p.Dx)+1;var height=parts.Max(p=>p.Dy)+1;
   if(parts.Count>32||parts.Count!=width*height)return;
   if(first.Grid is not null){var ids=segment.Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);if(segment.Any(a=>originalGroups.TryGetValue(a.Id,out var original)&&original.Any(id=>!ids.Contains(id))))return;}
   var placement=new Placement(first.Id,first.Name,first.Category,segment.Any(a=>a.Blocking),parts,first.Source,first.ModId,segment.SelectMany(a=>a.Requirements??[]).Distinct().ToArray());
   foreach(var a in segment)placements[a.Id]=placement with{Id=a.Id};
   // A lone grid cell can be an orphaned multi-tile object. Raw mode retains it;
   // only ungridded singletons or validated multi-cell rectangles get object cards.
   if(first.Grid is null||parts.Count>1)entries.Add(new(first,placement));
  }
  foreach(var a in items.Values.Where(a=>a.Grid is null))Add([a]);
  foreach(var segment in GridSegments(items.Values))Add(segment);
  objectPlacements=placements;objectEntries=entries.OrderBy(e=>e.Asset.Id,StringComparer.Ordinal).ToArray();objectIndexSource=items;
 }
 private static IEnumerable<List<Asset>> GridSegments(IEnumerable<Asset> assets){
  foreach(var family in assets.Where(a=>a.Grid is not null).GroupBy(a=>(Prefix(a.Id),a.Name,a.Group,a.Facing))){
   var segment=new List<Asset>();var seen=new HashSet<string>();
   foreach(var a in family.OrderBy(a=>Number(a.Id))){if(!seen.Add(a.Grid!)){yield return segment;segment=[];seen=[];seen.Add(a.Grid!);}segment.Add(a);}yield return segment;
  }
 }
 private static bool ObjectTile(Asset a)=>!a.Floor&&a.Category is not ("walls" or "doors" or "windows" or "roofs" or "stairs")&&!a.Properties.Keys.Any(p=>p is "WallN" or "WallW" or "attachedN" or "attachedW");
 public object SearchObjects(string? q,string? category,int offset=0,int limit=72,string? source=null,string? facing=null){lock(sync){
  q=(q??"").Trim();if(q.Length>120||offset<0||offset>200000||limit is <1 or >120||facing is {Length:>8})throw new ArgumentException("Check the catalog search and page.");
  EnsureObjectIndex();
  var matches=objectEntries.Where(e=>{var a=e.Asset;return (string.IsNullOrEmpty(source)||source=="game"&&a.ModId is null||source=="mod"&&a.ModId is not null||a.ModId==source)&&(string.IsNullOrEmpty(category)||a.Category==category)&&(string.IsNullOrEmpty(facing)||a.Facing.Equals(facing,StringComparison.OrdinalIgnoreCase))&&(q.Length==0||a.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||a.Group.Contains(q,StringComparison.OrdinalIgnoreCase)||e.Placement.Parts.Any(p=>p.Tile.Contains(q,StringComparison.OrdinalIgnoreCase)));}).ToArray();
  return new{total=matches.Length,offset,limit,items=matches.Skip(offset).Take(limit).Select(e=>{var a=e.Asset;var p=e.Placement;return new{a.Id,a.Category,a.Name,a.Group,a.Facing,grid=(string?)null,p.Blocking,a.Floor,a.Page,a.Sprite,p.Source,p.ModId,p.Requirements,pageWidth=a.Page is not null?atlases[a.Page].Page.Width:0,pageHeight=a.Page is not null?atlases[a.Page].Page.Height:0,water=false,roles=Roles(a),placeable=true,wholeObject=true,p.Parts,width=p.Parts.Max(p=>p.Dx)+1,height=p.Parts.Max(p=>p.Dy)+1};}).ToArray()};
 }}
}
