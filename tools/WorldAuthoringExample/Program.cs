using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

var root=Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??Directory.GetCurrentDirectory();
var output=Path.Combine(root,"examples","WorldAuthoring");Directory.CreateDirectory(output);
const int originX=100*256,originY=100*256;
var world=new WorldProject{Id="0f000011000000000000000000000001",Name="World authoring showcase"};
for(var section=0;section<2;section++){
 var side=section==0?"West":"East";var p=MapGenerator.Generate(new(){Kind="blank",Size=256,Seed=1100+section,Name=side+" riverside section"});p.WorldX=100+section;p.WorldY=100;p.WorldSection=true;p.Version=11;p.Spawns=[new(){X=80,Y=80}];p.Attribution=["Synthetic authored example created by SimplePZMapmaker. No external geographic data."];
 var generated=ParametricBuildings.Create(new(){Name=side+" three-floor apartments",Entrances=1,Floors=3,ApartmentsPerFloor=2,Layout="slab",Facade="brick",Seed=1100+section});var house=generated.Building;house.Id="world-authoring-"+side.ToLowerInvariant()+"-apartments";house.X=40;house.Y=40;p.Buildings.Add(house);p.BuildingLibrary.AddRange(generated.Modules);
 foreach(var building in p.Buildings.Concat(p.BuildingLibrary))building.AssetSources=NativeScene.BuildingResourceIds(building).Order(StringComparer.Ordinal).ToDictionary(id=>id,_=>"game");
 world.Maps.Add(new(){Id=section==0?"0f000011000000000000000000000002":"0f000011000000000000000000000003",Project=p});
}
world=WorldInfrastructure.Put(world,new(){Id="world-authoring-river",Kind="river",Width=18,Points=[new(originX+256,originY+40),new(originX+256,originY+210)],SourceTags=new(){["name"]="Seam river"}});
world=WorldInfrastructure.Put(world,new(){Id="world-authoring-underpass",Kind="road",Width=8,Points=[new(originX+228,originY+60),new(originX+228,originY+150)],SourceTags=new(){["name"]="Ground road beneath the bridge"}});
world=WorldInfrastructure.Put(world,new(){Id="world-authoring-bridge",Kind="bridge",Width=6,Level=2,Access="stairs",GuardRails=true,SupportSpacing=12,Points=[new(originX+220,originY+100),new(originX+290,originY+100)],SourceTags=new(){["name"]="Shared river bridge"}});
// The main feature owns one automatic stair run. An overlapping approach adds dry access on the opposite bank.
world=WorldInfrastructure.Put(world,new(){Id="world-authoring-east-access",Kind="bridge",Width=6,Level=2,Access="stairs",GuardRails=true,Points=[new(originX+290,originY+100),new(originX+280,originY+100)],SourceTags=new(){["name"]="East bank stair access"}});
foreach(var member in world.Maps){var p=member.Project;p.AssetSources=NativeScene.ResourceIds(WorldAssembler.ProjectSection(world,member)).Concat(p.BuildingLibrary.SelectMany(NativeScene.BuildingResourceIds)).Distinct().Order(StringComparer.Ordinal).ToDictionary(id=>id,_=>"game");}
var bounds=WorldAssembler.Validate(world);var geometry=WorldInfrastructure.Build(world);
var json=JsonSerializer.SerializeToUtf8Bytes(world,NativeMapExporter.JsonOptions);File.WriteAllBytes(Path.Combine(output,"world-authoring.pzworld.json"),json);
var archive=Path.Combine(output,"WorldAuthoring-B42.zip");using(var stream=File.Create(archive))NativeWorldExporter.ExportToStream(world,stream);
// Native content and JSON are deterministic. Remove ZIP creation time from the artifact as well.
using(var zip=ZipFile.Open(archive,ZipArchiveMode.Update))foreach(var entry in zip.Entries)entry.LastWriteTime=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero);
Console.WriteLine(JsonSerializer.Serialize(new{sections=world.Maps.Count,cells=bounds.Cells,width=bounds.Width,height=bounds.Height,sharedPaths=world.LinearFeatures.Count,buildings=world.Maps.Sum(m=>m.Project.Buildings.Count),apartments=12,nativeInfrastructureTiles=geometry.Tiles.Count,stairOpenings=geometry.Holes.Count,sourceSha256=Convert.ToHexStringLower(SHA256.HashData(json)),archiveSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive)))},NativeMapExporter.JsonOptions));
