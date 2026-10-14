using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
public static class ZFormatTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("extended signed stacks promote map and world formats and survive editable serialization",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40,Width=12,Height=10};b.Design=BuildingDesigner.Generate(b,32,42,"rect",32);p.Buildings.Add(b);ProjectValidator.Validate(p);
   var world=new WorldProject{Maps=[new(){Project=p}]};WorldAssembler.Validate(world);if(p.Version!=10||world.Version!=4)throw new Exception("Extended authoring was not versioned");
   var copy=JsonSerializer.Deserialize<WorldProject>(JsonSerializer.Serialize(world,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;WorldAssembler.Validate(copy);var saved=copy.Maps[0].Project.Buildings[0].Design!;
   if(copy.Version!=4||copy.Maps[0].Project.Version!=10||saved.MinLevel!=-32||saved.Floors.Count!=64||saved.Roof!="none")throw new Exception("Signed stack metadata was lost");
  });
  yield return("explicit open roofs and deep library templates promote formats while legacy maps stay compatible",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});if(p.Version!=4)throw new Exception("Legacy blank format changed");var b=new Building{Width=12,Height=10};b.Design=BuildingDesigner.Generate(b,1,42);b.Design.Roof="none";p.BuildingLibrary.Add(b);ProjectValidator.Validate(p);if(p.Version!=10)throw new Exception("Open roof template was not versioned");
   var shared=new WorldProject{Maps=[new(){Project=MapGenerator.Generate(new(){Kind="blank"})}],Buildings=[b]};b.X=shared.Maps[0].Project.WorldX*256+40;b.Y=shared.Maps[0].Project.WorldY*256+40;WorldAssembler.Validate(shared);if(shared.Version!=4)throw new Exception("Shared open roof format was not versioned");
  });
 }
}
