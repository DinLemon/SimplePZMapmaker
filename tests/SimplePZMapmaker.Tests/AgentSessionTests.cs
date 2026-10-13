using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;
static class AgentSessionTests {
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("agent session revisions reject stale and preserve manual changes",()=>WithStore((store,_)=>{
   var created=store.Create(MapGenerator.Generate(new(){Kind="blank"}));
   Check(store.Status(created.Token.ToUpperInvariant()).Revision==1,"Initial aliased status wrong");
   var updated=store.Mutate(created.Token,created.Revision,p=>p.Name="Manually edited name","browser");
   try{store.Mutate(created.Token,created.Revision,p=>p.Name="Stale agent","agent");throw new Exception("Stale mutation accepted");}catch(AgentRevisionConflict){}
   Check(store.Read(created.Token).Project.Name=="Manually edited name"&&updated.Revision==2,"Manual state lost");
   Check(store.Status(created.Token.ToUpperInvariant()).Revision==2,"Aliased token cached stale revision");store.Close(created.Token.ToUpperInvariant());try{store.Status(created.Token);throw new Exception("Closed alias stayed active");}catch(ArgumentException){}
  }));
  yield return ("agent invalid mutation and returned clone cannot alter stored project",()=>WithStore((store,_)=>{
   var created=store.Create(MapGenerator.Generate(new(){Kind="blank"}));
   try{store.Mutate(created.Token,1,p=>p.Terrain=[],"agent");throw new Exception("Invalid project accepted");}catch(ArgumentException){}
   try{store.Publish(created.Token,1,new MapProject());throw new Exception("Invalid publish accepted");}catch(ArgumentException){}
   created.Project.Name="Changed through leaked reference";
   Check(store.Read(created.Token).Revision==1&&store.Read(created.Token).Project.Terrain.Length==65536&&store.Read(created.Token).Project.Name!=created.Project.Name,"Mutation leaked");
  }));
  yield return ("agent session persists revision and isolated unfinished building draft",()=>WithStore((store,path)=>{
   var created=store.Create(MapGenerator.Generate(new(){Kind="blank"}));var building=HousingTemplates.Create("village-izba");
   var draft=store.SaveDraft(created.Token,building);draft.Building.Name="Reference leak";
   var loaded=new AgentProjectStore(path).Read(created.Token);var reopened=new AgentProjectStore(path).ReadDraft(created.Token,draft.Id);
   Check(loaded.Project.Buildings.Count==0&&reopened.Building.Name!="Reference leak","Draft escaped isolation");
  }));
  yield return ("agent concurrent revisions allow exactly one writer",()=>WithStore((store,_)=>{
   var created=store.Create(MapGenerator.Generate(new(){Kind="blank"}));var successes=0;
   Parallel.For(0,2,i=>{try{store.Mutate(created.Token,1,p=>p.Name="Writer "+i,"agent");Interlocked.Increment(ref successes);}catch(AgentRevisionConflict){}});
   Check(successes==1&&store.Read(created.Token).Revision==2,"Concurrent write lost");
  }));
 }
 static void WithStore(Action<AgentProjectStore,string> action){var root=Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??throw new Exception("Workspace missing");var path=Path.Combine(root,"work","tests","agent-"+Guid.NewGuid().ToString("N"));try{action(new AgentProjectStore(path),path);}finally{if(Directory.Exists(path))Directory.Delete(path,true);}}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
