using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
public static class SharedDecorationTests
{
 /// <summary>A three-tile counter whose middle tile sits on the last column of section 100; the rest is in section 101.</summary>
 static SharedDecoration Counter(string id="shared-counter",int x=100*256+254,int y=100*256+40)=>new(){Id=id,X=x,Y=y,Asset="fixtures_counters_01_0",Parts=[new(0,0,"fixtures_counters_01_0"),new(1,0,"fixtures_counters_01_1"),new(2,0,"fixtures_counters_01_2")]};
 public static IEnumerable<(string,Action)> Cases(){
  yield return("shared decoration crosses a seam, is stored once and reaches both native cells",()=>{
   var w=WorldTests.Fixture(2);w.Decorations.Add(Counter());WorldAssembler.Validate(w);Check(w.Version==8,"Shared decorations must raise the world format to 8");
   Check(w.Maps.All(m=>m.Project.Decorations.Count==0),"Shared decoration was copied into sections");
   var left=WorldAssembler.ProjectSection(w,w.Maps[0]);var right=WorldAssembler.ProjectSection(w,w.Maps[1]);
   var l=left.Decorations.Single();var r=right.Decorations.Single();
   Check(l.Id=="shared-counter"&&l.X==254&&l.Parts.Select(p=>p.Tile).SequenceEqual(["fixtures_counters_01_0","fixtures_counters_01_1"]),"Left section got the wrong parts");
   Check(r.X==-2&&r.Parts.Single().Dx==2&&r.Parts.Single().Tile=="fixtures_counters_01_2","Right section got the wrong part");
   ProjectValidator.Validate(left);ProjectValidator.Validate(right);
   using var zip=new ZipArchive(new MemoryStream(NativeWorldExporter.Export(w)));
   Check(Header(zip,"100_100").Contains("fixtures_counters_01_1")&&!Header(zip,"100_100").Contains("fixtures_counters_01_2"),"Cell 100 has the wrong counter tiles");
   Check(Header(zip,"101_100").Contains("fixtures_counters_01_2"),"Cell 101 lost its counter tile");
   var round=JsonSerializer.Deserialize<WorldProject>(JsonSerializer.Serialize(w,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;Check(round.Decorations.Single().Parts.Count==3&&round.Version==8,"Shared decorations did not survive JSON");
  });
  yield return("shared decorations reject overlaps water gaps spawns and reserved IDs",()=>{
   WorldProject With(Action<WorldProject> change){var w=WorldTests.Fixture(2);w.Decorations.Add(Counter());change(w);return w;}
   Reject(()=>WorldAssembler.Validate(With(w=>w.Maps[1].Project.Decorations.Add(new(){Id="bush",X=0,Y=40,Asset="vegetation_ornamental_01_0",Parts=[new(0,0,"vegetation_ornamental_01_0")]}))));
   Reject(()=>WorldAssembler.Validate(With(w=>w.Maps[1].Project.Terrain[40*256]=Terrain.Water)));
   Reject(()=>WorldAssembler.Validate(With(w=>w.Maps[0].Project.Spawns.Add(new(){X=255,Y=40}))));
   Reject(()=>WorldAssembler.Validate(With(w=>w.Decorations[0].X=101*256+254)));
   Reject(()=>WorldAssembler.Validate(With(w=>w.Decorations.Add(Counter("other",100*256+255,100*256+40)))));
   Reject(()=>WorldAssembler.Validate(With(w=>w.Decorations[0].Id="world-road-1-2")));
   Reject(()=>WorldAssembler.Validate(With(w=>w.Maps[0].Project.Decorations.Add(new(){Id="shared-counter",X=10,Y=10,Asset="vegetation_ornamental_01_0",Parts=[new(0,0,"vegetation_ornamental_01_0")]}))));
   Reject(()=>WorldAssembler.Validate(With(w=>{var b=new Building{X=100*256+250,Y=100*256+36,Width=8,Height=8};b.Design=BuildingDesigner.Generate(b,1,42);w.Buildings.Add(b);})));
  });
  yield return("section decoration promotes put and remove through the shared layer",()=>{
   var w=WorldTests.Fixture(2);var local=new WorldDecoration{Id="bench",X=255,Y=60,Asset="fixtures_counters_01_0",Parts=[new(0,0,"fixtures_counters_01_0"),new(1,0,"fixtures_counters_01_1")]};w.Maps[0].Project.Decorations.Add(local);w.Maps[0].Project.AssetSources["fixtures_counters_01_1"]="BenchMod";w.Maps[0].Project.RequiredMods=["BenchMod"];w.Maps[0].Project.Version=Math.Max(6,w.Maps[0].Project.Version);
   Reject(()=>WorldAssembler.Validate(w));// a local group cannot leave its section
   w.Maps[0].Project.Decorations[0].X=200;WorldAssembler.Validate(w);
   var promoted=WorldAssembler.PromoteDecoration(w,w.Maps[0].Id,"bench",100*256+255,100*256+60);
   var shared=promoted.Decorations.Single();Check(shared.X==100*256+255&&promoted.Maps[0].Project.Decorations.Count==0&&w.Maps[0].Project.Decorations.Count==1,"Promotion did not move the decoration or mutated the input");
   Check(shared.AssetSources.GetValueOrDefault("fixtures_counters_01_1")=="BenchMod"&&shared.RequiredMods.SequenceEqual(["BenchMod"]),"Mod provider did not travel with the decoration");
   var right=WorldAssembler.ProjectSection(promoted,promoted.Maps[1]);Check(right.AssetSources.GetValueOrDefault("fixtures_counters_01_1")=="BenchMod"&&right.RequiredMods.Contains("BenchMod"),"The section drawing a mod tile does not require its mod");
   var moved=WorldAssembler.PutDecoration(promoted,new(){Id="bench",X=100*256+100,Y=100*256+60,Asset=shared.Asset,Parts=shared.Parts});Check(moved.Decorations.Single().X==100*256+100,"Put did not replace the decoration");
   var removed=WorldAssembler.RemoveDecoration(moved,"bench");Check(removed.Decorations.Count==0&&removed.Version==8,"Remove failed or lowered the saved format");Reject(()=>WorldAssembler.RemoveDecoration(removed,"bench"));
  });
  yield return("shared decorations block seam repair and shared path surfaces",()=>{
   var w=WorldTests.Fixture(2);w.Decorations.Add(Counter());w.Maps[0].Project.Terrain[40*256+255]=Terrain.Road;var result=WorldSeams.Repair(w,"road");
   Check(result.World.Maps[1].Project.Terrain[40*256]!=Terrain.Road&&result.Issues.Any(i=>i.Contains("protected")),"Seam repair rewrote ground under a shared decoration");
  });
  yield return("streamed workspace keeps shared decorations read-only and exports them like the complete world",()=>InWorkspace(root=>{
   using var store=new WorldStore(root);var w=WorldTests.Fixture(2);w.Decorations.Add(Counter());store.SaveAsync(w.Id,w).GetAwaiter().GetResult();
   var snapshot=store.LoadSectionAsync(w.Id,w.Maps[1].Id).GetAwaiter().GetResult();Check(snapshot.Shared.Decorations.Single().Id=="shared-counter"&&snapshot.Member.Project.Decorations.Count==0,"Section context lost the shared decoration or copied it into the section");
   var p=snapshot.Member.Project;p.Decorations.Add(new(){Id="bush",X=0,Y=40,Asset="vegetation_ornamental_01_0",Parts=[new(0,0,"vegetation_ornamental_01_0")]});
   Reject(()=>store.SaveSectionAsync(w.Id,w.Maps[1].Id,snapshot.Revision,p).GetAwaiter().GetResult());
   p.Decorations[0].X=5;var saved=store.SaveSectionAsync(w.Id,w.Maps[1].Id,snapshot.Revision,p).GetAwaiter().GetResult();Check(saved.Revision==snapshot.Revision+1,"A clear local edit was not saved");
   var whole=store.LoadAsync(w.Id).GetAwaiter().GetResult()!;Check(whole.Decorations.Single().Id=="shared-counter"&&whole.Version==8,"Complete-world read lost shared decorations");
   using var jobs=new ExportJobStore(root);var done=Wait(jobs,jobs.StartWorkspace(store.PinAsync(w.Id).GetAwaiter().GetResult()).Id);Check(done.Status=="completed",done.Error??"Streamed export failed");
   using var actual=ZipFile.OpenRead(Path.Combine(root,"work","export-jobs",done.Id+".zip"));using var expected=new ZipArchive(new MemoryStream(NativeWorldExporter.Export(whole)));
   foreach(var entry in expected.Entries.Where(e=>e.Name.EndsWith(".lotheader")||e.Name.EndsWith(".lotpack"))){using var a=entry.Open();using var b=actual.Entries.Single(e=>e.Name==entry.Name).Open();Check(Read(a).SequenceEqual(Read(b)),"Streamed shared decoration differs in "+entry.Name);}
  }));
 }
 static string Header(ZipArchive zip,string cell){using var r=new StreamReader(zip.Entries.Single(e=>e.Name==$"{cell}.lotheader").Open(),Encoding.Latin1);return r.ReadToEnd();}
 static byte[] Read(Stream source){using var result=new MemoryStream();source.CopyTo(result);return result.ToArray();}
 static ExportJobStatus Wait(ExportJobStore jobs,string id){var deadline=DateTime.UtcNow.AddSeconds(60);while(DateTime.UtcNow<deadline){var state=jobs.Status(id);if(state.Status is not ("queued" or "running")){Thread.Sleep(50);return state;}Thread.Sleep(10);}throw new Exception("Streamed native job timed out");}
 static void InWorkspace(Action<string> run){var root=Path.Combine(Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??Path.GetFullPath("."),"work","shared-decoration-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{run(root);}finally{Directory.Delete(root,true);}}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}catch(AggregateException e) when(e.InnerException is ArgumentException){return;}throw new Exception("Expected validation failure");}
}
