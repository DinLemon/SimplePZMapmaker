using System.Text.Json;
using System.IO.Compression;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
public static class BuildingModuleTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("module catalog has distinct authored rooms and complete rotated stairs",()=>{
   foreach(var e in BuildingModuleCatalog.Entries){var b=BuildingModuleCatalog.Create(e.Id);if(!BuildingModules.IsModule(b))throw new Exception("Module role lost");foreach(var turns in new[]{0,1,2,3})foreach(var mirror in new[]{false,true}){var reversed=b.Design!.Floors.SelectMany(f=>f.Objects).Any(o=>o.Direction=="n"?turns is 1 or 2:mirror?turns is 0 or 1:turns is 2 or 3);if(reversed){Reject(()=>BuildingModules.Transform(b,turns,mirror));continue;}var rotated=BuildingModules.Transform(b,turns,mirror);BuildingDesigner.Validate(rotated);if(rotated.Design!.Floors.Sum(f=>f.Cells.Count(c=>c!='.'))!=b.Design!.Floors.Sum(f=>f.Cells.Count(c=>c!='.')))throw new Exception("Transform lost room tiles");}}
   if(BuildingModuleCatalog.Create("entrance-lobby").Design!.Floors[0].Cells==BuildingModuleCatalog.Create("service-annex").Design!.Floors[0].Cells)throw new Exception("Built-ins are renamed clones");
  });
  yield return ("module attachment bakes connected rooms without mutating source",()=>{
   var house=Empty(8,8);var module=BuildingModuleCatalog.Create("residential-section");var original=JsonSerializer.Serialize(house);var source=JsonSerializer.Serialize(module);var b=BuildingModules.Compose(house,module,8,0,0);BuildingDesigner.Validate(b);if(b.Width!=20||b.Height!=10||b.Design!.Floors[0].Cells.Count(c=>c!='.')!=184||!b.Design.Floors[0].Edges.Any(e=>e.X==8&&e.Edge=="w"&&e.Kind=="door"))throw new Exception("Attachment or connecting door missing");if(JsonSerializer.Serialize(house)!=original||JsonSerializer.Serialize(module)!=source)throw new Exception("Inputs mutated");
  });
  yield return ("signed module assembly links basement through ground and upper floors",()=>{
   var b=BuildingModules.Compose(Empty(8,8),BuildingModuleCatalog.Create("stair-tower"),8,0,-1);BuildingDesigner.Validate(b);if(b.Design!.MinLevel!=-1||b.Design.Floors.Count!=4||!b.Design.Floors.Take(3).All(f=>f.Objects.Any(o=>o.Kind=="stairs")))throw new Exception("Signed stair stack lost");
  });
  yield return ("full signed module capture preserves all 64 floors and source ground",()=>{
   var b=BuildingModuleCatalog.Create("stair-tower");var sample=b.Design!.Floors[0];b.Design.MinLevel=-32;b.Design.Roof="none";b.Design.Floors=[];for(var z=-32;z<=31;z++){var f=JsonSerializer.Deserialize<BuildingFloor>(JsonSerializer.Serialize(sample))!;if(z==31)f.Objects.Clear();f.Edges=BuildingDesigner.AutoEdges(b,f,z);b.Design.Floors.Add(f);}BuildingDesigner.Validate(b);var module=BuildingModules.Capture(b,0,0,6,8,-32,31,"Complete vertical core","stairs");BuildingDesigner.Validate(module);if(module.Design!.MinLevel!=-32||module.Design.Floors.Count!=64||module.Design.Roof!="none")throw new Exception("Capture truncated signed levels or shifted the source ground");var baseBuilding=Empty(8,8);baseBuilding.Design!.Roof="none";var attached=BuildingModules.Compose(baseBuilding,module,8,0,0);BuildingDesigner.Validate(attached);if(attached.Design!.MinLevel!=-32||attached.Design.Floors.Count!=64)throw new Exception("Complete module assembly shifted signed levels");Reject(()=>BuildingModules.Compose(baseBuilding,module,8,0,1));
  });
  yield return ("overlap envelope disconnected floors and signed overflow are atomic rejections",()=>{
   var b=Empty(8,8);var before=JsonSerializer.Serialize(b);var annex=BuildingModuleCatalog.Create("service-annex");Reject(()=>BuildingModules.Compose(b,annex,0,0,0));Reject(()=>BuildingModules.Compose(b,annex,60,0,0));Reject(()=>BuildingModules.Compose(b,annex,8,0,0,expand:false));Reject(()=>BuildingModules.Compose(b,annex,8,0,-33));Reject(()=>BuildingModules.Compose(b,annex,8,0,32));Reject(()=>BuildingModules.Compose(b,annex,8,0,3));if(JsonSerializer.Serialize(b)!=before)throw new Exception("Failed assembly altered source");
  });
  yield return ("capture rejects cut furniture models and upper stair landings",()=>{
   var source=BuildingModuleCatalog.Create("stair-core");Reject(()=>BuildingModules.Capture(source,0,0,6,8,0,0,"Cut stair","stairs"));var b=Empty(12,10);b.Design!.Floors[0].Objects.Add(new(){Kind="bed",X=5,Y=3,Direction="w"});Reject(()=>BuildingModules.Capture(b,0,0,6,8,0,0,"Cut bed","section"));b.Design.Floors[0].Objects.Clear();b.Design.Floors[0].StaticModels.Add(new(){ModelId="Base.Chair",Profile="scenery",X=5,Y=3,Z=0,Footprint=[new(0,0),new(1,0)]});Reject(()=>BuildingModules.Capture(b,0,0,6,8,0,0,"Cut model","section"));
  });
  yield return ("capture retains crop resources models asset parts and attribution",()=>{
   var b=Empty(12,12);var f=b.Design!.Floors[0];f.FloorTile="floors_interior_tilesandwood_01_0";f.FloorTiles[3*12+3]="floors_interior_tilesandwood_01_1";f.Objects.Add(new(){Kind="asset",X=4,Y=4,Asset="furniture_seating_indoor_01_0",Parts=[new(0,0,"furniture_seating_indoor_01_0",1)]});f.StaticModels.Add(new(){ModelId="Base.Chair",Profile="scenery",X=6,Y=6,Z=0});b.RequiredMods=["ModuleMod"];b.AssetSources=new(){["furniture_seating_indoor_01_0"]="ModuleMod"};b.Attribution=["Module author"];
   var module=BuildingModules.Capture(b,2,2,8,8,0,0,"Furnished annex","annex");var floor=module.Design!.Floors[0];if(floor.FloorTiles[9]!="floors_interior_tilesandwood_01_1"||floor.Objects.Single().X!=2||floor.Objects.Single().Parts.Single().Flags!=1||floor.StaticModels.Single().X!=4||module.RequiredMods.Single()!="ModuleMod"||!module.Attribution.Contains("Module author"))throw new Exception("Capture dropped authored layers");Reject(()=>BuildingModules.Transform(module,1));var attached=BuildingModules.Compose(Empty(8,8),module,8,0,0);if(attached.Design!.Floors[0].StaticModels.Single().Id==floor.StaticModels.Single().Id||attached.RequiredMods.Single()!="ModuleMod")throw new Exception("IDs/dependencies not composed");
  });
  yield return ("model rotation updates complete footprints offsets and signed placement",()=>{
   var b=Empty(8,10);var model=new StaticModelPlacement{ModelId="Base.Chair",Profile="scenery",X=2,Y=2,Z=0,OffsetX=.2,OffsetY=.7,Rotation=30,Footprint=[new(0,0),new(1,0)]};b.Design!.Floors[0].StaticModels.Add(model);var transformed=BuildingModules.Transform(b,1);var m=transformed.Design!.Floors[0].StaticModels.Single();if(m.X!=7||m.Y!=2||!m.Footprint.ToHashSet().SetEquals([new(0,0),new(0,1)])||Math.Abs(m.OffsetX-.3)>1e-9||m.Rotation!=120)throw new Exception("Model transform lost physical geometry");Reject(()=>BuildingModules.Transform(b,0,true));
  });
  yield return ("directional edge resources require verified transform pairs",()=>{
   var b=Empty(8,10);b.Design!.Floors[0].Edges.Single(e=>e.X==3&&e.Y==0&&e.Edge=="n").WallTile="walls_exterior_house_01_49";Reject(()=>BuildingModules.Transform(b,1));var transformed=BuildingModules.Transform(b,1,false,(tile,direction,role)=>direction=="w"?"walls_exterior_house_01_48":"walls_exterior_house_01_49");if(!transformed.Design!.Floors[0].Edges.Any(e=>e.Edge=="w"&&e.WallTile=="walls_exterior_house_01_48"))throw new Exception("Catalog pair ignored");
  });
  yield return ("composed modules survive project template JSON and native export",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var module=BuildingModuleCatalog.Create("stair-tower");var b=BuildingModules.Compose(Empty(8,8),module,8,0,-1);b.X=b.Y=40;p.Buildings.Add(b);p.BuildingLibrary.Add(module);p.Version=6;ProjectValidator.Validate(p);var reload=JsonSerializer.Deserialize<MapProject>(JsonSerializer.Serialize(p,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;ProjectValidator.Validate(reload);if(!BuildingModules.IsModule(reload.BuildingLibrary.Single())||reload.Buildings.Single().Design!.MinLevel!=-1)throw new Exception("Roundtrip lost modules or levels");using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(reload)));if(!zip.Entries.Any(e=>e.FullName.EndsWith(".lotpack")))throw new Exception("Native assembly export missing");
  });
 }
 static Building Empty(int width,int height){var b=new Building{Width=width,Height=height,Design=new(){Shape="custom",Floors=[new(){Cells=new string('0',width*height),Rooms=new(){["0"]="hall"}}]}};b.Design.Floors[0].Edges=BuildingDesigner.AutoEdges(b,b.Design.Floors[0],0);return b;}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid module accepted");}
}
