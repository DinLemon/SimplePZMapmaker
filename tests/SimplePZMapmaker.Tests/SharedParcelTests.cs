using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

static class SharedParcelTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("shared parcel occupancy protects complete models authored ground and native fences",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.StaticModels.Add(new(){X=60,Y=60,ModelId="Base.Test",Footprint=[new(0,0),new(1,0)]});p.FenceStyles=[NeighborhoodPlanner.Copy(ParcelFences.Picket)];p.Fences=[new(){Style=ParcelFences.Picket.Id,Points=[new(70,70),new(75,70)]}];TerrainEditMask.Mark(p,80*p.Width+80);
   var placement=new ParcelPlacement(p);Check(!placement.Free(61,60)&&!placement.Free(72,70)&&!placement.Free(80,80),"A generator can overwrite protected geometry");placement.ReservePassage([(90,90)]);Check(!placement.Free(90,90)&&placement.Free(90,90,forPassage:true),"Optional furnishing consumes pedestrian access");
  });
  yield return("new parcel fences use native layer and legacy fence decorations remain unchanged",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var old=new WorldDecoration{Id="legacy-fence",X=240,Y=240,Asset="fencing_01_4",Parts=[new(0,0,"fencing_01_4",4)]};p.Decorations.Add(old);var before=JsonSerializer.Serialize(old);
   var plan=NeighborhoodPlanner.Create(p,new(){MaximumBuildings=2,TemplateIds=["village-izba"]});Check(plan.Fences.Count>0&&plan.Decorations.Count==0,"Generator still uses decoration fence slots");NeighborhoodPlanner.Apply(p,plan);Check(p.Version>=12&&p.Fences.Count==plan.Fences.Count&&JsonSerializer.Serialize(p.Decorations.Single())==before,"Native fence addition migrated or lost the legacy decoration");Check(NativeScene.ResourceIds(p).Contains("fencing_01_6"),"Native corners did not reach the resource set");
  });
  yield return("template strategy shares pure preview server proposals and stale apply protection",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var before=JsonSerializer.Serialize(p);var area=new GenerationArea{X=16,Y=16,Width=224,Height=224};var c=new NeighborhoodConstraints{MaximumBuildings=3,TemplateIds=["village-izba"]};var a=GenerationStudio.Preview(p,area,52,rules:new(){Preset="contextual"},strategy:"templates",neighborhood:c);var b=GenerationStudio.Preview(p,area,52,rules:new(){Preset="contextual"},strategy:"templates",neighborhood:c);
   Check(JsonSerializer.Serialize(p)==before&&JsonSerializer.Serialize(a.Project)==JsonSerializer.Serialize(b.Project),"Preview mutates source or ignores seed");Check(a.Summary.AddedBuildings==3&&a.Project.Fences.Count>0&&a.Project.Buildings.All(GenerationStudio.IsGenerated),"Template strategy lost geometry or provenance");
   var store=new GenerationProposalStore();var saved=store.Add(a);var result=store.Apply(p,saved.ProposalId);Check(result.Project.Buildings.Count==3,"Stored strategy proposal did not apply");p.Name="changed";Reject(()=>store.Apply(p,saved.ProposalId));
  });
  yield return("fence style conflicts and blocked proposed fences leave parcel application atomic",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var plan=NeighborhoodPlanner.Create(p,new(){MaximumBuildings=1,TemplateIds=["village-izba"]});p.FenceStyles=[new(){Id=ParcelFences.Picket.Id,W="fencing_01_8",N="fencing_01_9"}];var before=JsonSerializer.Serialize(p);Reject(()=>NeighborhoodPlanner.Apply(p,plan));Check(JsonSerializer.Serialize(p)==before,"Rejected fence style changed the project");
  });
  yield return("template strategy retains underground stairs without turning reservations into authored edits",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p=LinearFeatureEditor.Put(p,new(){Id="underground",Kind="bridge",Level=-1,Access="stairs",Width=4,Points=[new(50,180),new(110,180)]});var before=JsonSerializer.Serialize(p);
   var preview=GenerationStudio.Preview(p,new(){X=16,Y=16,Width=224,Height=224},17,rules:new(){Preset="contextual"},strategy:"templates",neighborhood:new(){MaximumBuildings=2,TemplateIds=["village-izba"]});
   ProjectValidator.Validate(preview.Project);Check(JsonSerializer.Serialize(p)==before&&preview.Project.LinearFeatures.Count==1,"Template reservations changed infrastructure or authored input");
  });
  yield return("template strategy transfers complete collector roads across widely spaced parcels",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var area=new GenerationArea{X=16,Y=16,Width=224,Height=224};var preview=GenerationStudio.Preview(p,area,42,rules:new(){Preset="contextual"},strategy:"templates",neighborhood:new(){MaximumBuildings=2,Spacing=64,TemplateIds=["village-izba"]});
   Check(preview.Summary.AddedBuildings==2,"Road fixture needs two buildings");for(var x=60;x<=86;x++)Check(preview.Project.Terrain[19*256+x]==Terrain.Road,"Validated frontage was cropped between template buildings");
  });
  yield return("template strategy uses remaining section capacity instead of rejecting the whole preview",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Size=512});p.Spawns=[new(){X=500,Y=500}];for(var i=0;i<3999;i++)p.Buildings.Add(new(){Id="small-"+i,X=2+(i%64)*4,Y=2+(i/64)*4,Width=2,Height=2});
   ProjectValidator.Validate(p);var preview=GenerationStudio.Preview(p,new(){X=272,Y=272,Width=224,Height=224},42,rules:new(){Preset="contextual"},strategy:"templates",neighborhood:new(){MaximumBuildings=3,TemplateIds=["village-izba"]});
   Check(preview.Project.Buildings.Count==4000&&preview.Summary.AddedBuildings==1&&preview.Summary.Warnings.Any(w=>w.Contains("budget")),"Remaining building capacity was ignored");
  });
  yield return("parcel proposals cannot put a new native fence across their pedestrian access",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var plan=NeighborhoodPlanner.Create(p,new(){MaximumBuildings=1,TemplateIds=["village-izba"]});var path=plan.Surfaces.First(s=>s.Kind=="driveway");
   plan.Fences.Add(new(null,new(){Style=ParcelFences.Picket.Id,Points=[new(path.X,path.Y),new(path.X+1,path.Y)]}));var before=JsonSerializer.Serialize(p);Reject(()=>NeighborhoodPlanner.Apply(p,plan));Check(JsonSerializer.Serialize(p)==before,"Rejected access blockage changed the map");
  });
  yield return("generation proposals retain geographic evidence above the old 64 MiB limit",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Version=14;var tags=Enumerable.Range(0,64).ToDictionary(i=>i.ToString("D2")+new string('k',98),_=>new string('v',300));
   p.SourceFeatures=Enumerable.Range(0,2700).Select(i=>new GeographicFeature{Id="evidence-"+i,SourceId="source-"+i,Tags=tags,SourceGeometry=[[new(30,50)]],Geometry=[[new(20,20)]]}).ToList();
   Check(JsonSerializer.SerializeToUtf8Bytes(p).Length>64*1024*1024,"Proposal fixture did not exceed the old limit");var store=new GenerationProposalStore();var proposal=store.Add(new(GenerationStudio.Fingerprint(p),p,new(0,0,0,0,0,[]),[]));Check(store.Apply(p,proposal.ProposalId).Project.SourceFeatures.Count==2700,"Large proposal lost source evidence");
  });
  yield return("ensemble traversable frontage can share an existing house entrance route",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});for(var x=0;x<256;x++)p.Terrain[32*256+x]=Terrain.Road;
   var house=new Building{X=100,Y=6,Width=12,Height=10};house.Design=BuildingDesigner.Generate(house,1,42);var floor=house.Design.Floors[0];floor.Cells=new string('0',house.Width*house.Height);floor.Rooms=new(){["0"]="hall"};floor.Objects.Clear();floor.Edges.RemoveAll(e=>e.X>0&&e.X<house.Width&&e.Y>0&&e.Y<house.Height);foreach(var e in floor.Edges.Where(e=>e.Kind=="door"))if(e.Y==0||e.X==0||e.Y==house.Height||e.X==house.Width)e.Kind="wall";
   var exit=floor.Edges.First(e=>e.Edge=="n"&&e.Y==house.Height&&e.X==6);exit.Kind="door";p.Buildings.Add(house);ProjectValidator.Validate(p);
   var placement=new ParcelPlacement(p);placement.ReserveEntrances();Check(placement.Passage(106,22),"Fixture needs an existing entrance path across the future frontage");
   var plan=StreetEnsembles.Create(p,new(){X=20,Y=20,Width=100,Height=90},[]);var applied=StreetEnsembles.Apply(p,plan);Check(applied.Terrain[22*256+106]==Terrain.Road&&applied.Buildings.Any(b=>b.Id==house.Id),"Shared traversable frontage lost the existing house or its route");
  });
  yield return("ensemble preview omits complete lamp footprints crossing its parking apron",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var lamp=new StaticModelPlacement{ModelId="Base.FloorLamp",Profile="light",Light=new(),Footprint=[new(0,0),new(1,0),new(2,0)]};
   var plan=StreetEnsembles.Create(p,new(){X=20,Y=20,Width=100,Height=90},[],lamp);Check(plan.StaticModels.Count==1,"The model footprint crossed planned parking");ProjectValidator.Validate(StreetEnsembles.Apply(p,plan));
  });
  yield return("template world preview protects shared fence projections without persisting their fragments",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});for(var x=8;x<240;x++)p.Terrain[32*256+x]=Terrain.Road;
   var member=new WorldMapMember{Project=p};var world=new WorldProject{Maps=[member],FenceStyles=[NeighborhoodPlanner.Copy(ParcelFences.Picket)],Fences=[new(){Id="shared-road-edge",Style=ParcelFences.Picket.Id,Points=[new(p.WorldX*256+20,p.WorldY*256+32),new(p.WorldX*256+220,p.WorldY*256+32)]}]};
   var before=JsonSerializer.Serialize(world);var context=GenerationStudioWorldContext.Create(p,world,member.Id);var preview=GenerationStudio.Preview(p,new(){X=16,Y=16,Width=224,Height=224},42,rules:new(){Preset="contextual"},worldContext:context,strategy:"templates",neighborhood:new(){MaximumBuildings=2,TemplateIds=["village-izba"]});
   context.ValidateCandidate(preview.Project);Check(preview.Project.Fences.All(f=>f.Id!="shared-road-edge")&&JsonSerializer.Serialize(world)==before,"Projected world fence became local authoritative data");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action a){try{a();}catch(ArgumentException){return;}throw new Exception("Invalid proposal accepted");}
}
