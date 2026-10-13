using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.Text.Json;

public static class VisualWorkshopTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("draft sprite preview renders unfinished access without changing the building",()=>{
   var b=TwoCores();foreach(var f in b.Design!.Floors)foreach(var e in f.Edges.Where(e=>e.Kind is "door" or "open"))e.Kind="wall";
   var before=JsonSerializer.Serialize(b);var scene=NativeScene.DraftBuilding(b,0);if(scene.Sprites.Count==0||before!=JsonSerializer.Serialize(b))throw new Exception("Draft preview mutated or lost its layout.");Reject(()=>NativeScene.Building(b,0));
   var p=MapGenerator.Generate(new(){Kind="blank"});b.X=32;b.Y=32;p.Buildings.Add(b);Reject(()=>NativeMapExporter.Export(p));
  });
  yield return ("draft preview still rejects unsafe resources and structural geometry",()=>{
   var b=TwoCores();b.Design!.Floors[0].FloorTile="invalid / resource";Reject(()=>NativeScene.DraftBuilding(b,0));
   b=TwoCores();b.Design!.Floors[0].Cells=new('.',b.Width*b.Height);Reject(()=>NativeScene.DraftBuilding(b,0));
   b=TwoCores();b.Design!.Floors[0].Objects.Add(new(){X=50,Y=2,Kind="seat"});Reject(()=>NativeScene.DraftBuilding(b,0));
  });
  yield return ("independent entrance cores are accessible through their own staircases",()=>BuildingDesigner.Validate(TwoCores()));
  yield return ("an upper entrance section without a staircase is rejected",()=>{var b=TwoCores();b.Design!.Floors[0].Objects.RemoveAll(o=>o.X==9);Reject(()=>BuildingDesigner.Validate(b));});
  yield return ("an entrance section without access to outside is rejected",()=>{var b=TwoCores();b.Design!.Floors[0].Edges.Single(e=>e.X==9&&e.Y==0&&e.Edge=="n").Kind="wall";Reject(()=>BuildingDesigner.Validate(b));});
 }
 public static Building TwoCores(){
  var b=new Building{Id="two-cores",Name="Два подъезда",Kind="apartments",Width=12,Height=6,Design=new(){Floors=[]}};
  for(var z=0;z<2;z++){
   var f=new BuildingFloor{Rooms=new(){["1"]="hall",["2"]="hall"},Cells=string.Concat(Enumerable.Range(0,6).Select(_=>"11111..22222"))};
   b.Design.Floors.Add(f);f.Edges=BuildingDesigner.AutoEdges(b,f,z);
   if(z==0)foreach(var x in new[]{2,9}){var edge=f.Edges.Single(e=>e.X==x&&e.Y==0&&e.Edge=="n");edge.Kind="door";f.Objects.Add(new(){X=x,Y=0,Kind="stairs",Direction="n"});}
  }return b;
 }
 private static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Unreachable section accepted.");}
}
