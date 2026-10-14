using SimplePZMapmaker.Core;
using System.Text.Json;

public static class ZLevelTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("all 64 signed floors generate regenerate and retain connected stair access",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40,Width=12,Height=10};
   BuildingDesigner.Generate(b,32,42,"rect",32);p.Buildings.Add(b);ProjectValidator.Validate(p);
   if(b.Design!.MinLevel!=-32||b.Design.Floors.Count!=64||b.Design.Roof!="none"||RoofGeometry.MaxLevel(b)!=31||b.Design.Floors.Sum(f=>f.Objects.Count(o=>o.Kind=="stairs"))!=63)throw new Exception("The complete signed stack or stair links were lost");
   var masks=b.Design.Floors.Select(f=>f.Cells.Select(c=>c!='.').ToArray()).ToArray();BuildingDesigner.Regenerate(p,b.Id);
   if(b.Design.MinLevel!=-32||b.Design.Floors.Count!=64||b.Design.Floors.Where((f,i)=>!f.Cells.Select(c=>c!='.').SequenceEqual(masks[i])).Any())throw new Exception("Regeneration changed the signed stack");
   b.Design.Floors[30].Objects.RemoveAll(o=>o.Kind=="stairs");Reject(()=>BuildingDesigner.Validate(b));
  });
  yield return("signed endpoints are inclusive and out of range generation is rejected without mutation",()=>{
   var b=new Building{Width=12,Height=10};BuildingDesigner.Generate(b,1,42);var original=b.Design;
   Reject(()=>BuildingDesigner.Generate(b,33,42));Reject(()=>BuildingDesigner.Generate(b,1,42,"rect",33));
   if(b.Design!=original)throw new Exception("Invalid generation mutated the original design");
   b.Design!.MinLevel=-33;Reject(()=>BuildingDesigner.Validate(b));
   foreach(var z in new[]{-32,31})StaticModelPlacement.Validate(new(){ModelId="Base.Test",Z=z},12,10);
   foreach(var z in new[]{-33,32})Reject(()=>StaticModelPlacement.Validate(new(){ModelId="Base.Test",Z=z},12,10,-100,100));
  });
  yield return("full signed-stack access rejects an isolated upper room despite all stair links",()=>{
   var b=new Building{Width=12,Height=10};BuildingDesigner.Generate(b,32,42,"rect",32);
   for(var i=0;i<b.Design!.Floors.Count;i++){var floor=b.Design.Floors[i];floor.Cells=new string('0',120);floor.Rooms=new(){["0"]="hall"};floor.Objects.RemoveAll(o=>o.Kind!="stairs");floor.Edges=BuildingDesigner.AutoEdges(b,floor,i+b.Design.MinLevel);}
   BuildingDesigner.Validate(b);
   b.Design.Floors[^1].Edges.AddRange([new(){X=11,Y=9,Edge="n",Kind="wall"},new(){X=11,Y=9,Edge="w",Kind="wall"}]);
   try{BuildingDesigner.Validate(b);}catch(ArgumentException error) when(error.Message.Contains("isolated room")){return;}throw new Exception("An isolated room at z=31 remained accessible");
  });
  yield return("automatic roofs respect actual slope depth and ceiling headroom",()=>{
   var b=new Building{Width=20,Height=20};BuildingDesigner.Generate(b,30,42);b.Design!.Roof="gabled";
   if(RoofGeometry.MaxLevel(b)!=31)throw new Exception("Six-piece slope height was not measured");BuildingDesigner.Validate(b);
   b.Design.Roof="shed";if(RoofGeometry.MaxLevel(b)!=33)throw new Exception("Shed roof span did not use actual slope depth");Reject(()=>BuildingDesigner.Validate(b));
   if(!RoofGeometry.SelectSafeAutomaticRoof(b)||b.Design.Roof!="flat"||RoofGeometry.MaxLevel(b)!=30)throw new Exception("Unsafe imported roof was not adapted to a valid flat roof");
   for(var seed=0;seed<12;seed++){BuildingDesigner.Generate(b,31,seed);if(RoofGeometry.MaxLevel(b)>31)throw new Exception("Generated high roof overflows");}
   BuildingDesigner.Generate(b,32,42);foreach(var roof in new[]{"flat","gabled","shed","hipped"}){b.Design!.Roof=roof;Reject(()=>BuildingDesigner.Validate(b));}
   b.Design!.Roof="none";BuildingDesigner.Validate(b);
  });
  yield return("roof checks include exposed lower terraces and preserve open balconies",()=>{
   var b=new Building{Width=12,Height=10};BuildingDesigner.Generate(b,32,42);b.Design!.Roof="flat";var top=b.Design.Floors[^1];
   top.Rooms=top.Rooms.ToDictionary(pair=>pair.Key,pair=>"balcony");
   if(RoofGeometry.MaxLevel(b)!=31)throw new Exception("Balcony received an automatic ceiling");BuildingDesigner.Validate(b);
   top.Rooms["0"]="hall";Reject(()=>BuildingDesigner.Validate(b));b.Design.Roof="none";BuildingDesigner.Validate(b);
  });
  yield return("OSM floor and height imports preserve 32 above and 32 basement levels",()=>{
   var bounds=new GeoBounds{West=30,East=30.003,South=50,North=50.003};
   foreach(var tags in new[]{"\"building:levels\":\"32\"","\"height\":\"96 m\""}){
    var source="{\"elements\":[{\"type\":\"way\",\"id\":77,\"tags\":{\"building\":\"apartments\","+tags+",\"building:levels:underground\":\"32\",\"roof:shape\":\"gabled\"},\"geometry\":[{\"lon\":30.0001,\"lat\":50.0001},{\"lon\":30.0004,\"lat\":50.0001},{\"lon\":30.0004,\"lat\":50.0004},{\"lon\":30.0001,\"lat\":50.0004},{\"lon\":30.0001,\"lat\":50.0001}]}]}";
    var p=GeographicImporter.Import(source,MapGenerator.Generate(new(){Kind="blank"}),bounds).Project;var b=p.Buildings.Single();
    if(b.Design!.MinLevel!=-32||b.Design.Floors.Count!=64||b.Design.Roof!="none"||b.SourceRoof!="gabled"||p.ImportSummary!.Notes.Any(note=>note.Contains("limited to 16"))||p.ImportSummary.Quality!.AdaptedRoofs!=1||p.ImportSummary.Quality.SourceRoofs!=0)throw new Exception("OSM levels were silently capped or adapted source roof evidence lost");ProjectValidator.Validate(p);
   }
  });
  yield return("neighborhood task constraints accept 32 storeys and reject 33",()=>{
   var c=new NeighborhoodConstraints{MaximumLevels=32};NeighborhoodPlanner.ValidateConstraints(c);c.MaximumLevels=33;Reject(()=>NeighborhoodPlanner.ValidateConstraints(c));
  });
  yield return("cross-section OSM buildings retain all 64 signed floors as one shared building",()=>{
   var world=new WorldProject{GeoReference=new(){Bounds=new(){West=32,East=32.02,South=54.77,North=54.79},MetresPerTile=1}};
   for(var i=0;i<2;i++){var p=MapGenerator.Generate(new(){Kind="blank"});p.WorldX=100+i;p.WorldY=100;world.Maps.Add(new(){Project=p});}
   var projection=new GeographicProjection(world.GeoReference,100,100);
   var points=new[]{(250d,80d),(266d,80d),(266d,94d),(250d,94d),(250d,80d)}.Select(point=>{var g=projection.Geographic(point.Item1,point.Item2);return new{lon=g.Longitude,lat=g.Latitude};});
   var json=JsonSerializer.Serialize(new{elements=new[]{new{type="way",id=42,tags=new Dictionary<string,string>{{"building","house"},{"building:levels","32"},{"building:levels:underground","32"},{"roof:shape","gabled"}},geometry=points}}});
   var notes=new List<string>();var result=GeographicImporter.ImportWorldBuildings(json,world,42,notes);var b=world.Buildings.Single();
   if(result.Added!=1||b.Design!.MinLevel!=-32||b.Design.Floors.Count!=64||b.Design.Roof!="none"||b.SourceRoof!="gabled"||world.Maps.Any(m=>m.Project.Buildings.Count!=0)||!notes.Any(n=>n.Contains("adapted to fit")))throw new Exception("Shared OSM signed stack was capped, duplicated, or its roof adaptation was hidden");WorldAssembler.Validate(world);
  });
 }
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected a signed-level or roof validation failure");}
}
