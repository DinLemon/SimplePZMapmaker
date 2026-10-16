using System.Text.Json;
using SimplePZMapmaker.Core;

static class SettlementGenerationTests
{
 public static IEnumerable<(string,Action)> Cases()
 {
  yield return("settlement default preset preserves legacy generation",()=>{
   var a=MapGenerator.Generate(new(){Seed=29});var b=MapGenerator.Generate(new(){Seed=29,Rules=new(){Preset="legacy",Era="modern",Density=1}});
   Check(JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b),"Legacy generation changed when inactive rules changed");
  });
  yield return("settlement contextual generation is deterministic and resource independent",()=>{
   var options=new GenerationOptions{Seed=93,BuildingDensity=100,Rules=new(){Preset="contextual",Era="prewar",Density=100,LandUse="residential"}};
   var a=MapGenerator.Generate(options);var b=MapGenerator.Generate(options);
   Check(a.Buildings.Count>0,"No contextual buildings");Check(JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b),"Same seed changed contextual output");
   Check(a.Buildings.All(b=>b.RequiredMods.Count==0&&b.AssetSources.Count==0)&&a.RequiredMods.Count==0,"Generator requires local assets");
   Check(a.Buildings.All(b=>b.Kind is "house" or "apartments"),"Residential district contains unrelated industry");ProjectValidator.Validate(a);
  });
  yield return("settlement rules enforce era land use and floor interval",()=>{
   var industrial=MapGenerator.Generate(new(){Seed=37,BuildingDensity=100,Rules=new(){Preset="contextual",LandUse="industrial",Era="modern",Density=100,MinimumFloors=3,MaximumFloors=3}});
   Check(industrial.Buildings.Count>0&&industrial.Buildings.All(b=>b.Kind is "industrial" or "warehouse" or "garage"),"Industrial zoning ignored");
   Check(industrial.Buildings.All(b=>b.Design!.Floors.Count+b.Design.MinLevel==3&&b.Design.Style=="industrial"),"Floor interval or industrial appearance ignored");
   var old=MapGenerator.Generate(new(){Seed=37,BuildingDensity=100,Rules=new(){Preset="contextual",LandUse="residential",Era="prewar",Density=100}});
   var modern=MapGenerator.Generate(new(){Seed=37,BuildingDensity=100,Rules=new(){Preset="contextual",LandUse="residential",Era="modern",Density=100}});
   Check(old.Buildings.All(b=>b.Design!.Style is "brick" or "cottage" or "stone"),"Prewar contains modern facades");
   Check(modern.Buildings.Any(b=>b.Design!.Style=="modern"),"Modern era does not affect buildings");
  });
  yield return("settlement density setback and courtyard controls alter geometry",()=>{
   MapProject Make(int density,int setback=3,bool courtyard=true)=>MapGenerator.Generate(new(){Seed=12,BuildingDensity=100,Rules=new(){Preset="contextual",Density=density,LandUse="residential",Setback=setback,Courtyards=courtyard}});
   var empty=Make(0);var sparse=Make(15);var full=Make(100);Check(empty.Buildings.Count==0&&full.Buildings.Count>sparse.Buildings.Count,"Density does not control occupancy");
   var deep=Make(100,12);Check(deep.Buildings.Count>0,"Deep setback removed every building");
   Check(full.Buildings.Any(b=>!deep.Buildings.Any(d=>d.X==b.X&&d.Y==b.Y)),"Setback did not change locations");
   Check(full.Terrain.Count(t=>t==Terrain.Dirt)>Make(100,3,false).Terrain.Count(t=>t==Terrain.Dirt),"Courtyard control ignored");
   foreach(var b in deep.Buildings)Check(RoadDistance(deep,b)>=12,"Building violates road setback");
  });
  yield return("settlement entrances face roads and retain clear access",()=>{
   var p=StreetMap();SettlementGenerator.Populate(p,new(){Preset="contextual",Density=100,LandUse="residential"},66,100);
   Check(p.Buildings.Count>3,"Street frontage was not populated");
   foreach(var b in p.Buildings){BuildingDesigner.Validate(b);var entrances=Entrances(b).ToArray();Check(entrances.Length>0,"No exterior entrance");foreach(var e in entrances)Check(Connected(p,e.X,e.Y),"Exterior entrance has no clear paved path to a road");var expected=b.Id.Split('-')[^1] switch{"01"=>"e","21"=>"w","10"=>"s",_=>"n"};Check(Front(b)==expected,"Building front does not face its road");}
   ProjectValidator.Validate(p);
  });
  yield return("settlement additive fill preserves existing geometry manual terrain and resources",()=>{
   var p=StreetMap();var house=HousingTemplates.Create("village-izba");house.X=30;house.Y=30;p.Buildings.Add(house);
   p.Decorations.Add(new(){Id="existing-fence",X=109,Y=70,Asset="fencing_01_4",Parts=[new(0,0,"fencing_01_4")]});
   p.StaticModels.Add(new(){Id="00000000000000000000000000000001",ModelId="Base.TestModel",X=109,Y=160,Profile="obstacle",Footprint=[new(0,0),new(1,0),new(0,1),new(1,1)]});
   p.Generation=new(){ProtectedAreas=[new(){X=140,Y=160,Width=70,Height=60}]};var protectedBefore=JsonSerializer.Serialize(p.Generation);
   var existing=JsonSerializer.Serialize(house);p.SurfaceTiles[10*p.Width+10]="blends_natural_01_0";p.Terrain[90*p.Width+90]=Terrain.Water;
   var edited=TerrainEditMask.Decode(p);for(var y=20;y<70;y++)for(var x=150;x<210;x++){var i=y*p.Width+x;edited[i/8]|=(byte)(1<<(i%8));p.Terrain[i]=Terrain.Sand;}p.Version=6;p.TerrainEdits=TerrainEditMask.Encode(edited);
   var before=(Terrain[])p.Terrain.Clone();var result=SettlementGenerator.Populate(p,new(){Preset="contextual",Density=100},17,100);
   Check(result.PlacedBuildings>0,"No additive placements");Check(JsonSerializer.Serialize(house)==existing&&p.Buildings.Contains(house),"Existing building changed");
   Check(p.Terrain[90*p.Width+90]==Terrain.Water&&p.SurfaceTiles[10*p.Width+10]=="blends_natural_01_0","Water or explicit resources changed");
   Check(JsonSerializer.Serialize(p.Generation)==protectedBefore&&!p.Buildings.Skip(1).Any(b=>p.Generation.ProtectedAreas[0].Intersects(b)),"Protected area changed or populated");
   Check(p.Decorations.Count==1&&p.StaticModels.Count==1&&!p.Buildings.Skip(1).Any(b=>BuildingDesigner.Occupies(b,109-b.X,70-b.Y)||StaticModelPlacement.FootprintCells(p.StaticModels[0]).Any(c=>BuildingDesigner.Occupies(b,109+c.Dx-b.X,160+c.Dy-b.Y))),"Existing prop/model footprint overlapped");
   for(var i=0;i<before.Length;i++)if(TerrainEditMask.IsEdited(edited,i))Check(p.Terrain[i]==before[i]&&!p.Buildings.Skip(1).Any(b=>i%p.Width>=b.X&&i%p.Width<=b.X+b.Width&&i/p.Width>=b.Y&&i/p.Width<=b.Y+b.Height),"Manual terrain overwritten");
  });
  yield return("settlement invalid rules rejected and roadless fill reports limitation",()=>{
   foreach(var rules in new[]{new SettlementRules{Era="unknown"},new(){LandUse="unknown"},new(){Preset="unknown"},new(){Setback=0},new(){Density=101},new(){MinimumFloors=5,MaximumFloors=2}}){try{MapGenerator.Generate(new(){Rules=rules});throw new Exception("Invalid rules accepted");}catch(ArgumentException){}}
   var p=MapGenerator.Generate(new(){Kind="blank"});var result=SettlementGenerator.Populate(p,new(){Preset="contextual"},1,100);Check(result.PlacedBuildings==0&&result.Warnings.Count>0,"Roadless area not reported");
  });
  yield return("settlement editable road features remain traversable and retain their ownership",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p=LinearFeatureEditor.Put(p,new(){Id="editable-street",Kind="road",Width=6,Points=[new(20,80),new(230,80)]});
   var mask=TerrainEditMask.Decode(p);for(var i=0;i<p.Terrain.Length;i++)if(p.Terrain[i]==Terrain.Road)mask[i/8]|=(byte)(1<<(i%8));p.TerrainEdits=TerrainEditMask.Encode(mask);p.SurfaceTiles[80*p.Width+100]="blends_street_01_0";
   var before=JsonSerializer.Serialize(p.LinearFeatures);var result=SettlementGenerator.Populate(p,new(){Preset="contextual",LandUse="residential",Density=100},5,100);
   Check(result.PlacedBuildings>0,"Editable road frontage is incorrectly blocked");Check(JsonSerializer.Serialize(p.LinearFeatures)==before,"Street ownership changed");
   foreach(var b in p.Buildings)foreach(var e in Entrances(b))Check(Connected(p,e.X,e.Y),"Editable street has an inaccessible entrance");ProjectValidator.Validate(p);
  });
  yield return("settlement generated courtyard blocks have a usable ground gate",()=>{
   var p=MapGenerator.Generate(new(){Seed=93,BuildingDensity=100,Rules=new(){Preset="contextual",Era="prewar",LandUse="residential",Density=100}});
   var report=GenerationQuality.Analyze(p);Check(report.Complete&&report.Counts.Errors==0,"Generated settlement failed complete structural analysis");Check(!report.Issues.Any(i=>i.Code=="courtyard-isolated"),"Generated courtyard has no usable gate");
  });
  yield return("settlement mixed seeds retain actual room and stair landing access",()=>{
   foreach(var seed in new[]{0,1,2}){var p=MapGenerator.Generate(new(){Seed=seed,BuildingDensity=100,Rules=new(){Preset="contextual",Density=100}});var report=GenerationQuality.Analyze(p);var errors=report.Issues.Where(i=>i.Severity=="error").ToArray();Check(p.Buildings.Count>0&&report.Complete&&errors.Length==0,$"Mixed seed {seed} has {report.Counts.Errors} errors: "+string.Join(", ",errors.Take(3).Select(i=>$"{i.Code} {i.BuildingId}@{i.X},{i.Y},{i.Z}")));}
  });
  yield return("settlement all era and land use combinations preserve actual vertical access",()=>{
   foreach(var era in new[]{"prewar","soviet","modern","mixed"})foreach(var landUse in new[]{"residential","mixed","industrial","rural"}){
    var p=MapGenerator.Generate(new(){Seed=17,BuildingDensity=100,Rules=new(){Preset="contextual",Era=era,LandUse=landUse,Density=100,MinimumFloors=3,MaximumFloors=3}});var report=GenerationQuality.Analyze(p);Check(p.Buildings.Count>0&&report.Complete&&report.Counts.Errors==0,$"{era}/{landUse} has {report.Counts.Errors} errors: "+string.Join(", ",report.Issues.Where(i=>i.Severity=="error").Take(3).Select(i=>$"{i.Code} {i.BuildingId}@{i.X},{i.Y},{i.Z}")));
   }
  });
 }
 static MapProject StreetMap(){var p=MapGenerator.Generate(new(){Kind="blank"});for(var y=0;y<p.Height;y++)for(var x=0;x<p.Width;x++)if(x is >=120 and <=124||y is >=120 and <=124)p.Terrain[y*p.Width+x]=Terrain.Road;return p;}
 static int RoadDistance(MapProject p,Building b){var distance=int.MaxValue;for(var y=0;y<p.Height;y++)for(var x=0;x<p.Width;x++)if(p.Terrain[y*p.Width+x]==Terrain.Road)distance=Math.Min(distance,Math.Max(b.X-x,Math.Max(0,x-(b.X+b.Width-1)))+Math.Max(b.Y-y,Math.Max(0,y-(b.Y+b.Height-1))));return distance;}
 static IEnumerable<(int X,int Y)> Entrances(Building b){var f=b.Design!.Floors[-b.Design.MinLevel];foreach(var e in f.Edges.Where(e=>e.Kind is "door" or "open")){var a=BuildingDesigner.Cell(b,f,e.X,e.Y);var nx=e.X-(e.Edge=="w"?1:0);var ny=e.Y-(e.Edge=="n"?1:0);var other=BuildingDesigner.Cell(b,f,nx,ny);if((a=='.')==(other=='.'))continue;yield return a=='.'?(b.X+e.X,b.Y+e.Y):(b.X+nx,b.Y+ny);}}
 static string Front(Building b){var f=b.Design!.Floors[-b.Design.MinLevel];var e=f.Edges.First(e=>e.Kind=="door"&&(BuildingDesigner.Cell(b,f,e.X,e.Y)=='.')!=(BuildingDesigner.Cell(b,f,e.X-(e.Edge=="w"?1:0),e.Y-(e.Edge=="n"?1:0))=='.'));return e.Edge=="n"?(BuildingDesigner.Cell(b,f,e.X,e.Y)=='.'?"s":"n"):(BuildingDesigner.Cell(b,f,e.X,e.Y)=='.'?"e":"w");}
 static bool Connected(MapProject p,int x,int y){var queue=new Queue<(int X,int Y)>();var seen=new HashSet<(int,int)>();queue.Enqueue((x,y));while(queue.TryDequeue(out var c)){if(!seen.Add(c)||c.X<0||c.Y<0||c.X>=p.Width||c.Y>=p.Height)continue;var inside=p.Buildings.FirstOrDefault(b=>BuildingDesigner.Cell(b,b.Design!.Floors[-b.Design.MinLevel],c.X-b.X,c.Y-b.Y)!='.');if(inside is not null){var f=inside.Design!.Floors[-inside.Design.MinLevel];if(f.Rooms[BuildingDesigner.Cell(inside,f,c.X-inside.X,c.Y-inside.Y).ToString()]!="hall")continue;}else if(p.Terrain[c.Y*p.Width+c.X] is not(Terrain.Road or Terrain.Pavement))continue;if(p.Terrain[c.Y*p.Width+c.X]==Terrain.Road)return true;foreach(var d in new[]{(0,-1),(1,0),(0,1),(-1,0)}){var next=(X:c.X+d.Item1,Y:c.Y+d.Item2);if(p.Buildings.Any(b=>b.Design!.Floors[-b.Design.MinLevel].Edges.Any(e=>e.Kind is "wall" or "window"&&e.Edge==(d.Item1==0?"n":"w")&&b.X+e.X==(d.Item1==0?c.X:Math.Max(c.X,next.X))&&b.Y+e.Y==(d.Item1==0?Math.Max(c.Y,next.Y):c.Y))))continue;queue.Enqueue(next);}}return false;}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
