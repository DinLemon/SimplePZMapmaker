using SimplePZMapmaker.Core;
public static class AuthoringIntegrationTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("ground model full footprint rejects water building spawn blocker and decoration overlap",()=>{
   foreach(var change in new Action<MapProject>[] {p=>p.Terrain[50*p.Width+51]=Terrain.Water,p=>p.Spawns.Add(new(){X=51,Y=50}),p=>p.Buildings.Add(new(){X=51,Y=50,Width=8,Height=8}),p=>p.StaticModels.Add(Model(51)),p=>p.Decorations.Add(new(){X=51,Y=50,Asset="carpentry_01_16",Parts=[new(0,0,"carpentry_01_16")]})}){var p=MapGenerator.Generate(new(){Kind="blank"});p.StaticModels.Add(Model(50));change(p);Reject(()=>ProjectValidator.Validate(p));}
   var valid=MapGenerator.Generate(new(){Kind="blank"});valid.StaticModels.Add(Model(50));ProjectValidator.Validate(valid);
  });
  yield return("ground native model doors reserve edges and clear approaches on both sides",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var door=Model(50);door.Profile="door";door.Footprint=[];door.Door=new(){Direction="w",KeyId=55};p.StaticModels.Add(door);ProjectValidator.Validate(p);p.StaticModels.Add(Model(49));Reject(()=>ProjectValidator.Validate(p));p.StaticModels.RemoveAt(1);var duplicate=Model(50);duplicate.Profile="door";duplicate.Footprint=[];duplicate.Door=new(){Direction="w",KeyId=56};p.StaticModels.Add(duplicate);Reject(()=>ProjectValidator.Validate(p));
  });
 }
 static StaticModelPlacement Model(int x)=>new(){ModelId="Base.Test",X=x,Y=50,Profile="obstacle",Footprint=[new(0,0),new(1,0)]};
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected complete footprint validation failure.");}
}
