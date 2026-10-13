using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;
static class PlanningSessionTests {
 public static IEnumerable<(string,Action)> Cases(){
  yield return("world agent revisions isolate returned references reject stale and rollback all invalid sections",()=>{
   var root=Path.Combine(Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")!,"work","tests","world-agent-"+Guid.NewGuid().ToString("N"));
   try{var store=new AgentWorldStore(root);var world=new WorldProject{Maps=[new(){Project=MapGenerator.Generate(new(){Kind="blank"})}]};var s=store.Create(world);s.World.Name="Leaked reference";Check(store.Read(s.Token).World.Name!=s.World.Name,"Reference leaked");
    var updated=store.Mutate(s.Token,1,w=>w.Name="Manual world","browser");try{store.Mutate(s.Token,1,w=>w.Name="Stale world");throw new Exception("Stale accepted");}catch(AgentRevisionConflict){}
    try{store.Mutate(s.Token,2,w=>{w.Name="Invalid world";w.Maps.Add(new(){Project=NeighborhoodPlanner.Copy(w.Maps[0].Project)});});throw new Exception("Overlap accepted");}catch(ArgumentException){}
    Check(store.Read(s.Token).World.Name=="Manual world"&&store.Read(s.Token).Revision==2,"Atomic rollback lost");store.Close(s.Token.ToUpperInvariant());try{store.Read(s.Token);throw new Exception("Closed token survived");}catch(ArgumentException){}
   }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
  });
  yield return("dependency preview reports full missing chain and cycles with actionable repair",()=>{
   ModDescriptor Mod(string id,string[] requires)=>new(id,id,"G:\\unused","G:\\unused\\mod.info","",requires,[],[],[],null);
   var mods=new Dictionary<string,ModDescriptor>{{"city",Mod("city",["furniture"])},{"furniture",Mod("furniture",["tiles"])}};
   var missing=ModDependencies.Check(mods,["city"]);Check(!missing.Valid&&missing.Issues[0].Chain.SequenceEqual(new[]{"city","furniture","tiles"})&&missing.Issues[0].Action.Contains("Workshop"),"Missing dependency is vague");mods["tiles"]=Mod("tiles",["city"]);Check(ModDependencies.Check(mods,["city"]).Issues.Any(i=>i.Code=="dependency-cycle"),"Cycle missed");mods["tiles"]=Mod("tiles",[]);var ready=ModDependencies.Check(mods,["city"]);Check(ready.Valid&&ready.ActivationOrder.SequenceEqual(new[]{"tiles","furniture","city"}),"Dependency order wrong");
  });
 }
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
