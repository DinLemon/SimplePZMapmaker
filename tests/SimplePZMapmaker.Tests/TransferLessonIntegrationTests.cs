using System.Text.Json;
using System.Text.Json.Nodes;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
public static class TransferLessonIntegrationTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("transfer supplemental GeoJSON retains complete attributes and replaces only stable identities",()=>{
   var first=Point("same",new(){["name"]="before",["nested"]=new JsonObject{["value"]=42}});
   var last=Point("same",new(){["name"]="after",["nested"]=new JsonObject{["value"]=43}});
   var result=GeographicImporter.CombineSources([first,Point("different",new(){["name"]="independent"}),last]);
   Check(result.Features==2&&result.Replaced==1,"Source composition erased independent evidence");using var doc=JsonDocument.Parse(result.GeoJson);
   Check(doc.RootElement.GetProperty("features")[0].GetProperty("properties").GetProperty("nested").GetProperty("value").GetInt32()==43,"Full source attributes lost");
   Reject(()=>GeographicImporter.CombineSources([first],16));
  });
  yield return("transfer combines Overpass baseline with a survey without discarding roads",()=>{
   var osm="""{"elements":[{"type":"way","id":7,"tags":{"highway":"residential"},"geometry":[{"lon":45,"lat":55},{"lon":45.001,"lat":55.001}]}]}""";
   var result=GeographicImporter.CombineSources([osm,Point("survey",new(){["entrance"]="yes"})]);using var doc=JsonDocument.Parse(result.GeoJson);
   var road=doc.RootElement.GetProperty("features").EnumerateArray().Single(f=>f.GetProperty("properties").TryGetProperty("highway",out _));
   Check(result.Features==2&&road.GetProperty("properties").GetProperty("source:attribution").GetString()!.Contains("OpenStreetMap"),"Baseline road or attribution disappeared");
  });
  yield return("transfer local correction targets an explicit source identity and retains inference",()=>{
   var supplied=Point("local",new(){["pz:replaces"]="osm-way-7",["building:levels"]="2",["pz:evidence"]="inferred"});
   var result=GeographicSurvey.Normalize(supplied,new(){Source=GeographicSurveySourceKind.LocalSurvey,Attribution="Resident survey"});using var doc=JsonDocument.Parse(result.GeoJson);var feature=doc.RootElement.GetProperty("features")[0];
   Check(feature.GetProperty("id").GetString()=="osm-way-7","Correction lost target identity");
   Check(feature.GetProperty("properties").GetProperty("pz:evidence").GetString()=="inferred","Inferred observation promoted to surveyed fact");
   Reject(()=>GeographicSurvey.Normalize(supplied,new(){Source=GeographicSurveySourceKind.Cadastre,Attribution="NSPD"}));
  });
  yield return("transfer compact provenance survives rich source tag budgets",()=>{
   var tags=Enumerable.Range(0,100).ToDictionary(i=>"pz:extra-"+i,i=>"extra");foreach(var key in GeographicSurvey.EssentialProjectTags)tags[key]="essential";
   var retained=GeographicEvidence.CaptureTags(tags);Check(retained.Count==64&&GeographicSurvey.EssentialProjectTags.All(retained.ContainsKey),"Essential attribution was displaced by incidental tags");
  });
  yield return("transfer warped fetched polygon clips outer boundary and removes outside holes without changing original evidence",()=>{
   var reference=Reference();var affine=new GeographicProjection(reference,100,100);reference.Warp=Grid();var projection=new GeographicProjection(reference,100,100);
   var outer=Path(affine,(-30,-30),(400,-30),(400,400),(-30,400),(-30,-30));var hole=Path(affine,(300,300),(350,300),(350,350),(300,350),(300,300));
   var rings=projection.ProjectGeometry(new[]{outer,hole},true);Check(rings.Count==1&&rings[0].All(p=>p.X>=-1e-7&&p.Y>=-1e-7&&p.X<=256+1e-7&&p.Y<=256+1e-7),"Bounded clipping lost a hole boundary or fabricated mesh coordinates");
   var p=MapGenerator.Generate(new(){Kind="blank"});p.SourceFeatures=[new(){Id="clipped",SourceId="source",Kind="site",GeometryKind="polygon",SourceGeometry=new[]{outer,hole}.Select(r=>r.Select(v=>new SourceOutlinePoint(v.X,v.Y)).ToList()).ToList(),Geometry=rings.Select(r=>r.Select(v=>new SitePoint(v.X,v.Y)).ToList()).ToList()}];
   Check(p.Version==15&&p.SourceFeatures[0].SourceGeometry.Count==2,"Original source geometry was rewritten");GeographicEvidence.Validate(p);
   var restored=JsonSerializer.Deserialize<MapProject>(JsonSerializer.Serialize(p,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;GeographicEvidence.Validate(restored);
  });
  yield return("transfer clipped crossing polylines remain separate and share section seam coordinates",()=>{
   var reference=Reference();var affine=new GeographicProjection(reference,100,100);reference.Warp=Grid();var projection=new GeographicProjection(reference,100,100);
   var line=Path(affine,(-20,20),(280,20),(280,220),(-20,220));var pieces=projection.ProjectGeometry([line],false);
   Check(pieces.Count==2&&pieces.All(r=>r.All(v=>v.X>=-1e-7&&v.X<=256+1e-7)),"Disconnected clipping was joined through exterior space");
   var adjacent=new GeographicProjection(reference,101,100).ProjectGeometry([line],false);Check(pieces.SelectMany(r=>r).Zip(adjacent.SelectMany(r=>r)).All(v=>Math.Abs(v.First.X-v.Second.X-256)<1e-8&&Math.Abs(v.First.Y-v.Second.Y)<1e-8),"Clipping depends on section origin");
  });
  yield return("transfer warped importer accepts whole source contours beyond the mesh",()=>{
   var reference=Reference();var affine=new GeographicProjection(reference,100,100);reference.Warp=Grid();
   var ring=Path(affine,(-20,-20),(280,-20),(280,280),(-20,280),(-20,-20));
   var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",id="forest",properties=new{landuse="forest"},geometry=new{type="Polygon",coordinates=new[]{ring.Select(p=>new[]{p.X,p.Y})}}}}});
   var map=MapGenerator.Generate(new(){Kind="blank"});var imported=GeographicImporter.Import(json,map,reference.Bounds,projection:new(reference,100,100));ProjectValidator.Validate(imported.Project);
   Check(imported.Project.Terrain.Contains(Terrain.Forest)&&imported.Project.ImportSummary!.Notes.Any(n=>n.Contains("deformation")),"Outer source contour was lost or clipping was hidden");
  });
  yield return("transfer L T and courtyard roof partitions cover each occupied square once with consistent wings",()=>{
   foreach(var cells in new[]{"11111111"+"11111111"+"1111...."+"1111...."+"1111...."+"1111....","11111111"+"11111111"+"..1111.."+"..1111.."+"..1111.."+"..1111..","11111111"+"11....11"+"11....11"+"11....11"+"11....11"+"11111111"}){
    var b=new Building{Width=8,Height=6};b.Design=BuildingDesigner.Generate(b,1,42);b.Design.Roof="gabled";var f=b.Design.Floors[0];f.Cells=cells;f.Rooms=new(){["1"]="bedroom"};f.Objects=[];f.Edges=BuildingDesigner.AutoEdges(b,f,0);
    var parts=RoofPartitions.Parts(b,f);var squares=parts.SelectMany(r=>Enumerable.Range(r.Y,r.Height).SelectMany(y=>Enumerable.Range(r.X,r.Width).Select(x=>y*8+x))).ToArray();
    Check(parts.Count>1&&squares.Length==squares.Distinct().Count()&&squares.Order().SequenceEqual(Enumerable.Range(0,48).Where(i=>cells[i]!='.')),"Roof partition filled a courtyard or lost a wing");
    foreach(var i in squares){var r=RoofPartitions.At(b,f,i%8,i/8);var piece=RoofGeometry.Piece(b,f,i%8,i/8);Check(piece.Flat==(r.Width<2||r.Height<2),"Thin roof detail was not made safe");}
    Check(NativeScene.Building(b).Sprites.Count>0,"Partition does not reach native composition");
   }
  });
 }
 static WorldGeoReference Reference()=>new(){Bounds=new(){West=45,East=45+256/(Math.Cos(55*Math.PI/180)*111320),North=55+256d/111320,South=55},OriginCellX=100,OriginCellY=100};
 static GeographicWarpDefinition Grid()=>new(){SourceX=[0,128,256],SourceY=[0,128,256],Destination=[new(0,0),new(128,0),new(256,0),new(0,128),new(128,100),new(256,128),new(0,256),new(128,256),new(256,256)]};
 static List<(double X,double Y)> Path(GeographicProjection p,params (double X,double Y)[] points)=>points.Select(v=>{var g=p.Geographic(v.X,v.Y);return(g.Longitude,g.Latitude);}).ToList();
 static string Point(string id,JsonObject properties)=>new JsonObject{["type"]="FeatureCollection",["features"]=new JsonArray(new JsonObject{["type"]="Feature",["id"]=id,["properties"]=properties,["geometry"]=new JsonObject{["type"]="Point",["coordinates"]=new JsonArray(45.0,55.0)}})}.ToJsonString();
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid input accepted");}
}
