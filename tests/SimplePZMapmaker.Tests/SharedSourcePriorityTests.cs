using System.Text.Json;
using SimplePZMapmaker.Core;
public static class SharedSourcePriorityTests{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("shared source priority stays consistent with local source order across seams",()=>{
   var (json,w)=Fixture();var before=w.Maps[0].Project.Buildings.Single();var source=before.SourceId;var warnings=new List<string>();var result=GeographicImporter.ImportWorldBuildings(json,w,42,warnings);
   if(result.Added!=1||w.Buildings.Single().SourceId!="geo-first"||w.Maps.Any(m=>m.Project.Buildings.Any(b=>b.SourceId==source)))throw new Exception("Local lower-priority outline blocked earlier shared source");
   var evidence=w.Maps[0].Project.SourceFeatures.Single(f=>f.SourceId==source);if(evidence.BuildingId is not null)throw new Exception("Deferred outline retains a dangling building binding");
   if(!w.Maps[0].Project.ImportSummary!.SkippedOutlines.Any(o=>o.SourceId==source&&o.Reason.Contains("source priority")))throw new Exception("Deferred source disappeared from report");WorldAssembler.Validate(w);
  });
  foreach(var locked in new[]{false,true})yield return($"shared source priority respects {(locked?"locked":"modified")} local buildings",()=>{
   var (json,w)=Fixture();var b=w.Maps[0].Project.Buildings.Single();if(locked){w.Maps[0].Project.Sites.Add(new(){Id="locked-site",Name="Keep",Locked=true,Boundary=[[new(240,30),new(255,30),new(255,70),new(240,70),new(240,30)]],BuildingIds=[b.Id]});}else b.Modified=true;
   var result=GeographicImporter.ImportWorldBuildings(json,w,42,[]);if(result.Added!=0||w.Maps[0].Project.Buildings.Single().Id!=b.Id)throw new Exception("Protected local building was displaced");
  });
  yield return("shared source priority rolls back local placements when the shared source is invalid",()=>{
   var (json,w)=Fixture(water:true);var before=JsonSerializer.Serialize(w.Maps);var result=GeographicImporter.ImportWorldBuildings(json,w,42,[]);if(result.Added!=0||JsonSerializer.Serialize(w.Maps)!=before)throw new Exception("Failed shared source changed local geometry, evidence, or reports");WorldAssembler.Validate(w);
  });
 }
 static (string,WorldProject) Fixture(bool water=false){
  var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.012,South=54,North=54.003},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var origin=new GeographicProjection(reference,100,100);
  double[] At(double x,double y){var p=origin.Geographic(x,y);return[p.Longitude,p.Latitude];}
  object Feature(string id,double x1,double y1,double x2,double y2,string kind)=>new{type="Feature",id,properties=new Dictionary<string,string>{{kind=="water"?"natural":"building",kind}},geometry=new{type="Polygon",coordinates=new[]{new[]{At(x1,y1),At(x2,y1),At(x2,y2),At(x1,y2),At(x1,y1)}}}};
  var fs=new List<object>{Feature("first",250,40,270,60,"house"),Feature("second",247,44,254,51,"shed")};if(water)fs.Add(Feature("wet",262,43,268,57,"water"));var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=fs});var world=new WorldProject{GeoReference=reference};
  for(var x=0;x<2;x++){var projection=new GeographicProjection(reference,100+x,100);var p=MapGenerator.Generate(new(){Kind="blank"});p.WorldX=100+x;p.WorldY=100;p.WorldSection=true;p.Version=6;p=GeographicImporter.Import(json,p,projection.SectionBounds(256,256),projection:projection).Project;world.Maps.Add(new(){Id=x.ToString("D32"),Project=p});}
  WorldAssembler.Validate(world);return(json,world);
 }
}
