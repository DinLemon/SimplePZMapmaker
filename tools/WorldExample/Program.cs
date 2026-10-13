using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;
var root=Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??Directory.GetCurrentDirectory();
var assets=new GameAssetCatalog(Path.Combine(root,"work","world-example-catalog"),Environment.GetEnvironmentVariable("PZMAP_GAME_PATH")??"");if(!assets.Ready)throw new ArgumentException("Set PZMAP_GAME_PATH to your Build 42 installation before generating resource-based world examples.");var models=new StaticModelCatalog(assets);
var world=new WorldProject{Id="0f000006000000000000000000000001",Name="Stitched world example"};
for(var i=0;i<HousingTemplates.Catalog.Count;i++){
 var p=MapGenerator.Generate(new(){Kind="blank",Name="Section "+(i+1),Seed=42+i,SurfaceTheme="rural"});p.WorldSection=true;p.Version=6;p.WorldX=100+i;p.WorldY=100;
 for(var y=120;y<127;y++)for(var x=0;x<256;x++)p.Terrain[y*256+x]=Terrain.Road;
 for(var y=5;y<35;y++)for(var x=10;x<240;x++)p.Terrain[y*256+x]=Terrain.Forest;
 var b=HousingTemplates.Create(HousingTemplates.Catalog[i].Id);b.Id="world-building-"+i;b.X=40;b.Y=50;p.Buildings.Add(b);
 if(i==0){var bottle=models.Placement("Base.BeerBottle");bottle.Id="0f000006000000000000000000000002";bottle.X=6;bottle.Y=6;bottle.Z=b.Design!.MinLevel;bottle.Scale=.5;b.Design.Floors[0].StaticModels.Add(bottle);}
 if(i==1){var building=HousingTemplates.Create("village-izba");building.Id="cross-cell-house";building.X=250;building.Y=180;var next=MapGenerator.Generate(new(){Kind="blank",Size=512,Name="House crossing a cell boundary"});next.Version=6;next.WorldSection=true;next.WorldX=100;next.WorldY=102;next.Buildings.Add(building);next.Spawns=[new(){X=20,Y=20}];AssetRules.Project(next,assets);world.Maps.Add(new(){Id="0f000006000000000000000000000099",Project=next});}
 var chair=assets.PlacementFor("furniture_seating_indoor_01_0");p.Decorations.Add(new(){Id="world-chair-"+i,X=200,Y=150,Asset=chair.Id,Parts=chair.Parts.Select(part=>new BuildingObjects.Part(part.Dx,part.Dy,part.Tile,part.Flags)).ToList()});
 p.AssetSources=NativeScene.ResourceIds(p).ToDictionary(id=>id,_=>"game");AssetRules.Project(p,assets);models.Validate(p);world.Maps.Add(new(){Id=(i+1).ToString("x32"),Project=p});
}
var bounds=WorldAssembler.Validate(world);var output=Path.Combine(root,"examples","Worlds");Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"world.pzworld.json"),JsonSerializer.SerializeToUtf8Bytes(world,NativeMapExporter.JsonOptions));File.WriteAllBytes(Path.Combine(output,"Worlds-B42.zip"),NativeWorldExporter.Export(world));Console.WriteLine($"World example: {world.Maps.Count} sections, {bounds.Width}x{bounds.Height}, {bounds.Cells} native cells.");
