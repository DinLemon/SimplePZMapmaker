namespace SimplePZMapmaker.Core;

/// <summary>A decoration group stored once in absolute world tiles. It may cross section seams: every section draws the parts inside it.
/// Mod tiles carry their providers like a shared building does.</summary>
public sealed class SharedDecoration
{
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public int X {get;set;} public int Y {get;set;}
 public string Asset {get;set;}="";
 public List<BuildingObjects.Part> Parts {get;set;}=[];
 public List<string> RequiredMods {get;set;}=[];
 public Dictionary<string,string> AssetSources {get;set;}=[];
}
public static partial class WorldAssembler
{
 public const int MaximumSharedDecorations=10000;
 public static IEnumerable<(int X,int Y)> DecorationCells(SharedDecoration d)=>d.Parts.Select(p=>(d.X+p.Dx,d.Y+p.Dy));
 /// <summary>Shared decoration IDs never use the "world-" prefix of projected infrastructure decorations.</summary>
 public static bool SafeDecorationId(string? id)=>id is {Length:>0 and <=64}&&!id.StartsWith("world-",StringComparison.Ordinal)&&id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
 public static void ValidateDecoration(SharedDecoration d){
  if(d is null||!SafeDecorationId(d.Id)||d.X is <0 or >258000||d.Y is <0 or >258000)throw new ArgumentException("Invalid shared decoration: it needs an ID of 1-64 letters, digits, '-' or '_' (not starting with 'world-') and world tiles 0-258000.");
  VisualStyles.ValidateParts(d.Asset,d.Parts);ResourceDependencies.Validate(d.RequiredMods,d.AssetSources);
 }
 private static void ValidateDecorations(WorldProject world){
  if(world.Decorations is null||world.Decorations.Count>MaximumSharedDecorations)throw new ArgumentException($"A world can contain up to {MaximumSharedDecorations:N0} shared decorations.");
  if(world.Decorations.Count==0)return;
  var blocked=new Dictionary<(int,int),string>();var ids=new HashSet<string>(StringComparer.Ordinal);
  foreach(var b in world.Buildings)foreach(var c in BuildingCells(b))blocked.TryAdd(c,"a shared building");
  foreach(var m in world.Maps){var p=m.Project;var ox=p.WorldX*256;var oy=p.WorldY*256;
   foreach(var d in p.Decorations){ids.Add(d.Id);foreach(var part in d.Parts)blocked.TryAdd((ox+d.X+part.Dx,oy+d.Y+part.Dy),"a section decoration");}
   foreach(var b in p.Buildings)foreach(var c in BuildingCells(b))blocked.TryAdd((ox+c.X,oy+c.Y),"a building");
   foreach(var s in p.Spawns)blocked.TryAdd((ox+s.X,oy+s.Y),"a spawn point");
   foreach(var s in p.StaticModels)foreach(var c in StaticModelPlacement.FootprintCells(s))blocked.TryAdd((ox+s.X+c.Dx,oy+s.Y+c.Dy),"a model");}
  foreach(var d in world.Decorations){
   ValidateDecoration(d);if(!ids.Add(d.Id))throw new ArgumentException($"Shared decoration {d.Id} repeats a decoration ID.");
   foreach(var (x,y) in DecorationCells(d)){
    var member=MemberAt(world,x,y)??throw new ArgumentException($"Shared decoration {d.Id} reaches an empty part of the world ({x},{y}). Add an adjacent section.");var p=member.Project;
    if(p.Terrain[(y-p.WorldY*256)*p.Width+x-p.WorldX*256]==Terrain.Water)throw new ArgumentException($"Shared decoration {d.Id} stands on water at {x},{y}.");
    if(blocked.TryGetValue((x,y),out var what))throw new ArgumentException($"Shared decoration {d.Id} overlaps {what} at {x},{y}.");blocked[(x,y)]="another shared decoration";
   }
  }
 }
 /// <summary>The parts of shared decorations inside a section, as section-local decorations with the shared ID.</summary>
 public static List<WorldDecoration> DecorationsFor(IEnumerable<SharedDecoration> decorations,MapProject p){
  var ox=p.WorldX*256;var oy=p.WorldY*256;var result=new List<WorldDecoration>();
  foreach(var d in decorations){var parts=d.Parts.Where(part=>d.X+part.Dx>=ox&&d.Y+part.Dy>=oy&&d.X+part.Dx<ox+p.Width&&d.Y+part.Dy<oy+p.Height).ToList();if(parts.Count>0)result.Add(new(){Id=d.Id,X=d.X-ox,Y=d.Y-oy,Asset=d.Asset,Parts=parts});}
  return result;
 }
 public static WorldProject PutDecoration(WorldProject world,SharedDecoration decoration){ValidateDecoration(decoration);var copy=Clone(world);var prior=copy.Decorations.FindIndex(d=>d.Id==decoration.Id);var stored=CloneDecoration(decoration);if(prior>=0)copy.Decorations[prior]=stored;else copy.Decorations.Add(stored);Validate(copy);return copy;}
 public static WorldProject RemoveDecoration(WorldProject world,string id){var copy=Clone(world);if(copy.Decorations.RemoveAll(d=>d.Id==id)!=1)throw new ArgumentException("Shared decoration not found.");Validate(copy);return copy;}
 /// <summary>Moves a section decoration into the shared layer at its absolute position, or at (x, y) when given.</summary>
 public static WorldProject PromoteDecoration(WorldProject world,string memberId,string decorationId,int? x=null,int? y=null){
  var copy=Clone(world);var member=copy.Maps.SingleOrDefault(m=>m.Id==memberId)??throw new ArgumentException("Section not found.");var p=member.Project;var d=p.Decorations.SingleOrDefault(d=>d.Id==decorationId)??throw new ArgumentException("Section decoration not found.");
  if(!SafeDecorationId(d.Id))throw new ArgumentException("This decoration belongs to shared infrastructure or has an unsupported ID.");
  var tiles=d.Parts.Select(part=>part.Tile).Append(d.Asset).ToHashSet(StringComparer.Ordinal);var sources=p.AssetSources.Where(s=>tiles.Contains(s.Key)).ToDictionary(s=>s.Key,s=>s.Value,StringComparer.Ordinal);
  p.Decorations.Remove(d);copy.Decorations.Add(new(){Id=d.Id,X=x??p.WorldX*256+d.X,Y=y??p.WorldY*256+d.Y,Asset=d.Asset,Parts=[..d.Parts],AssetSources=sources,RequiredMods=sources.Values.Where(v=>v!="game").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()});
  Validate(copy);return copy;
 }
 /// <summary>After a preserve-edits reimport: shared decorations keep their ground from the prior world, and refreshed local
 /// decorations or unmodified source buildings under them give way, because the shared layer is authored.</summary>
 public static int KeepSharedDecorations(WorldProject world,WorldProject prior,List<string> warnings){
  var cleared=0;var touched=new HashSet<MapProject>();
  foreach(var d in world.Decorations)foreach(var (x,y) in DecorationCells(d)){
   var target=MemberAt(world,x,y);if(target is null)continue;var p=target.Project;var lx=x-p.WorldX*256;var ly=y-p.WorldY*256;
   if(MemberAt(prior,x,y)?.Project is {} o)p.Terrain[ly*p.Width+lx]=o.Terrain[(y-o.WorldY*256)*o.Width+x-o.WorldX*256];
   var removed=p.Decorations.RemoveAll(local=>local.Parts.Any(part=>local.X+part.Dx==lx&&local.Y+part.Dy==ly))+p.Buildings.RemoveAll(b=>b.SourceId is not null&&!b.Modified&&BuildingCells(b).Contains((lx,ly)));
   if(removed>0){cleared+=removed;touched.Add(p);}
  }
  foreach(var p in touched)SiteModel.DetachMissingMembers(p);
  if(cleared>0)warnings.Add($"Refreshed section objects under shared decorations were removed to keep the shared layer: {cleared}.");
  return cleared;
 }
 public static SharedDecoration CloneDecoration(SharedDecoration d)=>new(){Id=d.Id,X=d.X,Y=d.Y,Asset=d.Asset,Parts=[..d.Parts],RequiredMods=[..d.RequiredMods],AssetSources=new(d.AssetSources,StringComparer.Ordinal)};
}
