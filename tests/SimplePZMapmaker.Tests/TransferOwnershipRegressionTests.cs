using System.Text.Json;
using SimplePZMapmaker.Core;
public static class TransferOwnershipRegressionTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("parcel regression creating a group never imports generation ownership from its payload",()=>{
   var p=Complete(ParcelGenerationTests.Fixture());var supplied=NeighborhoodPlanner.Copy(p.Sites[0]);p.Sites[0].Locked=true;var originalFences=p.Fences.Select(f=>f.Id).ToHashSet();var created=SiteEditor.Apply(new(p,"create",Site:supplied));
   Check(created.Project.Sites.Single(s=>s.Id==created.SiteId).ParcelGeneration is null,"Create inherited foreign generation ownership");var result=Complete(created.Project,true,99);Check(originalFences.All(id=>result.Fences.Any(f=>f.Id==id)),"Created group removed original pinned fences");
  });
  yield return("parcel regression overlapping pinned site protects generated ground and its ownership",()=>{
   var p=Complete(ParcelGenerationTests.Fixture());var (i,ground)=p.Sites[0].ParcelGeneration!.Ground.First(pair=>pair.Value.After==Terrain.Pavement);var x=i%p.Width;var y=i/p.Width;
   p.Sites.Add(new(){Id="pinned-tile",Name="Pinned surface",Locked=true,Boundary=[[new(x,y),new(x+1,y),new(x+1,y+1),new(x,y+1)]]});var result=Complete(p,true,99);
   Check(result.Terrain[i]==ground.After,"Pinned-site ground was restored to an old baseline");Check(result.Sites.Single(s=>s.Id==p.Sites[0].Id).ParcelGeneration!.Ground.ContainsKey(i),"Protected ground lost ownership");
  });
  yield return("parcel regression copying a completed site cannot regenerate original objects",()=>{
   var p=Complete(ParcelGenerationTests.Fixture());var originals=p.Buildings.ToDictionary(b=>b.Id,b=>JsonSerializer.Serialize(b));var originalState=JsonSerializer.Serialize(p.Sites[0].ParcelGeneration);
   var copy=SiteEditor.Apply(new(p,"copy",SiteId:p.Sites[0].Id,Dx:110));var after=ParcelGenerator.Preview(copy.Project,new(){X=140,Y=0,Width=116,Height=256},99,true).Project;
   Check(originals.All(pair=>after.Buildings.Any(b=>b.Id==pair.Key&&JsonSerializer.Serialize(b)==pair.Value)),"Regenerating copied site deleted or changed originals");Check(JsonSerializer.Serialize(after.Sites.Single(s=>s.Id==p.Sites[0].Id).ParcelGeneration)==originalState,"Copy changed original ownership state");
  });
  yield return("parcel regression regeneration preserves pins and protected areas until released",()=>{
   foreach(var protectedArea in new[]{false,true}){var p=Complete(ParcelGenerationTests.Fixture());var child=p.Buildings.First(b=>b.SourceKind=="parcel-generator");var before=JsonSerializer.Serialize(child);p.Generation=new();if(protectedArea)p.Generation.ProtectedAreas.Add(new(){X=child.X,Y=child.Y,Width=child.Width+1,Height=child.Height+1});else p.Generation.LockedBuildingIds.Add(child.Id);
    var after=Complete(p,true,99);Check(after.Buildings.Any(b=>b.Id==child.Id&&JsonSerializer.Serialize(b)==before),"Regeneration moved a pinned/protected child");Check(after.Sites[0].ParcelGeneration!.Objects.ContainsKey("building:"+child.Id),"Protection detached generation ownership");
   }
  });
  yield return("parcel regression detached generated member is never removed by later completion",()=>{
   var p=Complete(ParcelGenerationTests.Fixture());var child=p.Buildings.First(b=>b.SourceKind=="parcel-generator");var before=JsonSerializer.Serialize(child);p.Sites[0].BuildingIds.Remove(child.Id);var after=Complete(p,true,99);Check(after.Buildings.Any(b=>b.Id==child.Id&&JsonSerializer.Serialize(b)==before),"Detached object was still destructively regenerated");
  });
  yield return("parcel regression preserving geographic refresh retains completed children ground and source parcel ownership",()=>{
   var f=Imported();var p=Complete(f.Project);var children=p.Buildings.Where(b=>b.SourceKind=="parcel-generator").ToDictionary(b=>b.Id,b=>JsonSerializer.Serialize(b));Check(children.Count>0,"Fixture has no completed outbuildings");var state=JsonSerializer.Serialize(p.Sites.Single().ParcelGeneration);
   var after=GeographicImporter.Import(f.Json,p,f.Bounds,preserveEdits:true,projection:f.Projection).Project;
   Check(children.All(pair=>after.Buildings.Any(b=>b.Id==pair.Key&&JsonSerializer.Serialize(b)==pair.Value)),"Preserving refresh discarded parcel children");Check(JsonSerializer.Serialize(after.Sites.Single().ParcelGeneration)==state,"Preserving refresh discarded source parcel ownership");
   foreach(var (i,change) in p.Sites.Single().ParcelGeneration!.Ground)Check(after.Terrain[i]==p.Terrain[i],"Preserving refresh reset inferred yard ground");
  });
  yield return("parcel regression clipped roads and fences retain original path association for all fragments",()=>{
   foreach(var fence in new[]{false,true}){var bounds=new GeoBounds{West=45,East=45.003,North=55.003,South=55};var reference=new WorldGeoReference{Bounds=bounds};var affine=new GeographicProjection(reference,100,100);reference.Warp=new(){SourceX=[0,256,512],SourceY=[0,256,512],Destination=[new(0,0),new(256,0),new(512,0),new(0,256),new(256,256),new(512,256),new(0,512),new(256,512),new(512,512)]};
    var points=new[]{(-20d,40d),(550d,40d),(550d,170d),(-20d,170d)}.Select(v=>{var g=affine.Geographic(v.Item1,v.Item2);return new[]{g.Longitude,g.Latitude};}).ToArray();
    var json=JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",id="clipped",properties=fence?new Dictionary<string,string>{{"barrier","fence"}}:new(){{"highway","residential"}},geometry=new{type="LineString",coordinates=points}}}});
    var result=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank",Size=512}),bounds,projection:new(reference,100,100));Check(fence?result.Project.Fences.Count>=2:result.Project.LinearFeatures.Count>=2,"Clipped source lost one of its independent fragments");
   }
  });
 }
 static MapProject Complete(MapProject p,bool regenerate=false,int seed=42)=>ParcelGenerator.Preview(p,new(){X=0,Y=0,Width=p.Width,Height=p.Height},seed,regenerate).Project;
 static (MapProject Project,string Json,GeoBounds Bounds,GeographicProjection Projection) Imported(){var bounds=new GeoBounds{West=45,East=45.004,North=55.004,South=55};var projection=new GeographicProjection(new(){Bounds=bounds},100,100);double[] G(int x,int y){var g=projection.Geographic(x,y);return[g.Longitude,g.Latitude];}var features=new object[]{new{type="Feature",id="road",properties=new Dictionary<string,string>{{"highway","residential"}},geometry=new{type="LineString",coordinates=new[]{G(0,14),G(256,14)}}},new{type="Feature",id="house",properties=new Dictionary<string,string>{{"building","house"},{"building:levels","1"}},geometry=new{type="Polygon",coordinates=new[]{new[]{G(55,30),G(67,30),G(67,40),G(55,40),G(55,30)}}}},new{type="Feature",id="plot",properties=new Dictionary<string,string>{{"pz:parcel","yes"}},geometry=new{type="Polygon",coordinates=new[]{new[]{G(40,20),G(100,20),G(100,150),G(40,150),G(40,20)}}}}};var json=JsonSerializer.Serialize(new{type="FeatureCollection",features});return(GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),bounds,projection:projection).Project,json,bounds,projection);}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
