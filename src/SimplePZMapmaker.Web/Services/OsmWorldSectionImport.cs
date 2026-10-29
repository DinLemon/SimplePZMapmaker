using System.Text.Json;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

/// <summary>Section rasters live only during composition, a bounded building window, or validation.
/// The source catalog and bounded shared metadata are separate from raster residency.</summary>
internal sealed class OsmWorldSectionImport(WorldStore store,GameAssetCatalog assets,StaticModelCatalog models)
{
 internal sealed class Statistics {
  public string Mode=>"incremental-sections";
  public int SectionsWritten {get;set;}
  public int PeakResidentSections {get;set;}
  public int PeakResidentCells {get;set;}
  public long PeakResidentBaseRasterBytes=>PeakResidentCells*WorldLayoutLimits.BaseRasterBytesPerCell;
  public int SharedBuildings {get;set;}
  public int SharedBuildingsSkipped {get;set;}
  public int SharedFences {get;set;}
 }
 internal sealed record Result(string File,long Revision,Statistics Assembly);
 // A candidate is at most 512 tiles wide. Its overlapping earlier shared footprints
 // extend at most another 512 tiles on each side, independent of the world area.
 internal const int MaximumWindowCells=WorldLayoutLimits.MaxResidentCells;
 public async Task<Result> BuildAsync(string id,OsmWorldImportRequest request,OsmWorldPlan plan,string source,
  Func<int,int,WorldMapMember> compose,IProgress<OsmProgress> progress,HashSet<string> warnings,CancellationToken token)
 {
  if(request.PreserveEdits)throw new ArgumentException("Existing authored worlds use the compatibility refresh path.");
  await using var writer=await store.BeginImportAsync(id,token);
  var shared=new WorldProject{Id=Guid.NewGuid().ToString("N"),Name=request.Name,GeoReference=plan.GeoReference};
  var stats=new Statistics();var fences=new FenceAccumulator(shared,request,plan);
  var version=shared.Version;var total=plan.Sections;var localSources=new Dictionary<string,int>(StringComparer.Ordinal);var sharedSources=new HashSet<string>(StringComparer.Ordinal);
  void Resident(IEnumerable<WorldMapMember> members){var list=members as ICollection<WorldMapMember>??members.ToArray();var cells=list.Sum(m=>m.Project.Width/256*(m.Project.Height/256));if(cells>MaximumWindowCells)throw new ArgumentException("A shared-building window exceeds the bounded section residency budget.");stats.PeakResidentSections=Math.Max(stats.PeakResidentSections,list.Count);stats.PeakResidentCells=Math.Max(stats.PeakResidentCells,cells);}
  async Task WriteOne(int x,int y){
   token.ThrowIfCancellationRequested();var member=compose(x,y);Resident([member]);
   foreach(var b in member.Project.Buildings)if(b.SourceId is {} sourceId)localSources[sourceId]=localSources.GetValueOrDefault(sourceId)+1;
   fences.Accept(member,x+y*plan.Columns);version=Math.Max(version,new WorldProject{GeoReference=plan.GeoReference,Maps=[member]}.Version);
   await writer.WriteSectionAsync(member,token);stats.SectionsWritten++;
   progress.Report(new("compose",stats.SectionsWritten,total,Warning:null));
  }
  for(var y=0;y<plan.Rows;y++)for(var x=0;x<plan.Columns;x++)await WriteOne(x,y);
  var byCell=new Dictionary<(int,int),WorldSectionArchive.SectionReference>();
  foreach(var s in writer.References)for(var y=s.WorldY;y<s.WorldY+s.Height/256;y++)for(var x=s.WorldX;x<s.WorldX+s.Width/256;x++)byCell.Add((x,y),s);
  var batch=GeographicImporter.PrepareWorldBuildings(source,plan.GeoReference);var completed=0;
  async Task ResolveBuilding(int index,int x,int y,int width,int height){
   token.ThrowIfCancellationRequested();var sourceId=batch.SourceId(index);if(sourceId is not null&&(sharedSources.Contains(sourceId)||localSources.ContainsKey(sourceId)))return;
   var required=new Dictionary<string,WorldSectionArchive.SectionReference>(StringComparer.Ordinal);
   void Include(int left,int top,int right,int bottom){
    for(var cy=(int)Math.Floor(top/256d);cy<=(int)Math.Floor(bottom/256d);cy++)for(var cx=(int)Math.Floor(left/256d);cx<=(int)Math.Floor(right/256d);cx++)if(byCell.TryGetValue((cx,cy),out var s))required.TryAdd(s.Id,s);
   }
   Include(x-1,y-1,x+width,y+height);
   // Ignore ordinary buildings contained in one section: the section importer owns them.
   var owners=new[]{(x,y),(x+width,y),(x,y+height),(x+width,y+height)}.Select(c=>byCell.GetValueOrDefault(((int)Math.Floor(c.Item1/256d),(int)Math.Floor(c.Item2/256d)))?.Id).ToArray();
   if(owners.Any(s=>s is null)||required.Count<2)return;
   var nearby=shared.Buildings.Where(b=>b.X<=x+width+1&&b.X+b.Width>=x-1&&b.Y<=y+height+1&&b.Y+b.Height>=y-1).ToList();
   foreach(var b in nearby)Include(b.X,b.Y,b.X+b.Width,b.Y+b.Height);
   var cells=required.Values.Sum(s=>s.Width/256*(s.Height/256));if(cells>MaximumWindowCells)throw new ArgumentException("A shared-building window exceeds its raster budget.");
   var window=new WorldProject{Id=shared.Id,Name=shared.Name,GeoReference=shared.GeoReference,Buildings=nearby};
   foreach(var s in required.Values){token.ThrowIfCancellationRequested();window.Maps.Add(new(){Id=s.Id,Project=await writer.ReadSectionAsync(s.Id,token)});}
   var beforeSources=window.Maps.SelectMany(m=>m.Project.Buildings).Where(b=>b.SourceId is not null).GroupBy(b=>b.SourceId!).ToDictionary(g=>g.Key,g=>g.Count());
   Resident(window.Maps);var before=window.Buildings.Count;var notes=new List<string>();var placed=batch.ImportOne(index,window,request.Seed,notes,token);stats.SharedBuildingsSkipped+=placed.Skipped;
   foreach(var note in notes)warnings.Add(note);
   if(window.Buildings.Count==before)return;
   var building=window.Buildings[^1];if(shared.Buildings.Count>=4000||shared.Buildings.Sum(b=>(long)(b.Design?.Floors.Sum(f=>f.Cells.Length)??0))+(building.Design?.Floors.Sum(f=>(long)f.Cells.Length)??0)>20_000_000)throw new ArgumentException("The imported shared buildings exceed the world's building or floor-cell budget.");
   AssetRules.Building(building,assets);if(building.Design is {} design)foreach(var floor in design.Floors)foreach(var model in floor.StaticModels)models.Validate(model);
   shared.Buildings.Add(building);if(building.SourceId is {} placedSource)sharedSources.Add(placedSource);
   var afterSources=window.Maps.SelectMany(m=>m.Project.Buildings).Where(b=>b.SourceId is not null).GroupBy(b=>b.SourceId!).ToDictionary(g=>g.Key,g=>g.Count());
   foreach(var (localSource,count) in beforeSources){var remaining=localSources.GetValueOrDefault(localSource)-count+afterSources.GetValueOrDefault(localSource);if(remaining<=0)localSources.Remove(localSource);else localSources[localSource]=remaining;}
   version=Math.Max(version,window.Version);
   // Placement can clear source dressing or defer a later automatic local outline.
   // Persist every affected section before releasing this window.
   foreach(var member in window.Maps)await writer.WriteSectionAsync(member,token);
  }
  foreach(var candidate in batch.Candidates){await ResolveBuilding(candidate.Index,candidate.X,candidate.Y,candidate.Width,candidate.Height);progress.Report(new("world-buildings",++completed,batch.Candidates.Count));}
  if(stats.SharedBuildingsSkipped>0)warnings.Add($"Shared buildings skipped because of water, overlaps, or inaccessible layouts: {stats.SharedBuildingsSkipped}.");
  token.ThrowIfCancellationRequested();stats.SharedBuildings=shared.Buildings.Count;stats.SharedFences=shared.Fences.Count;
  var metadata=new WorldSectionArchive.Metadata{Version=Math.Max(version,shared.Version),Id=shared.Id,Name=shared.Name,GeoReference=shared.GeoReference,Buildings=shared.Buildings,Fences=shared.Fences,FenceStyles=shared.FenceStyles,GateStyles=shared.GateStyles};
  progress.Report(new("world-validate",0,total));var committed=await writer.CommitAsync(metadata,token,new InlineProgress<int>(done=>progress.Report(new("world-validate",done,total))));
  return new(committed.File,committed.Revision,stats);
 }

 sealed class FenceAccumulator(WorldProject shared,OsmWorldImportRequest request,OsmWorldPlan plan)
 {
  readonly Dictionary<string,FenceLine> lines=new(StringComparer.Ordinal);long edges;
  int? Owner(int x,int y){var dx=x-request.OriginCellX*256;var dy=y-request.OriginCellY*256;if(dx<0||dy<0||dx>=plan.Width||dy>=plan.Height)return null;return dy/request.SectionSize*plan.Columns+dx/request.SectionSize;}
  IEnumerable<int> Owners(List<FencePoint> points){
   for(var n=1;n<points.Count;n++){var a=points[n-1];var b=points[n];var sx=Math.Sign(b.X-a.X);var sy=Math.Sign(b.Y-a.Y);var length=Math.Abs(b.X-a.X)+Math.Abs(b.Y-a.Y);
    for(var step=0;step<length;step++){var x=a.X+step*sx;var y=a.Y+step*sy;if(Owner(sx==0?x:Math.Min(x,x+sx),sy==0?y:Math.Min(y,y+sy)) is {} owner)yield return owner;}
   }
  }
  static void Styles<T>(List<T> target,IEnumerable<T> incoming,Func<T,string> key){foreach(var value in incoming){var old=target.FirstOrDefault(s=>key(s)==key(value));if(old is not null){if(JsonSerializer.Serialize(old)!=JsonSerializer.Serialize(value))throw new ArgumentException("Imported fence copies disagree on their style.");}else target.Add(value);}}
  public void Accept(WorldMapMember member,int memberIndex){
   var p=member.Project;foreach(var local in p.Fences.Where(f=>f.SourceId is not null).ToList()){
    var points=local.Points.Select(v=>new FencePoint(v.X+p.WorldX*256,v.Y+p.WorldY*256)).ToList();var owners=Owners(points).Distinct().Take(2).ToArray();
    if(owners.Length<2){if(owners.Length==1&&owners[0]!=memberIndex)p.Fences.Remove(local);continue;}
    Styles(shared.FenceStyles,p.FenceStyles.Where(s=>s.Id==local.Style),s=>s.Id);Styles(shared.GateStyles,p.GateStyles.Where(s=>local.Gates.Any(g=>g.Style==s.Id)),s=>s.Id);
    if(lines.TryGetValue(local.Id,out var existing)){
     if(!existing.Points.SequenceEqual(points)||existing.Style!=local.Style||existing.PostSpacing!=local.PostSpacing)throw new ArgumentException("Imported copies of a crossing fence have inconsistent world geometry.");
     foreach(var gate in local.Gates){var old=existing.Gates.FirstOrDefault(g=>g.At==gate.At);if(old is not null){if(JsonSerializer.Serialize(old)!=JsonSerializer.Serialize(gate))throw new ArgumentException("Imported copies of a gate disagree.");}else existing.Gates.Add(gate);}
     existing.Gates=existing.Gates.OrderBy(g=>g.At).ToList();FenceLines.Validate([existing],shared.FenceStyles,shared.GateStyles,258048);
    }else{
     var line=new FenceLine{Id=local.Id,Style=local.Style,PostSpacing=local.PostSpacing,Points=points,Gates=[..local.Gates],SourceId=local.SourceId,SourceTags=new(local.SourceTags),SourcePath=[..local.SourcePath]};
     FenceLines.Validate([line],shared.FenceStyles,shared.GateStyles,258048);
     edges+=points.Zip(points.Skip(1),(a,b)=>(long)Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y)).Sum();if(edges>FenceLines.MaximumEdges||lines.Count>=FenceLines.MaximumLines)throw new ArgumentException("Imported shared fences exceed the world edge/line budget.");
     lines.Add(line.Id,line);shared.Fences.Add(line);
    }
    p.Fences.Remove(local);
   }
  }
 }
}
