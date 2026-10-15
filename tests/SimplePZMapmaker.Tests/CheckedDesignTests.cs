using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;
public static class CheckedDesignTests {
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("access-only tasks accept a small crop and projected ground blockers prevent unsafe repairs",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});new DesignConstraints{Width=8,Height=8,RequireLighting=false}.Validate(p);var b=new Building{X=8,Y=8};b.Design=BuildingDesigner.Generate(b,1,42,"rect");b.Design.Floors[0].Objects.Clear();foreach(var edge in b.Design.Floors[0].Edges.Where(e=>e.Kind=="door"))edge.Kind="wall";p.Buildings.Add(b);var crop=new WalkabilityOptions{Width=40,Height=40};Check(AccessRepair.Step(p,crop) is not null,"Control dry ground repair failed");MapProject Project(MapProject candidate){var copy=NeighborhoodPlanner.Copy(candidate);for(var y=b.Y-1;y<=b.Y+b.Height;y++)for(var x=b.X-1;x<=b.X+b.Width;x++)if(x<b.X||y<b.Y||x>=b.X+b.Width||y>=b.Y+b.Height)copy.Terrain[y*copy.Width+x]=Terrain.Water;return copy;}Check(AccessRepair.Step(p,crop,project:Project) is null,"Repair ignored projected world river around the building");
  });
  yield return ("checked design repairs doors on an isolated candidate and respects edited facades",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=8,Y=8};b.Design=BuildingDesigner.Generate(b,1,42,"rect");foreach(var f in b.Design.Floors){f.Objects.Clear();foreach(var e in f.Edges.Where(e=>e.Kind=="door"))e.Kind="wall";}p.Buildings.Add(b);var before=System.Text.Json.JsonSerializer.Serialize(p);var crop=new WalkabilityOptions{Width=40,Height=40};var step=AccessRepair.Step(p,crop);Check(step is not null,"No permitted door repair found");Check(before==System.Text.Json.JsonSerializer.Serialize(p),"Repair changed source");Check(WalkabilityAnalyzer.Analyze(step!.Project,crop).Reachable>WalkabilityAnalyzer.Analyze(p,crop).Reachable,"Repair did not improve access");
   foreach(var e in b.Design.Floors[0].Edges)e.Edited=true;Check(AccessRepair.Step(p,crop,allowStairs:false) is null,"Edited facade changed without permission");
  });
  yield return ("design proposal store uses task revision compare-and-swap and closes its files",()=>{
   var directory=Path.Combine(Environment.CurrentDirectory,"work","checked-design-tests-"+Guid.NewGuid().ToString("N"));var store=new AgentDesignStore(directory);var token=new string('a',64);try{var task=new AgentDesignTask{BaseRevision=1,Candidate=MapGenerator.Generate(new(){Kind="blank"}),Constraints=new(){Width=32,Height=32,RequireLighting=false}};store.Save(token,task,null);var a=store.Read(token,task.Id);a.Objective="Updated";store.Save(token,a,1);try{store.Save(token,task,1);throw new Exception("Stale task overwrite accepted");}catch(AgentRevisionConflict){}Check(store.Read(token,task.Id).TaskRevision==2,"Task revision changed incorrectly");store.Close(token);Check(!Directory.EnumerateFiles(directory,"*.design.json",SearchOption.AllDirectories).Any(),"Closed task persisted");}finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
