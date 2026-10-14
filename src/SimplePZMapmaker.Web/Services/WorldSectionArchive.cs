using System.Security.Cryptography;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

/// <summary>Small atomic manifests reference immutable sections shared by four recovery revisions.</summary>
public sealed class WorldSectionArchive(string directory)
{
 public const int SectionBytes=48*1024*1024,ManifestBytes=1024*1024;
 public sealed record SectionReference(string Id,string Hash,string Name,int WorldX,int WorldY,int Width,int Height,long Bytes,int? SpawnCount=null);
 public sealed class Manifest {
  public string Format {get;set;}="SimplePZWorldSections";public int Version {get;set;}=1;
  public string Id {get;set;}="";public string StorageId {get;set;}="";public string Name {get;set;}="";public long Revision {get;set;}
  public DateTimeOffset Updated {get;set;}=DateTimeOffset.UtcNow;
  public string MetadataHash {get;set;}="";public long MetadataBytes {get;set;}
  public List<SectionReference> Sections {get;set;}=[];
 }
 public sealed class Metadata {
  public int Version {get;set;} public string Id {get;set;}="";public string Name {get;set;}="";
  public List<Building> Buildings {get;set;}=[];public WorldGeoReference? GeoReference {get;set;} public ElevationSurvey? Elevation {get;set;}
 }
 public sealed record SectionSnapshot(long Revision,WorldMapMember Member,Metadata Shared);
 public string FileName(string id){if(!WorldAssembler.SafeId(id))throw new ArgumentException("Invalid world workspace ID.");return Path.Combine(directory,id+".pzworld.index.json");}
 string Folder(string id){FileName(id);return Path.Combine(directory,id+".sections");}
 string Blob(string id,string hash){if(!Hash(hash))throw new ArgumentException("Invalid section fingerprint.");return Path.Combine(Folder(id),hash.ToLowerInvariant()+".section.json");}
 static bool Hash(string? s)=>s is {Length:64}&&s.All(char.IsAsciiHexDigit);
 static void Validate(Manifest m){
  if(m is null||m.Format!="SimplePZWorldSections"||m.Version!=1||!WorldAssembler.SafeId(m.Id)||m.StorageId is null||m.StorageId.Length>0&&!WorldAssembler.SafeId(m.StorageId)||m.Revision<1||string.IsNullOrWhiteSpace(m.Name)||m.Name.Length>80||m.Name.Any(char.IsControl)||m.Sections is null||m.Sections.Count is <1 or >128||m.Sections.Any(s=>s is null||s.SpawnCount is <0 or >100)||m.Sections.Select(s=>s.Id).Distinct().Count()!=m.Sections.Count||m.MetadataBytes is <1 or >WorldStore.MaxBytes||!Hash(m.MetadataHash))throw new ArgumentException("Invalid section workspace manifest.");
  var cells=new HashSet<(int,int)>();foreach(var s in m.Sections){if(!WorldAssembler.SafeId(s.Id)||!Hash(s.Hash)||string.IsNullOrWhiteSpace(s.Name)||s.Name.Length>80||s.WorldX is <0 or >1000||s.WorldY is <0 or >1000||s.Width is <256 or >2048||s.Height is <256 or >2048||s.Width%256!=0||s.Height%256!=0||s.Bytes is <1 or >SectionBytes)throw new ArgumentException("Invalid section workspace entry.");for(var y=s.WorldY;y<s.WorldY+s.Height/256;y++)for(var x=s.WorldX;x<s.WorldX+s.Width/256;x++)if(!cells.Add((x,y))||cells.Count>8192)throw new ArgumentException("Section workspace cells overlap or exceed their budget.");}
  if(m.MetadataBytes+m.Sections.Sum(s=>s.Bytes)>WorldStore.MaxBytes)throw new ArgumentException("The section workspace exceeds 128 MB of authored data.");
 }
 public static int[] VersionsToTry(int? version){if(version is <0 or >3)throw new ArgumentException("Recovery version must be between 0 and 3.");return version is {} n?[n]:[0,1,2,3];}
 public Task<Manifest?> ReadManifest(string id,int version,CancellationToken token)=>RecoveryArchive.Read<Manifest>(FileName(id),ManifestBytes,Validate,token,version);
 async Task<(string Hash,long Bytes)> WriteBlob<T>(string id,T value,int maximum,CancellationToken token){
  Directory.CreateDirectory(Folder(id));var tmp=Path.Combine(Folder(id),Guid.NewGuid().ToString("N")+".tmp");try{
   await using(var stream=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))await JsonSerializer.SerializeAsync(stream,value,NativeMapExporter.JsonOptions,token);
   var bytes=new FileInfo(tmp).Length;if(bytes>maximum)throw new ArgumentException("Section workspace data exceeds its size limit.");string hash;await using(var stream=File.OpenRead(tmp))hash=Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,token));var file=Blob(id,hash);var same=false;
   if(File.Exists(file)&&new FileInfo(file).Length==bytes){await using var stream=File.OpenRead(file);same=Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,token))==hash;}token.ThrowIfCancellationRequested();if(!same)File.Move(tmp,file,true);return (hash,bytes);
  }finally{if(File.Exists(tmp))File.Delete(tmp);}
 }
 async Task<T> ReadBlob<T>(string id,string hash,long bytes,int maximum,CancellationToken token){
  var file=Blob(id,hash);if(bytes<1||bytes>maximum||!File.Exists(file)||new FileInfo(file).Length!=bytes)throw new ArgumentException("A saved section is missing or damaged. Choose another recovery revision.");
  await using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);if(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream,token))!=hash.ToLowerInvariant())throw new ArgumentException("A saved section failed its content fingerprint. Choose another recovery revision.");stream.Position=0;return await JsonSerializer.DeserializeAsync<T>(stream,NativeMapExporter.JsonOptions,token)??throw new ArgumentException("A saved section is empty.");
 }
 public async Task Save(string id,WorldProject world,CancellationToken token){
  FileName(id);WorldAssembler.Validate(world);var old=await RecoveryArchive.Read<Manifest>(FileName(id),ManifestBytes,Validate,token);var metadata=new Metadata{Version=world.Version,Id=world.Id,Name=world.Name,Buildings=world.Buildings,GeoReference=world.GeoReference,Elevation=world.Elevation};var meta=await WriteBlob(id,metadata,WorldStore.MaxBytes,token);
  var manifest=new Manifest{Id=world.Id,StorageId=id,Name=world.Name,Revision=checked((old?.Revision??0)+1),MetadataHash=meta.Hash,MetadataBytes=meta.Bytes};foreach(var m in world.Maps){var p=m.Project;var blob=await WriteBlob(id,p,SectionBytes,token);manifest.Sections.Add(new(m.Id,blob.Hash,p.Name,p.WorldX,p.WorldY,p.Width,p.Height,blob.Bytes,p.Spawns.Count));}Validate(manifest);await RecoveryArchive.Write(FileName(id),manifest,ManifestBytes,token);Prune(id);
 }
 public async Task<SectionSnapshot> ReadSection(string id,Manifest m,string memberId,CancellationToken token){
  var s=m.Sections.SingleOrDefault(s=>s.Id==memberId)??throw new ArgumentException("Section not found in this revision.");var p=await ReadBlob<MapProject>(id,s.Hash,s.Bytes,SectionBytes,token);ProjectValidator.Validate(p);if(p.Name!=s.Name||p.WorldX!=s.WorldX||p.WorldY!=s.WorldY||p.Width!=s.Width||p.Height!=s.Height)throw new ArgumentException("The section differs from its saved index.");var meta=await ReadBlob<Metadata>(id,m.MetadataHash,m.MetadataBytes,WorldStore.MaxBytes,token);return new(m.Revision,new(){Id=s.Id,Project=p},meta);
 }
 public async Task<WorldProject> ReadWorld(string id,Manifest m,CancellationToken token){
  var meta=await ReadBlob<Metadata>(id,m.MetadataHash,m.MetadataBytes,WorldStore.MaxBytes,token);if(meta.Id!=m.Id||meta.Name!=m.Name)throw new ArgumentException("World metadata differs from its saved index.");var world=new WorldProject{Version=meta.Version,Id=meta.Id,Name=meta.Name,Buildings=meta.Buildings,GeoReference=meta.GeoReference,Elevation=meta.Elevation};foreach(var s in m.Sections){var p=await ReadBlob<MapProject>(id,s.Hash,s.Bytes,SectionBytes,token);if(p.WorldX!=s.WorldX||p.WorldY!=s.WorldY||p.Width!=s.Width||p.Height!=s.Height||p.Name!=s.Name)throw new ArgumentException("The section differs from its saved index.");world.Maps.Add(new(){Id=s.Id,Project=p});}WorldAssembler.Validate(world);return world;
 }
 public async Task<Manifest> SaveSection(string id,string memberId,long revision,MapProject project,CancellationToken token){
  ProjectValidator.Validate(project);var m=await ReadManifest(id,0,token)??throw new ArgumentException("Section workspace not found.");if(m.Revision!=revision)throw new AgentRevisionConflict(m.Revision);var index=m.Sections.FindIndex(s=>s.Id==memberId);if(index<0)throw new ArgumentException("Section not found.");var original=m.Sections[index];if(project.WorldX!=original.WorldX||project.WorldY!=original.WorldY||project.Width!=original.Width||project.Height!=original.Height)throw new ArgumentException("A section-only save preserves its dimensions and cell position. Load and save the complete world to change its layout.");
  if(m.Sections.Any(s=>s.SpawnCount is null)){var previous=await ReadWorld(id,m,token);for(var n=0;n<m.Sections.Count;n++)m.Sections[n]=m.Sections[n] with{SpawnCount=previous.Maps[n].Project.Spawns.Count};}
  if(project.Spawns.Count+m.Sections.Where(s=>s.Id!=memberId).Sum(s=>s.SpawnCount??0)==0)throw new ArgumentException("The world needs at least one dry-land spawn.");
  var meta=await ReadBlob<Metadata>(id,m.MetadataHash,m.MetadataBytes,WorldStore.MaxBytes,token);ValidateSharedReplacement(meta,project);var blob=await WriteBlob(id,project,SectionBytes,token);m.Sections[index]=new(memberId,blob.Hash,project.Name,project.WorldX,project.WorldY,project.Width,project.Height,blob.Bytes,project.Spawns.Count);m.Revision=checked(m.Revision+1);m.Updated=DateTimeOffset.UtcNow;Validate(m);await RecoveryArchive.Write(FileName(id),m,ManifestBytes,token);Prune(id);return m;
 }
 static void ValidateSharedReplacement(Metadata meta,MapProject p){
  if(meta.Buildings is null)throw new ArgumentException("Shared world metadata is damaged.");var shared=new HashSet<int>();var rooms=new HashSet<int>();var ox=p.WorldX*256;var oy=p.WorldY*256;foreach(var b in meta.Buildings){if(p.Buildings.Any(local=>local.Id==b.Id))throw new ArgumentException("A local building repeats a shared world building ID.");foreach(var (wx,wy) in WorldAssembler.BuildingCells(b)){var x=wx-ox;var y=wy-oy;if(x<0||y<0||x>=p.Width||y>=p.Height)continue;var i=y*p.Width+x;shared.Add(i);if(BuildingDesigner.Occupies(b,wx-b.X,wy-b.Y)){rooms.Add(i);if(p.Terrain[i]==Terrain.Water)throw new ArgumentException("The changed section places a shared building on water.");}}}
  foreach(var b in p.Buildings)foreach(var i in BuildingDesigner.Occupied(b)){var x=b.X+i%(b.Width+1);var y=b.Y+i/(b.Width+1);if(shared.Contains(y*p.Width+x))throw new ArgumentException("A local building overlaps a shared world building.");}
  foreach(var s in p.Spawns)if(shared.Contains(s.Y*p.Width+s.X))throw new ArgumentException("A spawn overlaps a shared world building.");
  foreach(var m in p.StaticModels)foreach(var c in StaticModelPlacement.FootprintCells(m))if(shared.Contains((m.Y+c.Dy)*p.Width+m.X+c.Dx))throw new ArgumentException("A model footprint overlaps a shared world building.");
  foreach(var d in p.Decorations)foreach(var c in d.Parts)if(shared.Contains((d.Y+c.Dy)*p.Width+d.X+c.Dx))throw new ArgumentException("An object overlaps a shared world building.");
  if(p.LinearFeatures.Any(f=>f.Level!=0)){var geometry=InfrastructureGeometry.Build(p);if(geometry.Tiles.Any(t=>shared.Contains(t.Y*p.Width+t.X))||geometry.Holes.Any(h=>shared.Contains(h.Y*p.Width+h.X)))throw new ArgumentException("Infrastructure overlaps a shared world building.");}
  foreach(var z in p.GameZones.Where(z=>z.Vehicle))for(var y=z.Y;y<z.Y+z.Height;y++)for(var x=z.X;x<z.X+z.Width;x++)if(rooms.Contains(y*p.Width+x))throw new ArgumentException("A vehicle zone overlaps a shared world building.");
 }
 void Prune(string id){try{var live=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var v in RecoveryArchive.Versions(FileName(id))){var m=ReadManifest(id,v.Version,CancellationToken.None).GetAwaiter().GetResult();if(m is null)return;live.Add(Blob(id,m.MetadataHash));foreach(var s in m.Sections)live.Add(Blob(id,s.Hash));}foreach(var f in Directory.EnumerateFiles(Folder(id),"*.section.json"))if(!live.Contains(f))File.Delete(f);}catch(Exception e) when(e is ArgumentException or JsonException or IOException or UnauthorizedAccessException){}}
 public void Clear(string id){RecoveryArchive.Clear(FileName(id));var folder=Path.GetFullPath(Folder(id));if(!folder.StartsWith(Path.GetFullPath(directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid section workspace location.");if(Directory.Exists(folder))Directory.Delete(folder,true);}
}
