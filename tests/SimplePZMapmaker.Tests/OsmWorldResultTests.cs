using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

public static class OsmWorldResultTests
{
 public static IEnumerable<(string Name,Action Run)> Cases()
 {
  yield return ("OSM world result can publish a small wizard import directly by section",()=>Run(async (directory,store,world,id)=>{
   var result=await OsmWorldResultWriter.SaveAsync(directory,id,world,store,CancellationToken.None,preferSections:true);
   Check(result.WorkspaceId==id&&result.WorkspaceRevision==1,"Explicit section preference or published revision was ignored");
   var manifest=await store.ManifestAsync(id)??throw new Exception("Section import was not published");
   Check(manifest.Sections.Count==2&&!File.Exists(Path.Combine(directory,id+".pzworld.json")),"Direct section import wrote an unnecessary complete-world copy");
   foreach(var section in manifest.Sections){var snapshot=await store.LoadSectionAsync(id,section.Id);Check(snapshot.Shared.GeoReference?.MetresPerTile==1,"Section lost the common frame");}
   using(var lease=await store.PinAsync(id,expectedRevision:result.WorkspaceRevision))Check(lease.Manifest.Revision==1,"Wrong import revision pinned");
   await store.SaveWorkspaceAsync(id,world,manifest.Revision);
   try{using var stale=await store.PinAsync(id,expectedRevision:result.WorkspaceRevision);throw new Exception("Changed workspace silently compiled instead of the reviewed import");}catch(AgentRevisionConflict){}
  }));
  yield return ("OSM world result keeps small portable worlds",()=>Run(async (directory,store,world,id)=>{
   var result=await OsmWorldResultWriter.SaveAsync(directory,id,world,store,CancellationToken.None);
   Check(result.WorkspaceId is null&&result.File.EndsWith(".pzworld.json"),"Small result changed format");
   using var stream=File.OpenRead(result.File);var loaded=await JsonSerializer.DeserializeAsync<WorldProject>(stream,NativeMapExporter.JsonOptions);
   Check(loaded?.Maps.Count==2&&loaded.GeoReference?.MetresPerTile==1,"Portable result lost layout");
   Check((await store.ListAsync()).Length==0,"Small result unnecessarily published a workspace");
  }));
  yield return ("OSM world result falls back to linked sections after aggregate byte cap",()=>Run(async (directory,store,world,id)=>{
   var result=await OsmWorldResultWriter.SaveAsync(directory,id,world,store,CancellationToken.None,4096);
   Check(result.WorkspaceId==id&&result.File.EndsWith(".pzworld.index.json"),"Large result did not become a section workspace");
   var manifest=await store.ManifestAsync(id)??throw new Exception("Workspace was not published");
   Check(manifest.Sections.Count==2&&manifest.Sections.Sum(s=>s.Bytes)>4096,"Test did not exceed aggregate budget");
   using var index=JsonDocument.Parse(await File.ReadAllTextAsync(result.File));Check(index.RootElement.GetProperty("format").GetString()=="SimplePZWorldSections","Job result is not an index");
   foreach(var section in manifest.Sections){var snapshot=await store.LoadSectionAsync(id,section.Id);Check(snapshot.Member.Project.WorldX==section.WorldX&&snapshot.Shared.GeoReference?.MetresPerTile==1,"Section lost its common coordinate frame");}
   Check(manifest.Sections[1].WorldX==manifest.Sections[0].WorldX+1,"Linked maps no longer meet at their seam");
   Check(!File.Exists(Path.Combine(directory,id+".pzworld.json"))&&!Directory.EnumerateFiles(directory,"*.tmp").Any(),"Partial complete-world result survived fallback");
  }));
  yield return ("OSM world result cancellation and invalid world publish no index",()=>Run(async (directory,store,world,id)=>{
   using var cancel=new CancellationTokenSource();cancel.Cancel();try{await OsmWorldResultWriter.SaveAsync(directory,id,world,store,cancel.Token,4096);throw new Exception("Canceled result accepted");}catch(OperationCanceledException){}
   world.Maps[1].Project.WorldX=world.Maps[0].Project.WorldX;
   try{await OsmWorldResultWriter.SaveAsync(directory,id,world,store,CancellationToken.None,4096);throw new Exception("Overlapping maps accepted");}catch(ArgumentException){}
   Check((await store.ListAsync()).Length==0&&!Directory.EnumerateFiles(directory,"*.tmp").Any(),"Rejected result published partial data");
  }));
  yield return ("OSM world result cancellation during section writes removes owned workspace",()=>Run(async (directory,store,world,id)=>{
   foreach(var member in world.Maps){var x=member.Project.WorldX;member.Project=MapGenerator.Generate(new(){Kind="blank",Size=2048});member.Project.WorldX=x==100?100:108;member.Project.WorldY=100;member.Project.WorldSection=true;member.Project.Version=6;}
   using var cancel=new CancellationTokenSource();var saving=Task.Run(()=>OsmWorldResultWriter.SaveAsync(directory,id,world,store,cancel.Token,4096));
   var blobFolder=Path.Combine(directory,"workspaces",id+".sections");var deadline=DateTime.UtcNow.AddSeconds(20);
   while(!saving.IsCompleted&&!Directory.Exists(blobFolder)&&DateTime.UtcNow<deadline)await Task.Delay(1);
   Check(Directory.Exists(blobFolder),"Did not reach section-write cancellation point");cancel.Cancel();
   try{await saving;throw new Exception("Mid-save cancellation was ignored");}catch(OperationCanceledException){}
   Check((await store.ListAsync()).Length==0&&!Directory.Exists(blobFolder),"Canceled import left a visible or partial workspace");
  }));
  yield return ("OSM world result existing workspace and disk failures are not replaced",()=>Run(async (directory,store,world,id)=>{
   var before=await store.SaveWorkspaceAsync(id,world,null);
   try{await OsmWorldResultWriter.SaveAsync(directory,id,world,store,CancellationToken.None,4096);throw new Exception("Existing workspace overwritten");}catch(AgentRevisionConflict){}
   var after=await store.ManifestAsync(id);Check(after?.Revision==before.Revision&&after.MetadataHash==before.MetadataHash,"Conflicting import removed an existing workspace");
   var blocked=Path.Combine(directory,"blocked-store");await File.WriteAllTextAsync(blocked,"retain");using var unavailable=new WorldStore(blocked);
   try{await OsmWorldResultWriter.SaveAsync(directory,Guid.NewGuid().ToString("N"),world,unavailable,CancellationToken.None,4096);throw new Exception("Disk failure disguised as successful fallback");}catch(IOException){}
   Check(await File.ReadAllTextAsync(blocked)=="retain"&&!Directory.EnumerateFiles(directory,"*.tmp").Any(),"Disk failure changed unrelated data or left a partial result");
  }));
  yield return ("OSM world result disk failure after metadata removes unpublished blobs",()=>Run(async (directory,store,world,id)=>{
   var folder=Path.Combine(directory,"workspaces",id+".sections");
   var hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(world.Maps[0].Project,NativeMapExporter.JsonOptions)));
   // A directory at the first section's final blob path permits metadata to be
   // written, then forces an actual IO failure while publishing that section.
   var blocker=Path.Combine(folder,hash+".section.json");Directory.CreateDirectory(blocker);
   try{await OsmWorldResultWriter.SaveAsync(directory,id,world,store,CancellationToken.None,4096);throw new Exception("Mid-save IO failure ignored");}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
   Check((await store.ListAsync()).Length==0&&!Directory.EnumerateFiles(folder).Any(),"Failed save published an index or retained written metadata/partial blobs");
   Check(Directory.Exists(blocker),"Failure cleanup removed a preexisting directory");
  }));
  yield return ("OSM world result retains large shared multistorey building metadata",()=>Run(async (directory,store,world,id)=>{
   foreach(var member in world.Maps){var x=member.Project.WorldX;member.Project=MapGenerator.Generate(new(){Kind="blank",Size=1024});member.Project.WorldX=x==100?100:104;member.Project.WorldY=100;member.Project.WorldSection=true;member.Project.Version=6;}
   var prototype=new Building{Width=64,Height=64,Kind="apartments"};var design=BuildingDesigner.Generate(prototype,32,42,"rect",32);
   for(var i=0;i<20;i++)world.Buildings.Add(new(){X=100*256+200+(i/10)*800,Y=100*256+20+(i%10)*80,Width=64,Height=64,Kind="apartments",Design=design});
   var metadataBytes=JsonSerializer.SerializeToUtf8Bytes(new WorldSectionArchive.Metadata{Version=world.Version,Id=world.Id,Name=world.Name,Buildings=world.Buildings,GeoReference=world.GeoReference},NativeMapExporter.JsonOptions).Length;
   Check(metadataBytes>16*1024*1024,"Fixture did not reproduce the old shared-metadata cap");
   var result=await OsmWorldResultWriter.SaveAsync(directory,id,world,store,CancellationToken.None,4096);var manifest=await store.ManifestAsync(id)??throw new Exception("Large shared metadata was not published");
   Check(result.WorkspaceId==id&&manifest.MetadataBytes==metadataBytes,"Shared building metadata was truncated or discarded");
   using var lease=await store.PinAsync(id);var restored=await lease.ReadMetadataAsync();Check(restored.Buildings.Count==20&&restored.Buildings.All(b=>b.Design?.Floors.Count==64),"Large shared metadata failed its independently validated read");
  }));
 }
 static void Run(Func<string,WorldStore,WorldProject,string,Task> action){
  var directory=Path.Combine(Directory.GetCurrentDirectory(),"work","osm-result-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  using var store=new WorldStore(Path.Combine(directory,"workspaces"));
  var world=new WorldProject{GeoReference=new(){Bounds=new(){West=30,East=30.005,South=50,North=50.002},MetresPerTile=1,OriginCellX=100,OriginCellY=100}};
  for(var x=0;x<2;x++){var p=MapGenerator.Generate(new(){Kind="blank",Size=256});p.WorldX=100+x;p.WorldY=100;p.WorldSection=true;p.Version=6;world.Maps.Add(new(){Project=p});}
  action(directory,store,world,Guid.NewGuid().ToString("N")).GetAwaiter().GetResult();
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
