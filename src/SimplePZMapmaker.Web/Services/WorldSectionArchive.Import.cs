using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

namespace SimplePZMapmaker.Web.Services;

public sealed partial class WorldSectionArchive
{
 // Raw existence checks also protect damaged indexes, recovery-only saves and
 // foreign blob folders. None of those grant an import ownership of the ID.
 void RequireNewImportIdentity(string id){
  var index=FileName(id);var legacy=Path.Combine(directory,id+".pzworld.json");
  static bool Exists(string path)=>File.Exists(path)||Directory.Exists(path);
  if(Exists(Folder(id))||Exists(index+".tmp")||Exists(legacy+".tmp")||
   Enumerable.Range(0,4).Any(v=>Exists(v==0?index:index+".previous"+v)||Exists(v==0?legacy:legacy+".previous"+v)))
   throw new ArgumentException("An import requires a new world workspace ID; existing data is preserved.");
 }

 internal ImportWriter BeginImport(string id,SemaphoreSlim publicationGate,CancellationToken token){
  token.ThrowIfCancellationRequested();RequireNewImportIdentity(id);
  Directory.CreateDirectory(directory);
  var staging=Path.Combine(Path.GetFullPath(directory),"."+id+".import-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(staging);
  return new(this,new WorldSectionArchive(staging),id,staging,publicationGate);
 }

 /// <summary>Private immutable section staging. Only a fully validated new index is published.</summary>
 public sealed class ImportWriter:IAsyncDisposable
 {
  readonly WorldSectionArchive owner,staged;
  readonly string id,stagingRoot;
  readonly SemaphoreSlim publicationGate,operations=new(1,1);
  readonly List<SectionReference> references=[];
  readonly Dictionary<string,int> positions=new(StringComparer.Ordinal);
  readonly Dictionary<string,int> fingerprints=new(StringComparer.Ordinal);
  readonly HashSet<(int,int)> cells=[];
  bool finished,committed,movedFolder;
  string? metadataHash;

  internal ImportWriter(WorldSectionArchive owner,WorldSectionArchive staged,string id,string stagingRoot,SemaphoreSlim publicationGate){
   this.owner=owner;this.staged=staged;this.id=id;this.stagingRoot=stagingRoot;this.publicationGate=publicationGate;
   References=references.AsReadOnly();
  }
  public IReadOnlyList<SectionReference> References {get;}
  void Check(){if(finished)throw new InvalidOperationException("This import has already committed or ended.");}

  public async Task WriteSectionAsync(WorldMapMember member,CancellationToken token=default){
   // Waiting without a token lets cancellation enter the owned cleanup path.
   await operations.WaitAsync();try{
    Check();token.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(member);
    if(!WorldAssembler.SafeId(member.Id))throw new ArgumentException("Invalid import section ID.");
    var p=member.Project;ProjectValidator.Validate(p);token.ThrowIfCancellationRequested();
    var replacement=positions.TryGetValue(member.Id,out var position);SectionReference? old=replacement?references[position]:null;
    if(old is not null){
     if(p.WorldX!=old.WorldX||p.WorldY!=old.WorldY||p.Width!=old.Width||p.Height!=old.Height)
      throw new ArgumentException("Replacing an import section preserves its dimensions and cell position.");
    }else{
     if(references.Count>=WorldLayoutLimits.MaxSections||cells.Count+(long)p.Width/256*(p.Height/256)>WorldLayoutLimits.MaxWorldCells)
      throw new ArgumentException("Import exceeds the section or world cell budget.");
     for(var y=p.WorldY;y<p.WorldY+p.Height/256;y++)for(var x=p.WorldX;x<p.WorldX+p.Width/256;x++)
      if(cells.Contains((x,y)))throw new ArgumentException("Import sections overlap.");
    }
    var blob=await staged.WriteBlob(id,p,SectionBytes,token);
    var next=new SectionReference(member.Id,blob.Hash,p.Name,p.WorldX,p.WorldY,p.Width,p.Height,blob.Bytes,p.Spawns.Count);
    fingerprints[blob.Hash]=fingerprints.GetValueOrDefault(blob.Hash)+1;
    if(old is not null){
     references[position]=next;
     if(--fingerprints[old.Hash]==0){fingerprints.Remove(old.Hash);File.Delete(staged.Blob(id,old.Hash));}
    }else{
     positions.Add(member.Id,references.Count);references.Add(next);
     for(var y=p.WorldY;y<p.WorldY+p.Height/256;y++)for(var x=p.WorldX;x<p.WorldX+p.Width/256;x++)cells.Add((x,y));
    }
   }catch{if(!committed){finished=true;Cleanup();}throw;}finally{operations.Release();}
  }

  public async Task<MapProject> ReadSectionAsync(string memberId,CancellationToken token=default){
   await operations.WaitAsync();try{
    Check();token.ThrowIfCancellationRequested();
    if(!positions.TryGetValue(memberId,out var index))throw new ArgumentException("Import section not found.");
    return await staged.ReadProject(id,references[index],token);
   }catch{if(!committed){finished=true;Cleanup();}throw;}finally{operations.Release();}
  }

  public async Task<(string File,long Revision)> CommitAsync(Metadata metadata,CancellationToken token=default,IProgress<int>? validationProgress=null){
   await operations.WaitAsync();try{
    Check();token.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(metadata);
    ValidateMetadataFields(metadata);
    var meta=await staged.WriteBlob(id,metadata,MetadataBytes,token);metadataHash=meta.Hash;
    var manifest=new Manifest{Id=metadata.Id,StorageId=id,Name=metadata.Name,Revision=1,MetadataHash=meta.Hash,MetadataBytes=meta.Bytes,Sections=[..references]};
    Validate(manifest);
    if(!manifest.Sections.Any(s=>s.SpawnCount>0))throw new ArgumentException("The world needs at least one dry-land spawn.");
    var savedMetadata=await staged.ReadMetadata(id,manifest,token);ValidateMetadataFields(savedMetadata);
    using(var lease=staged.Pin(id,manifest)){
     ValidateSharedLayout(lease,savedMetadata,token);
     // No complete-world read or raster collection: the shared projector samples
     // at most one additional neighbour and each authored section is then freed.
     var validated=0;foreach(var section in manifest.Sections){
      token.ThrowIfCancellationRequested();
      var project=await lease.ReadSectionAsync(section.Id,token);
      await lease.ProjectSectionAsync(section.Id,project,savedMetadata,token);validationProgress?.Report(++validated);
     }
    }
    // Serialize under the manifest byte budget before touching the public paths.
    var prepared=Path.Combine(stagingRoot,"manifest.ready");
    await using(var stream=new FileStream(prepared,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))
    using(var bounded=new BoundedArchiveStream(stream,ManifestBytes,"Import manifest exceeds its 32 MiB limit."))
     await JsonSerializer.SerializeAsync(bounded,manifest,NativeMapExporter.JsonOptions,token);

    await publicationGate.WaitAsync(token);try{
     owner.RequireNewImportIdentity(id);token.ThrowIfCancellationRequested();
     Directory.Move(staged.Folder(id),owner.Folder(id));movedFolder=true;
     // New identity only: never overwrite an index, including a racing writer's.
     // This rename is the commit point; no cancellable IO follows it.
     File.Move(prepared,owner.FileName(id));committed=true;finished=true;
    }finally{publicationGate.Release();}
    Cleanup();return(owner.FileName(id),1);
   }catch{if(!committed){finished=true;Cleanup();}throw;}finally{operations.Release();}
  }

  static void ValidateMetadataFields(Metadata metadata){
   if(metadata.Buildings is null||metadata.Buildings.Any(b=>b is null))throw new ArgumentException("Invalid shared world buildings.");
   if(metadata.Buildings.Sum(b=>(long)(b.Design?.Floors?.Sum(f=>(long)(f?.Cells?.Length??0))??0))>20_000_000)
    throw new ArgumentException("Shared buildings exceed 20 million floor tiles.");
   foreach(var b in metadata.Buildings){
    if(string.IsNullOrWhiteSpace(b.Id)||b.Id.Length>100||b.Name is null||b.Name.Length>100||b.SourceId?.Length>100||b.SourceKind?.Length>100||b.SourceLevels?.Length>100||b.SourceBasements?.Length>100||b.SourceRoof?.Length>100||b.LevelsOrigin is not("inferred" or "osm-levels" or "osm-height" or "manual")||b.AttachedTo is not null)
     throw new ArgumentException("Invalid shared world building identity, source or attachment.");
   }
  }

  static void ValidateSharedLayout(RevisionLease lease,Metadata metadata,CancellationToken token){
   if(metadata.LinearFeatures.Count==0)return;
   var layout=new WorldProject{Id=metadata.Id,Name=metadata.Name,Version=metadata.Version,LinearFeatures=metadata.LinearFeatures};
   foreach(var s in lease.PinnedManifest.Sections)layout.Maps.Add(new(){Id=s.Id,Project=new(){WorldX=s.WorldX,WorldY=s.WorldY,Width=s.Width,Height=s.Height,Name=s.Name}});
   MapProject? sampled=null;
   Terrain Ground(int x,int y){
    token.ThrowIfCancellationRequested();
    var s=lease.PinnedManifest.Sections.SingleOrDefault(s=>Inside(s,x,y))??throw new ArgumentException("A shared path reaches an unmapped world tile.");
    var p=sampled is {} cached&&cached.WorldX==s.WorldX&&cached.WorldY==s.WorldY?cached:sampled=lease.ReadSectionAsync(s.Id,token).GetAwaiter().GetResult();
    return p.Terrain[(y-s.WorldY*256)*p.Width+x-s.WorldX*256];
   }
   // Checks even paths outside every section's projection context, and enforces
   // global path geometry budgets without loading the layout's source rasters.
   WorldInfrastructure.Build(layout,Ground);
  }

  void Cleanup(){
   // A successful directory move gives ownership only of these immutable blobs.
   // Never clear an ID or recursively delete a public folder on failure.
   if(movedFolder&&!committed){
    foreach(var hash in fingerprints.Keys.Append(metadataHash).OfType<string>())TryDelete(()=>File.Delete(owner.Blob(id,hash)));
    TryDelete(()=>{var folder=owner.Folder(id);if(Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);});
   }
   TryDelete(()=>{if(Directory.Exists(stagingRoot))Directory.Delete(stagingRoot,true);});
  }
  static void TryDelete(Action action){try{action();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}}
  public async ValueTask DisposeAsync(){await operations.WaitAsync();try{finished=true;Cleanup();}finally{operations.Release();}}
 }
}
