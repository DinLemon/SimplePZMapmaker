using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

public static class IncrementalWorldStorageTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return Case("publishes once and keeps staging invisible without holding the store gate",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(2);
   await using var writer=await Begin(store,world.Id);
   await writer.Write(world.Maps[0]);
   Check(!File.Exists(Index(root,world.Id)),"A partial index became public");
   Check((await store.ListAsync().WaitAsync(TimeSpan.FromSeconds(5))).Length==0,"Staging leaked into the listing");
   Check(await store.ManifestAsync(world.Id) is null,"A partial manifest was readable");
   var other=WorldTests.Fixture(1);await store.SaveAsync(other.Id,other).WaitAsync(TimeSpan.FromSeconds(5));
   await writer.Write(world.Maps[1]);var result=await writer.Commit(Metadata(world));
   Check(result.File==Index(root,world.Id)&&result.Revision==1,"Wrong published identity/revision");
   Check((await store.ManifestAsync(world.Id))!.Sections.Count==2,"Commit omitted a section");
   Check(store.Versions(world.Id).Length==1,"Import published intermediate recovery revisions");
   await Reject<InvalidOperationException>(()=>writer.Commit(Metadata(world)));
  });
  yield return Case("replaces a staged identity in order and snapshots authored content",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(2);
   await using var writer=await Begin(store,world.Id);foreach(var m in world.Maps)await writer.Write(m);
   var member=world.Maps[0];member.Project.Name="Replacement";member.Project.Terrain[17]=Terrain.Sand;
   await writer.Write(member);member.Project.Name="Unsaved mutation";member.Project.Terrain[17]=Terrain.Water;
   Check(writer.References.Select(s=>s.Id).SequenceEqual(world.Maps.Select(m=>m.Id)),"Replacement reordered or duplicated sections");
   var read=await writer.Read(member.Id);Check(read.Name=="Replacement"&&read.Terrain[17]==Terrain.Sand,"Writer retained mutable source");
   read.Terrain[17]=Terrain.Dirt;await writer.Commit(Metadata(world));
   var saved=await store.LoadSectionAsync(world.Id,member.Id);Check(saved.Member.Project.Terrain[17]==Terrain.Sand,"A staged read mutated immutable storage");
   Check(Directory.GetFiles(Path.Combine(root,world.Id+".sections"),"*.section.json").Length==3,"Replaced blobs were retained");
  });
  yield return Case("rejects replacement layout changes",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(1);await using var writer=await Begin(store,world.Id);
   await writer.Write(world.Maps[0]);world.Maps[0].Project.WorldX++;
   await Reject<ArgumentException>(()=>writer.Write(world.Maps[0]));Check(!File.Exists(Index(root,world.Id)),"Invalid replacement was published");
  });
  yield return Case("rejects invalid source projects before publication",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(1);var source=Path.Combine(root,"source.osm");await File.WriteAllTextAsync(source,"preserve source");
   await using(var writer=await Begin(store,world.Id)){world.Maps[0].Project.Zombies=[];await Reject<ArgumentException>(()=>writer.Write(world.Maps[0]));}
   Check(await File.ReadAllTextAsync(source)=="preserve source","Cleanup touched the source");Check(!File.Exists(Index(root,world.Id)),"Invalid project was published");
   Check(!Directory.EnumerateFiles(root,"*.section.json",SearchOption.AllDirectories).Any(),"Failed write leaked blobs");
  });
  yield return Case("rejects overlapping section cells",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(2);world.Maps[1].Project.WorldX=100;
   await using var writer=await Begin(store,world.Id);await writer.Write(world.Maps[0]);await Reject<ArgumentException>(()=>writer.Write(world.Maps[1]));
   Check(!File.Exists(Index(root,world.Id)),"Overlapping sections were published");
  });
  yield return Case("cancellation removes only unpublished owned data",async root=>{
   using var store=new WorldStore(root);var other=WorldTests.Fixture(1);await store.SaveAsync(other.Id,other);var before=await File.ReadAllBytesAsync(Index(root,other.Id));
   var world=WorldTests.Fixture(1);using var cancel=new CancellationTokenSource();
   await using(var writer=await Begin(store,world.Id)){await writer.Write(world.Maps[0]);cancel.Cancel();await Reject<OperationCanceledException>(()=>writer.Commit(Metadata(world),cancel.Token));}
   Check(!File.Exists(Index(root,world.Id))&&!Directory.Exists(Path.Combine(root,world.Id+".sections")),"Cancelled import left published data");
   Check((await File.ReadAllBytesAsync(Index(root,other.Id))).SequenceEqual(before),"Cancellation changed another world");
   Check((await store.ListAsync()).Single().Id==other.Id,"Cancellation changed the listing");
   Check(Directory.GetDirectories(root).Length==1,"Cancelled import leaked its private directory");
  });
  yield return Case("cancelled section write and abandoned writer clean staging",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(1);
   await using(var writer=await Begin(store,world.Id)){await writer.Write(world.Maps[0]);using var cancel=new CancellationTokenSource();cancel.Cancel();await Reject<OperationCanceledException>(()=>writer.Write(world.Maps[0],cancel.Token));}
   await using(var abandoned=await Begin(store,world.Id))await abandoned.Write(world.Maps[0]);
   Check(!Directory.EnumerateFileSystemEntries(root).Any(),"Abandon/cancel leaked private data");
  });
  yield return Case("revalidates immutable fingerprints and cleans failed commit",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(1);
   await using(var writer=await Begin(store,world.Id)){
    await writer.Write(world.Maps[0]);var blob=Directory.GetFiles(root,"*.section.json",SearchOption.AllDirectories).Single();
    var bytes=await File.ReadAllBytesAsync(blob);bytes[bytes.Length/2]^=1;await File.WriteAllBytesAsync(blob,bytes);
    await Reject<ArgumentException>(()=>writer.Commit(Metadata(world)));
   }
   Check(!Directory.EnumerateFileSystemEntries(root).Any(),"Failed fingerprint validation left unpublished data");
  });
  yield return Case("requires one global spawn but permits spawnless water sections",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(2);var water=world.Maps[1].Project;water.Version=6;water.WorldSection=true;water.Spawns=[];Array.Fill(water.Terrain,Terrain.Water);
   await using(var writer=await Begin(store,world.Id)){foreach(var m in world.Maps)await writer.Write(m);await writer.Commit(Metadata(world));}
   Check((await store.ManifestAsync(world.Id))!.Sections.Select(s=>s.SpawnCount).SequenceEqual([1,0]),"Global spawn summary changed");
   var empty=WorldTests.Fixture(1);empty.Maps[0].Project.Version=6;empty.Maps[0].Project.WorldSection=true;empty.Maps[0].Project.Spawns=[];
   await using var invalid=await Begin(store,empty.Id);await invalid.Write(empty.Maps[0]);await Reject<ArgumentException>(()=>invalid.Commit(Metadata(empty)));
   Check(await store.ManifestAsync(empty.Id) is null,"World without a spawn was published");
  });
  yield return Case("rejects metadata with invalid identity or shared coverage",async root=>{
   using var store=new WorldStore(root);
   foreach(var kind in new[]{"identity","gap","path","building"}){
    var world=WorldTests.Fixture(1);var meta=Metadata(world);
    if(kind=="identity")meta.Id="invalid";
    if(kind=="gap")meta.Decorations=[new(){Id="gap",X=101*256,Y=100*256+40,Asset="fixtures_counters_01_0",Parts=[new(0,0,"fixtures_counters_01_0")]}];
    if(kind=="path")meta.LinearFeatures=[new(){Id="off-layout",Kind="road",Width=4,Points=[new(102*256,100*256+30),new(102*256+10,100*256+30)]}];
    if(kind=="building"){var b=HousingTemplates.Create("village-izba");b.X=100*256+30;b.Y=100*256+40;b.AttachedTo="invalid-shared-attachment";meta.Buildings=[b];}
    await using var writer=await Begin(store,world.Id);await writer.Write(world.Maps[0]);await Reject<ArgumentException>(()=>writer.Commit(meta));
    Check(await store.ManifestAsync(world.Id) is null,"Invalid "+kind+" metadata was published");
   }
  });
  yield return Case("rejects shared projection conflicts against real section terrain",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(2);world.Maps[1].Project.Terrain[40*256]=Terrain.Water;
   var meta=Metadata(world);meta.Decorations=[new(){Id="seam",X=100*256+255,Y=100*256+40,Asset="fixtures_counters_01_0",Parts=[new(0,0,"fixtures_counters_01_0"),new(1,0,"fixtures_counters_01_1")]}];
   await using var writer=await Begin(store,world.Id);foreach(var m in world.Maps)await writer.Write(m);
   await Reject<ArgumentException>(()=>writer.Commit(meta));Check((await store.ListAsync()).Length==0,"Shared projection failure published an index");
  });
  yield return Case("preserves full projected seam metadata and independent authored sections",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(2);
   var b=HousingTemplates.Create("village-izba");b.X=100*256+250;b.Y=100*256+40;b.SourceId="osm-seam-building";world.Buildings.Add(b);
   world.Decorations=[new(){Id="seam-counter",X=100*256+255,Y=100*256+80,Asset="fixtures_counters_01_0",Parts=[new(0,0,"fixtures_counters_01_0"),new(1,0,"fixtures_counters_01_1")]}];
   world.FenceStyles=[new(){Id="seam-fence",W="fencing_01_40",N="fencing_01_41"}];
   world.Fences=[new(){Id="seam-line",Style="seam-fence",Points=[new(100*256+250,100*256+110),new(101*256+10,100*256+110)],SourceId="osm-seam-fence"}];
   world.LinearFeatures=[new(){Id="seam-bridge",Kind="bridge",Level=2,Width=4,SupportSpacing=8,GuardRails=true,Points=[new(100*256+220,100*256+150),new(101*256+30,100*256+150)]}];
   WorldAssembler.Validate(world);
   await using(var writer=await Begin(store,world.Id)){foreach(var m in world.Maps)await writer.Write(m);await writer.Commit(Metadata(world));}
   using var lease=await store.PinAsync(world.Id);var saved=await lease.ReadMetadataAsync();
   Check(saved.Buildings.Single().SourceId=="osm-seam-building"&&saved.Fences.Single().SourceId=="osm-seam-fence","Commit dropped shared provenance");
   foreach(var member in world.Maps){
    var authored=await lease.ReadSectionAsync(member.Id);Check(authored.Buildings.Count==0&&authored.Decorations.Count==0&&authored.Fences.Count==0,"Shared data was baked into authored source");
    var actual=await lease.ReadProjectedSectionAsync(member.Id);var expected=WorldAssembler.ProjectSection(world,member);
    Check(JsonSerializer.Serialize(actual,NativeMapExporter.JsonOptions)==JsonSerializer.Serialize(expected,NativeMapExporter.JsonOptions),"Projected seam differs from full world");
    Check(actual.SharedInfrastructure!.Tiles.SequenceEqual(expected.SharedInfrastructure!.Tiles),"Neighbour terrain changed shared bridge geometry");
   }
  });
  yield return Case("collisions preserve existing index blobs legacy saves and foreign folders",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(1);await store.SaveAsync(world.Id,world);
   var before=await File.ReadAllBytesAsync(Index(root,world.Id));await RejectCollision(()=>Begin(store,world.Id));
   Check((await File.ReadAllBytesAsync(Index(root,world.Id))).SequenceEqual(before),"Collision changed the existing manifest");
   foreach(var kind in new[]{"index","folder","legacy","recovery"}){
    var id=Guid.NewGuid().ToString("N");var file=kind switch{"index"=>Index(root,id),"folder"=>Path.Combine(root,id+".sections","foreign"),"legacy"=>Path.Combine(root,id+".pzworld.json"),_=>Index(root,id)+".previous1"};
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);await File.WriteAllTextAsync(file,"foreign data");
    await RejectCollision(()=>Begin(store,id));Check(await File.ReadAllTextAsync(file)=="foreign data","Collision deleted "+kind);
   }
  });
  yield return Case("competing imports cannot overwrite or delete the winner",async root=>{
   using var store=new WorldStore(root);using var secondStore=new WorldStore(root);var world=WorldTests.Fixture(1);
   await using var first=await Begin(store,world.Id);await using var second=await Begin(secondStore,world.Id);
   await first.Write(world.Maps[0]);world.Maps[0].Project.Name="Loser";await second.Write(world.Maps[0]);
   await first.Commit(Metadata(world));var before=await File.ReadAllBytesAsync(Index(root,world.Id));await RejectCollision(()=>second.Commit(Metadata(world)));
   Check((await File.ReadAllBytesAsync(Index(root,world.Id))).SequenceEqual(before),"Loser overwrote the winning index");
   Check((await store.LoadSectionAsync(world.Id,world.Maps[0].Id)).Member.Project.Name=="Section0","Loser removed winning blobs");
  });
  yield return Case("read IO failure aborts without a visible manifest",async root=>{
   using var store=new WorldStore(root);var world=WorldTests.Fixture(1);
   await using(var writer=await Begin(store,world.Id)){
    await writer.Write(world.Maps[0]);var file=Directory.GetFiles(root,"*.section.json",SearchOption.AllDirectories).Single();
    using(var locked=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))await Reject<IOException>(()=>writer.Commit(Metadata(world)));
    Check((await store.ListAsync()).Length==0,"Read fault left a visible world");
   }
   Check(!Directory.EnumerateFileSystemEntries(root).Any(),"IO failure leaked staging after disposal");
  });
  yield return Case("retains references without retaining written terrain rasters",async root=>{
   using var store=new WorldStore(root);var id=Guid.NewGuid().ToString("N");await using var writer=await Begin(store,id);
   var raster=WriteCollectible(writer);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
   Check(!raster.IsAlive,"Import retained a previously written section raster");Check(writer.References.Count==1,"Import discarded its layout reference");
  });
 }

 static (string,Action) Case(string name,Func<string,Task> run)=>("incremental storage "+name,()=>{
  var root=Path.Combine(Path.GetTempPath(),"pz-incremental-storage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{run(root).GetAwaiter().GetResult();}finally{Directory.Delete(root,true);}
 });
 static string Index(string root,string id)=>Path.Combine(root,id+".pzworld.index.json");
 static WorldSectionArchive.Metadata Metadata(WorldProject w)=>new(){Version=w.Version,Id=w.Id,Name=w.Name,Buildings=w.Buildings,LinearFeatures=w.LinearFeatures,Decorations=w.Decorations,Fences=w.Fences,FenceStyles=w.FenceStyles,GateStyles=w.GateStyles,GeoReference=w.GeoReference,Elevation=w.Elevation};
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 static WeakReference WriteCollectible(Writer writer){var p=MapGenerator.Generate(new(){Kind="blank"});var raster=new WeakReference(p.Terrain);writer.Write(new(){Project=p}).GetAwaiter().GetResult();return raster;}
 // Reflection keeps the red phase executable before the new storage API exists.
 static async Task<Writer> Begin(WorldStore store,string id){
  var method=typeof(WorldStore).GetMethod("BeginImportAsync")??throw new Exception("BeginImportAsync incremental storage API missing");
  var task=(Task)method.Invoke(store,[id,CancellationToken.None])!;await task;
  return new(task.GetType().GetProperty("Result")!.GetValue(task)!);
 }
 sealed class Writer(dynamic inner):IAsyncDisposable {
  public IReadOnlyList<WorldSectionArchive.SectionReference> References=>inner.References;
  public async Task Write(WorldMapMember m,CancellationToken token=default)=>await inner.WriteSectionAsync(m,token);
  public async Task<MapProject> Read(string id)=>await inner.ReadSectionAsync(id,CancellationToken.None);
  public async Task<(string File,long Revision)> Commit(WorldSectionArchive.Metadata metadata,CancellationToken token=default)=>await inner.CommitAsync(metadata,token);
  public ValueTask DisposeAsync()=>((IAsyncDisposable)inner).DisposeAsync();
 }
 static async Task Reject<T>(Func<Task> action) where T:Exception {try{await action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
 static async Task RejectCollision(Func<Task> action){try{await action();}catch(Exception e) when(e is ArgumentException or IOException){return;}throw new Exception("Expected identity collision");}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
