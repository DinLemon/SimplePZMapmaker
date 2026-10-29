using SimplePZMapmaker.Core;
using System.Text.Json;
public static class ParcelGenerationTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("parcel complete quarter validates installed native resources and produces reviewable export",()=>{
   var p=Quarter();var game=Environment.GetEnvironmentVariable("PZMAP_GAME_PATH");var root=Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??Directory.GetCurrentDirectory();ParcelRegionalResources? resources=null;
   SimplePZMapmaker.Web.Services.GameAssetCatalog? assets=null;
   if(!string.IsNullOrEmpty(game)){assets=new(root,game);var catalog=assets.RegionalSprites();resources=new(TransferRegionalProfiles.SelectRuralAppearance(catalog),TransferRegionalProfiles.SelectRuralPalette(catalog));}
   var result=ParcelGenerator.Preview(p,new(){X=0,Y=0,Width=256,Height=256},42,resources:resources);
   Check(result.Project.Sites.All(s=>s.ParcelGeneration is not null),"Some quarter parcels were not completed");
   Check(result.Project.Buildings.Count>p.Buildings.Count&&result.Paths.Count>=8,"Quarter is missing outbuildings or street routes");
   if(assets is not null)SimplePZMapmaker.Web.Services.AssetRules.Project(result.Project,assets);
   var directory=Path.Combine(root,"work","transfer-acceptance");Directory.CreateDirectory(directory);
   File.WriteAllText(Path.Combine(directory,"quarter-before.pzmap.json"),JsonSerializer.Serialize(p,SimplePZMapmaker.Core.Export.NativeMapExporter.JsonOptions));
   File.WriteAllText(Path.Combine(directory,"quarter-after.pzmap.json"),JsonSerializer.Serialize(result.Project,SimplePZMapmaker.Core.Export.NativeMapExporter.JsonOptions));
   File.WriteAllBytes(Path.Combine(directory,"quarter.zip"),SimplePZMapmaker.Core.Export.NativeMapExporter.Export(result.Project));
   File.WriteAllText(Path.Combine(directory,"quarter-review.json"),JsonSerializer.Serialize(new{warnings=result.Warnings,paths=result.Paths.Select(path=>path.Select(p=>new{p.X,p.Y}))},SimplePZMapmaker.Core.Export.NativeMapExporter.JsonOptions));
  });
  yield return("surveyed parcel generation is pure deterministic bounded and idempotent",()=>{
   var p=Fixture();var before=JsonSerializer.Serialize(p);var a=ParcelGenerator.Preview(p,new(){X=0,Y=0,Width=256,Height=256},42);var b=ParcelGenerator.Preview(p,new(){X=0,Y=0,Width=256,Height=256},42);
   Check(JsonSerializer.Serialize(p)==before,"Preview mutated the source");Check(JsonSerializer.Serialize(a.Project)==JsonSerializer.Serialize(b.Project),"Same source changed its generated plan");
   Check(a.Project.Buildings.Count>1&&a.Project.Fences.Count>0,"Parcel lacks outbuildings or fences");
   foreach(var building in a.Project.Buildings)Check(SiteModel.FullyContainsBuilding(a.Project.Sites[0],building),"Building leaves surveyed boundary");
   var second=ParcelGenerator.Preview(a.Project,new(){X=0,Y=0,Width=256,Height=256},42);Check(JsonSerializer.Serialize(second.Project)==JsonSerializer.Serialize(a.Project),"Repeated completion duplicated or changed generated objects");
   Check(a.Paths.Count>0&&a.Paths.All(path=>path.Count>0),"No street access preserved");ProjectValidator.Validate(a.Project);
  });
  yield return("parcel regeneration protects edited children and manual terrain",()=>{
   var p=ParcelGenerator.Preview(Fixture(),new(){X=0,Y=0,Width=256,Height=256},42).Project;var child=p.Buildings.First(b=>b.SourceKind=="parcel-generator");child.Name="My edited shed";child.Modified=true;var id=child.Id;
   var i=120*p.Width+70;p.Terrain[i]=Terrain.Sand;TerrainEditMask.Mark(p,i);
   var result=ParcelGenerator.Preview(p,new(){X=0,Y=0,Width=256,Height=256},99,regenerate:true).Project;
   Check(result.Buildings.Any(b=>b.Id==id&&b.Name=="My edited shed"),"Regeneration lost manual child changes");Check(result.Terrain[i]==Terrain.Sand,"Regeneration lost manual ground");ProjectValidator.Validate(result);
  });
  yield return("parcel completion skips locked crossing and roadless sites explicitly",()=>{
   var p=Fixture();p.Sites[0].Locked=true;var locked=ParcelGenerator.Preview(p,new(){X=0,Y=0,Width=256,Height=256},42);Check(locked.Project.Buildings.Count==1&&locked.Warnings.Count>0,"Locked site was filled");
   p.Sites[0].Locked=false;p.Sites[0].Boundary[0][1]=new(300,20);var crossing=ParcelGenerator.Preview(p,new(){X=0,Y=0,Width=256,Height=256},42);Check(crossing.Project.Buildings.Count==1&&crossing.Warnings.Any(w=>w.Contains("section")),"Cross-section ownership was silently clipped");
   p=Fixture();p.Terrain=Enumerable.Repeat(Terrain.Grass,p.Terrain.Length).ToArray();var roadless=ParcelGenerator.Preview(p,new(){X=0,Y=0,Width=256,Height=256},42);Check(roadless.Project.Buildings.Count==1&&roadless.Warnings.Any(w=>w.Contains("street")),"Roadless parcel invented street access");
  });
 }
 public static MapProject Fixture(){var p=MapGenerator.Generate(new(){Kind="blank",Size=256});for(var x=0;x<256;x++)for(var y=10;y<17;y++)p.Terrain[y*256+x]=Terrain.Road;p.Spawns=[new(){X=200,Y=200}];var b=new Building{X=55,Y=30,Width=12,Height=10,SourceId="geo-house",FootprintOrigin="source-grid"};b.Design=BuildingDesigner.Generate(b,1,42);p.Buildings.Add(b);p.Sites.Add(new(){Id="parcel-test",SourceId="geo-cad-52:1",Boundary=[[new(40,20),new(100,20),new(100,150),new(40,150)]],BuildingIds=[b.Id]});return p;}
 public static MapProject Quarter(){var p=MapGenerator.Generate(new(){Kind="blank",Size=256,Name="Surveyed quarter verification"});p.Spawns=[new(){X=225,Y=125}];for(var y=122;y<130;y++)for(var x=0;x<256;x++)p.Terrain[y*256+x]=Terrain.Road;var number=0;foreach(var x in new[]{40,120})foreach(var south in new[]{false,true}){var minY=south?134:20;var maxY=south?235:118;var b=new Building{X=x+15,Y=south?146:91,Width=12,Height=10,SourceId="geo-house-"+number,FootprintOrigin="source-grid"};b.Design=BuildingDesigner.Generate(b,1,42);p.Buildings.Add(b);p.Sites.Add(new(){Id="plot-"+number,SourceId="cad-52-1-"+number,Name="Surveyed plot "+number,Boundary=[[new(x,minY),new(x+60,minY),new(x+60,maxY),new(x,maxY)]],BuildingIds=[b.Id]});number++;}return p;}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
