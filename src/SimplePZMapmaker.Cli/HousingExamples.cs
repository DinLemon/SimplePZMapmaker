using System.Text.Json;
using System.Text.Json.Nodes;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Cli;

/// <summary>Refreshes the distributed housing examples (examples/HousingTemplates) from the built-in catalog: every
/// &lt;id&gt;.pzbuilding.json and the combined housing-templates.pzmap.json take the current plans, while ids, positions, names and the
/// rest of each file stay as they are; HousingTemplates-B42.zip is exported again from the combined map.</summary>
public static class HousingExamples
{
 public static int Refresh(string directory,Action<string> log){
  var mapPath=Path.Combine(directory,"housing-templates.pzmap.json");var map=JsonSerializer.Deserialize<MapProject>(File.ReadAllText(mapPath),NativeMapExporter.JsonOptions)??throw new ArgumentException("The housing example map is empty.");
  foreach(var descriptor in HousingTemplates.Catalog){
   var fresh=HousingTemplates.Create(descriptor.Id);var path=Path.Combine(directory,descriptor.Id+".pzbuilding.json");
   var document=JsonNode.Parse(File.ReadAllText(path))?.AsObject()??throw new ArgumentException(path+" is empty.");
   var building=JsonSerializer.Deserialize<Building>(document["building"]!.ToJsonString(),NativeMapExporter.JsonOptions)!;building.Design=fresh.Design;building.Attribution=fresh.Attribution;
   // Building template format 8 carries TileZed furniture; older editors refuse it instead of dropping the objects.
   if(AuthoringFormats.BuildingHasFurniture(building))document["version"]=Math.Max(8,(int)document["version"]!);
   document["building"]=JsonSerializer.SerializeToNode(building,NativeMapExporter.JsonOptions);WriteText(path,document.ToJsonString());
   var placed=map.Buildings.SingleOrDefault(b=>b.Name==fresh.Name)??throw new ArgumentException("The combined map has no building named "+fresh.Name);
   placed.Design=JsonSerializer.Deserialize<BuildingDesign>(JsonSerializer.Serialize(fresh.Design,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions);placed.Attribution=[..fresh.Attribution];
   log($"{descriptor.Id}: {fresh.Design!.Floors.Sum(f=>f.Objects.Count(o=>o.Kind=="furniture"))} pieces of furniture");
  }
  ProjectValidator.Validate(map);WriteText(mapPath,JsonSerializer.Serialize(map,NativeMapExporter.JsonOptions));
  var archive=Path.Combine(directory,"HousingTemplates-B42.zip");using(var file=File.Create(archive))NativeMapExporter.ExportToStream(map,file);
  log($"wrote {mapPath} (format {map.Version}) and {archive}");return 0;
 }
 // Replace the data file instead of truncating a mapped reader's file in place.
 static void WriteText(string path,string text){var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temporary,text);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
}
